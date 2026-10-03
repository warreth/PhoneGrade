using System;
using System.Threading.Tasks;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// What adb currently reports about the phones plugged in.
///
/// <see cref="Ran"/> is false when adb could not be run at all, which is a
/// different answer from "ran and listed nothing": the first says the tool is
/// missing, the second says no phone has switched debugging on yet. Both leave
/// the guide open, but for different reasons, so they are kept apart.
/// </summary>
public readonly record struct AdbDeviceListState(bool Ran, bool AnyAuthorized, bool AnyUnauthorized);

/// <summary>
/// Reads the adb device list. Seamed so the director can be exercised without
/// a phone, an adb binary, or a person to unlock one.
/// </summary>
public interface IAdbDeviceList
{
    Task<AdbDeviceListState> ReadAsync();
}

/// <summary>The real adb, as it answers today.</summary>
public sealed class AdbDeviceList : IAdbDeviceList
{
    public async Task<AdbDeviceListState> ReadAsync()
    {
        var (stdout, _, exitCode) = await ToolRunner.ExecuteAsync("adb", "devices");

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return new AdbDeviceListState(Ran: false, AnyAuthorized: false, AnyUnauthorized: false);

        bool anyAuthorized = false;
        bool anyUnauthorized = false;

        foreach (string raw in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (parts[1].Equals("device", StringComparison.OrdinalIgnoreCase)) anyAuthorized = true;
            else if (parts[1].Equals("unauthorized", StringComparison.OrdinalIgnoreCase)) anyUnauthorized = true;
        }

        return new AdbDeviceListState(Ran: true, anyAuthorized, anyUnauthorized);
    }
}
