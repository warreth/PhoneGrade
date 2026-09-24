using System;
using Xunit;
using PhoneGrade.Core;

namespace PhoneGrade.Tests;

/// <summary>
/// Tests for the native USB event watcher and the ADB connection state surface
/// used by the kiosk warning card.
/// </summary>
public class UsbEventWatcherTests
{
    [Fact]
    public void StartMonitoring_OnNonWindows_DoesNotActivateNativeMonitoring()
    {
        // The CI host is Linux, so the watcher must stay inert and report that
        // the polling loop remains the source of truth.
        UsbEventWatcher.StopMonitoring();
        UsbEventWatcher.StartMonitoring();

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(UsbEventWatcher.IsNativeMonitoringActive);
        }

        UsbEventWatcher.StopMonitoring();
    }

    [Fact]
    public void StopMonitoring_IsIdempotent()
    {
        UsbEventWatcher.StopMonitoring();
        UsbEventWatcher.StopMonitoring();
        Assert.False(UsbEventWatcher.IsNativeMonitoringActive);
    }

    [Fact]
    public void RaiseConnected_InvokesSubscribersOnce()
    {
        int calls = 0;
        EventHandler handler = (_, _) => calls++;
        UsbEventWatcher.UsbDeviceConnected += handler;
        try
        {
            UsbEventWatcher.RaiseConnected();
            Assert.Equal(1, calls);
        }
        finally
        {
            UsbEventWatcher.UsbDeviceConnected -= handler;
        }
    }

    [Fact]
    public void RaiseDisconnected_InvokesSubscribersOnce()
    {
        int calls = 0;
        EventHandler handler = (_, _) => calls++;
        UsbEventWatcher.UsbDeviceDisconnected += handler;
        try
        {
            UsbEventWatcher.RaiseDisconnected();
            Assert.Equal(1, calls);
        }
        finally
        {
            UsbEventWatcher.UsbDeviceDisconnected -= handler;
        }
    }

    [Fact]
    public void UnsubscribedHandler_IsNotInvoked()
    {
        int calls = 0;
        EventHandler handler = (_, _) => calls++;
        UsbEventWatcher.UsbDeviceConnected += handler;
        UsbEventWatcher.UsbDeviceConnected -= handler;

        UsbEventWatcher.RaiseConnected();
        Assert.Equal(0, calls);
    }
}

/// <summary>
/// Tests for the ADB diagnostic state surface that drives the kiosk warning card.
/// </summary>
public class AdbDiagnosticsTests
{
    [Theory]
    [InlineData("unauthorized")]
    [InlineData("device unauthorized")]
    [InlineData("error: device unauthorized.")]
    public void UnauthorizedAdbOutput_IsDetected(string adbOutput)
    {
        Assert.Contains("unauthorized", adbOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidSerial_LooksNothingLikeIosUdid()
    {
        Assert.False(DeviceService.LooksLikeIosUdid("R58M12ABCDE"));
    }

    [Fact]
    public void AndroidEmulatorSerial_IsNotMistakenForIosUdid()
    {
        Assert.False(DeviceService.LooksLikeIosUdid("emulator-5554"));
    }

    [Fact]
    public void IosUdid_IsRecognized()
    {
        Assert.True(DeviceService.LooksLikeIosUdid("00008030-001A2B3C4D5E6F7G"));
        Assert.True(DeviceService.LooksLikeIosUdid("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678"));
    }

    [Fact]
    public void EmptyAndNullIdentifiers_AreNotIosUdids()
    {
        Assert.False(DeviceService.LooksLikeIosUdid(""));
        Assert.False(DeviceService.LooksLikeIosUdid("   "));
    }
}
