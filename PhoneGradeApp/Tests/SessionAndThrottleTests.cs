using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

public class LogThrottlerTests
{
    [Fact]
    public void ShouldLog_FirstMessage_ReturnsTrue()
    {
        LogThrottler.Reset();
        bool result = LogThrottler.ShouldLog("Test message", LogSource.UsbDetector);
        Assert.True(result);
    }

    [Fact]
    public void ShouldLog_DuplicateWithinWindow_ReturnsFalse()
    {
        LogThrottler.Reset();
        string msg = "Critical USB tools missing";
        
        bool first = LogThrottler.ShouldLog(msg, LogSource.UsbDetector);
        bool second = LogThrottler.ShouldLog(msg, LogSource.UsbDetector);
        
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void ShouldLog_DifferentMessages_BothReturnTrue()
    {
        LogThrottler.Reset();
        bool msg1 = LogThrottler.ShouldLog("Message A", LogSource.UsbDetector);
        bool msg2 = LogThrottler.ShouldLog("Message B", LogSource.UsbDetector);
        
        Assert.True(msg1);
        Assert.True(msg2);
    }

    [Fact]
    public void ShouldLog_DifferentSources_BothReturnTrue()
    {
        LogThrottler.Reset();
        string msg = "Same message";
        bool detector = LogThrottler.ShouldLog(msg, LogSource.UsbDetector);
        bool desktop = LogThrottler.ShouldLog(msg, LogSource.Desktop);
        
        Assert.True(detector);
        Assert.True(desktop);
    }

    [Fact]
    public async Task ShouldLog_AfterThrottleWindow_ReturnsTrue()
    {
        LogThrottler.Reset();
        string msg = "Repeated error";
        
        bool first = LogThrottler.ShouldLog(msg, LogSource.UsbDetector);
        Assert.True(first);
        
        await Task.Delay(5100); // Wait for throttle window (5s) to expire
        
        bool second = LogThrottler.ShouldLog(msg, LogSource.UsbDetector);
        Assert.True(second);
    }

    [Fact]
    public void ShouldLog_IdlePollingMessages_ThrottledImmediately()
    {
        LogThrottler.Reset();
        string adbMsg = "[adb] stdout: List of devices attached";
        string ideviceMsg = "[ideviceinfo] stdout: ERROR: No device found!";
        
        Assert.True(LogThrottler.ShouldLog(adbMsg, LogSource.Desktop));
        Assert.False(LogThrottler.ShouldLog(adbMsg, LogSource.Desktop));
        
        Assert.True(LogThrottler.ShouldLog(ideviceMsg, LogSource.Desktop));
        Assert.False(LogThrottler.ShouldLog(ideviceMsg, LogSource.Desktop));
    }
}

public class DeviceSessionManagerTests
{
    [Fact]
    public void IsDeviceCompleted_NewDevice_ReturnsFalse()
    {
        DeviceSessionManager.ClearAll();
        bool completed = DeviceSessionManager.IsDeviceCompleted("abc123");
        Assert.False(completed);
    }

    [Fact]
    public void MarkCompleted_DeviceMarked_IsDeviceCompletedReturnsTrue()
    {
        DeviceSessionManager.ClearAll();
        string udid = "test-udid-123";
        
        DeviceSessionManager.MarkCompleted(udid);
        bool completed = DeviceSessionManager.IsDeviceCompleted(udid);
        
        Assert.True(completed);
    }

    [Fact]
    public void ResetDevice_CompletedDevice_IsDeviceCompletedReturnsFalse()
    {
        DeviceSessionManager.ClearAll();
        string udid = "test-udid-456";
        
        DeviceSessionManager.MarkCompleted(udid);
        Assert.True(DeviceSessionManager.IsDeviceCompleted(udid));
        
        DeviceSessionManager.ResetDevice(udid);
        Assert.False(DeviceSessionManager.IsDeviceCompleted(udid));
    }

    [Fact]
    public void MarkCompleted_NullUdid_DoesNotThrow()
    {
        DeviceSessionManager.ClearAll();
        DeviceSessionManager.MarkCompleted(null!);
        DeviceSessionManager.MarkCompleted("");
        // No assertion needed, just verify no exception
    }

    [Fact]
    public void ClearAll_RemovesAllSessions()
    {
        DeviceSessionManager.ClearAll();
        DeviceSessionManager.MarkCompleted("device1");
        DeviceSessionManager.MarkCompleted("device2");
        
        DeviceSessionManager.ClearAll();
        
        Assert.False(DeviceSessionManager.IsDeviceCompleted("device1"));
        Assert.False(DeviceSessionManager.IsDeviceCompleted("device2"));
    }
}

public class DevicePresenceTrackerTests
{
    /// <summary>Clock the tests drive by hand, so no test ever has to sleep.</summary>
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (DevicePresenceTracker Tracker, FakeClock Clock) NewTracker(double graceSeconds = 7.5)
    {
        var clock = new FakeClock();
        return (new DevicePresenceTracker(TimeSpan.FromSeconds(graceSeconds), () => clock.Now), clock);
    }

    [Fact]
    public void ConnectedDevice_IsNeverUnplugged()
    {
        var (tracker, _) = NewTracker();
        for (int i = 0; i < 50; i++) tracker.Report(1);
        Assert.False(tracker.IsUnplugged);
        Assert.Null(tracker.AbsentFor);
    }

    [Fact]
    public void OneEmptyPoll_KeepsTheSessionAlive()
    {
        // The reported bug: a single adb hiccup reset the whole inspection.
        var (tracker, clock) = NewTracker();
        tracker.Report(1);

        tracker.Report(0);
        clock.Advance(2.5); // one polling interval
        Assert.False(tracker.IsUnplugged);
    }

    [Fact]
    public void AbsentAcrossSeveralPolls_StaysBelowTheGrace()
    {
        // Two empty polls, five seconds, session intact.
        var (tracker, clock) = NewTracker();
        tracker.Report(1);

        for (int i = 0; i < 2; i++)
        {
            tracker.Report(0);
            clock.Advance(2.5);
            Assert.False(tracker.IsUnplugged);
        }
    }

    [Fact]
    public void ThirdEmptyPoll_ReachesTheGrace()
    {
        var (tracker, clock) = NewTracker();
        tracker.Report(1);

        for (int i = 0; i < 3; i++)
        {
            tracker.Report(0);
            clock.Advance(2.5);
        }

        Assert.True(tracker.IsUnplugged);
    }

    [Fact]
    public void AbsentLongerThanTheGrace_CountsAsUnplugged()
    {
        var (tracker, clock) = NewTracker();
        tracker.Report(1);

        tracker.Report(0);
        clock.Advance(6.0);
        Assert.False(tracker.IsUnplugged);

        clock.Advance(2.0);
        Assert.True(tracker.IsUnplugged);
    }

    [Fact]
    public void ReconnectBeforeTheGrace_ClearsTheAbsence()
    {
        var (tracker, clock) = NewTracker();
        tracker.Report(1);

        tracker.Report(0);
        clock.Advance(5.0);
        tracker.Report(1);
        clock.Advance(60.0);

        Assert.False(tracker.IsUnplugged);
        Assert.Null(tracker.AbsentFor);
    }

    [Fact]
    public void RepeatedEmptyReports_DoNotExtendTheGrace()
    {
        // The polling loop and the USB event both report, so the clock, not the
        // report count, has to decide. Otherwise a device that keeps being
        // probed would never be called unplugged.
        var (tracker, clock) = NewTracker();
        tracker.Report(1);

        tracker.Report(0);
        for (int i = 0; i < 20; i++)
        {
            clock.Advance(0.5);
            tracker.Report(0);
        }

        Assert.True(tracker.IsUnplugged);
    }

    [Fact]
    public void AbsentFor_GrowsFromTheFirstEmptyReport()
    {
        var (tracker, clock) = NewTracker();
        tracker.Report(1);
        tracker.Report(0);

        clock.Advance(3.25);
        Assert.Equal(TimeSpan.FromSeconds(3.25), tracker.AbsentFor);
    }

    [Fact]
    public void Reset_ClearsTheAbsence()
    {
        var (tracker, clock) = NewTracker();
        tracker.Report(0);
        clock.Advance(60.0);
        Assert.True(tracker.IsUnplugged);

        tracker.Reset();
        Assert.False(tracker.IsUnplugged);
    }

    [Fact]
    public void ZeroGrace_DisconnectsOnTheNextObservation()
    {
        var clock = new FakeClock();
        var tracker = new DevicePresenceTracker(TimeSpan.Zero, () => clock.Now);
        tracker.Report(0);
        Assert.True(tracker.IsUnplugged);
    }

    [Fact]
    public void NegativeGrace_IsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DevicePresenceTracker(TimeSpan.FromSeconds(-1), () => DateTimeOffset.UtcNow));

    [Fact]
    public void DefaultGrace_CoversThreePollingIntervals()
    {
        // StartWatcher polls every 2.5s, so the default has to span three of
        // them for the "wait a bit longer" behaviour to actually hold.
        Assert.Equal(TimeSpan.FromSeconds(7.5), DevicePresenceTracker.DefaultGrace);
    }
}
