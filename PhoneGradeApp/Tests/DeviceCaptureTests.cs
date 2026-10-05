using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// A fact that records from a handset, and therefore only runs when asked to.
/// The environment variable is the ask:
///
///     PHONEGRADE_CAPTURE=1 dotnet test PhoneGradeApp/Tests/Tests.csproj \
///         --filter FullyQualifiedName~DeviceCaptureTests
///
/// The captures land in <c>Tests/Fixtures/live</c>. Keeping the recorder in the
/// suite means the fixtures can be refreshed on purpose instead of by hand, and
/// a change to <see cref="DeviceCapture"/> shows up as a fixture that no longer
/// has an answer.
/// </summary>
public sealed class CaptureFactAttribute : FactAttribute
{
    public const string Switch = "PHONEGRADE_CAPTURE";

    public CaptureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Switch) != "1")
        {
            Skip = $"Set {Switch}=1 with a handset attached to re-record the device captures.";
        }
    }
}

public class DeviceCaptureTests
{
    [CaptureFact]
    public async Task RecordWhatTheIphoneAnswers()
    {
        Assert.NotNull(DeviceCapture.EnsureToolsOnPath());
        string? udid = await ConnectedDevice.FindIphoneAsync();
        Assert.False(string.IsNullOrWhiteSpace(udid),
            "An iPhone has to be attached and trusted for this to record anything.");

        var domains = await DeviceCapture.CaptureIosDomainsAsync(udid!);
        var ioreg = await DeviceCapture.CaptureIosIORegAsync(udid!);

        DeviceCapture.Write(LiveFixture.Folder, LiveFixture.Iphone8, domains);
        DeviceCapture.Write(LiveFixture.Folder, LiveFixture.Iphone8, ioreg);

        // The disk usage domain is the one read a storage figure comes from, so an
        // empty answer here means the capture is worthless and should not be kept.
        Assert.Contains("<key>TotalDiskCapacity</key>", domains["com-apple-disk-usage"], StringComparison.Ordinal);
    }

    [CaptureFact]
    public async Task RecordWhatTheAndroidAnswers()
    {
        Assert.NotNull(DeviceCapture.EnsureToolsOnPath());
        string? serial = await ConnectedDevice.FindAndroidAsync();
        Assert.False(string.IsNullOrWhiteSpace(serial),
            "An authorized Android handset has to be attached for this to record anything.");

        var commands = await DeviceCapture.CaptureAdbAsync(serial!);
        DeviceCapture.Write(LiveFixture.Folder, LiveFixture.HonorX8b, commands);

        Assert.Contains("getprop", commands.Keys, StringComparer.Ordinal);
    }
}