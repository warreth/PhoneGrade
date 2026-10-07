using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core;
using Xunit;

namespace Tests;

/// <summary>Which handset a live fact needs on the cable.</summary>
public enum DeviceOn
{
    /// <summary>Any handset answers the question.</summary>
    Any,

    /// <summary>The fact needs an Android phone.</summary>
    Android,

    /// <summary>The fact needs an iPhone.</summary>
    Ios
}

/// <summary>
/// A fact that reads the real handset attached to this machine.
///
/// It reports itself as skipped, with the reason, when nothing answers the probe:
/// a machine in CI, a laptop with no cable in it, or a working copy where the app
/// has never run and therefore never fetched the adb and idevice tools beside
/// the binaries. A fact that quietly returned instead would look the same as a
/// fact that passed.
///
/// The probe is done once per run and shared: finding the handsets costs an adb
/// call and an idevice call, and every fact would otherwise repeat them.
/// </summary>
public sealed class AttachedDeviceFactAttribute : FactAttribute
{
    private static readonly object Gate = new();
    private static (DateTime At, List<(string Id, bool Android)> Devices, string? Error)? _snapshot;

    public AttachedDeviceFactAttribute(DeviceOn needs = DeviceOn.Any)
    {
        var (devices, error) = Snapshot();
        if (error is not null)
        {
            Skip = error;
            return;
        }

        if (devices.Count == 0)
        {
            Skip = "No handset answered: plug an Android phone or an iPhone in, "
                + "and make sure the app has fetched the adb and idevice tools beside the binaries.";
            return;
        }

        if (needs == DeviceOn.Android && !devices.Any(d => d.Android))
        {
            Skip = "Only an iPhone is attached, and this fact needs an Android handset.";
            return;
        }

        if (needs == DeviceOn.Ios && devices.All(d => d.Android))
        {
            Skip = "Only an Android handset is attached, and this fact needs an iPhone.";
        }
    }

    private static (List<(string Id, bool Android)> Devices, string? Error) Snapshot()
    {
        lock (Gate)
        {
            if (_snapshot is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(60))
            {
                return (cached.Devices, cached.Error);
            }

            var probe = Task.Run(async () =>
            {
                var (devices, _) = await DeviceService.GetConnectedDevicesWithStateAsync();
                var list = new List<(string, bool)>();
                foreach (var id in devices.Keys)
                {
                    list.Add((id, await DeviceService.IsAndroidDeviceAsync(id)));
                }
                return list;
            });

            // A probe, not a test: a wedged daemon must produce a skip, not a hung run.
            if (!probe.Wait(TimeSpan.FromSeconds(45)))
            {
                _snapshot = (DateTime.UtcNow, new List<(string, bool)>(), "The device probe did not answer within 45 seconds.");
                return (_snapshot.Value.Devices, _snapshot.Value.Error);
            }

            try
            {
                var devices = probe.Result;
                _snapshot = (DateTime.UtcNow, devices, null);
                return (devices, null);
            }
            catch (Exception ex)
            {
                var reason = $"The device probe failed: {ex.GetBaseException().Message}";
                _snapshot = (DateTime.UtcNow, new List<(string, bool)>(), reason);
                return (_snapshot.Value.Devices, reason);
            }
        }
    }
}

/// <summary>
/// The same reads the application performs, run against the handsets actually on
/// the cable.
///
/// Every read goes through <see cref="DeviceService"/>, so a fact passing here is
/// a statement about the product's own reader rather than about a capture of one.
/// The captures under <c>Fixtures/live</c> keep the same readers honest in CI;
/// these facts keep them honest against today's hardware.
/// </summary>
public class AttachedDeviceTests
{
    [AttachedDeviceFact]
    public async Task EveryAttachedHandsetIsReadWithoutFallingOver()
    {
        var (devices, state) = await DeviceService.GetConnectedDevicesWithStateAsync();

        Assert.True(devices.Count > 0, $"the probe saw a handset but the read found none (state: {state})");

        var failures = new List<string>();
        foreach (var pair in devices)
        {
            try
            {
                if (await DeviceService.IsAndroidDeviceAsync(pair.Key))
                {
                    var data = await DeviceService.GetAndroidDeviceDataAsync(pair.Key);
                    Assert.False(string.IsNullOrWhiteSpace(data.Model), "no model was read");
                }
                else
                {
                    var data = await DeviceService.GetDeviceDataAsync(pair.Key);
                    Assert.False(string.IsNullOrWhiteSpace(data.Model), "no model was read");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{pair.Key}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, "a read threw where it should report: " + string.Join("; ", failures));
    }

    [AttachedDeviceFact(DeviceOn.Android)]
    public async Task TheAndroidHandsetAnswersThroughTheProductReader()
    {
        var data = await ReadTheAndroidOneAsync();

        Assert.False(string.IsNullOrWhiteSpace(data.Model), "the model came back empty");
        Assert.Contains("Android", data.IosVersion ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(data.Identifier),
            "neither an IMEI nor the serial was read; a refusal is fine but something identifiable has to remain");
        Assert.False(string.IsNullOrWhiteSpace(data.Storage), "the storage came back empty");
        Assert.True(data.BatteryLevel > 0, "dumpsys reported no charge level");
    }

    [AttachedDeviceFact(DeviceOn.Ios)]
    public async Task TheIphoneAnswersThroughTheProductReader()
    {
        var data = await ReadTheIphoneOneAsync();

        Assert.False(string.IsNullOrWhiteSpace(data.Model), "the model came back empty");
        Assert.False(string.IsNullOrWhiteSpace(data.MotherboardSerialNumber), "the serial came back empty");
        Assert.False(string.IsNullOrWhiteSpace(data.IosVersion), "the iOS version came back empty");
        Assert.False(string.IsNullOrWhiteSpace(data.Identifier), "no IMEI was read and no serial fell back");
    }

    private static async Task<DeviceData> ReadTheAndroidOneAsync()
    {
        var (devices, _) = await DeviceService.GetConnectedDevicesWithStateAsync();
        foreach (var id in devices.Keys)
        {
            if (await DeviceService.IsAndroidDeviceAsync(id))
            {
                return await DeviceService.GetAndroidDeviceDataAsync(id);
            }
        }

        throw new InvalidOperationException("the probe saw an Android handset and the read found none");
    }

    private static async Task<DeviceData> ReadTheIphoneOneAsync()
    {
        var (devices, _) = await DeviceService.GetConnectedDevicesWithStateAsync();
        foreach (var id in devices.Keys)
        {
            if (!await DeviceService.IsAndroidDeviceAsync(id))
            {
                return await DeviceService.GetDeviceDataAsync(id);
            }
        }

        throw new InvalidOperationException("the probe saw an iPhone and the read found none");
    }
}
