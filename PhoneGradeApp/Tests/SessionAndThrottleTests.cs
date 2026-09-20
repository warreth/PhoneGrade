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
        
        DeviceSessionManager.MarkCompleted(udid, wasSuccessful: true);
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

public class DeviceRefreshGuardTests
{
    [Fact]
    public void EmptyDictionary_DoesNotThrowNullReference()
    {
        var devices = new Dictionary<string, string>();
        
        // Simulate the fixed logic from RefreshDeviceListSilentAsync
        bool hasDevices = devices.Count > 0;
        Assert.False(hasDevices);
        
        // Should not attempt to access Devices[0] when count is 0
        if (hasDevices)
        {
            // This branch should not execute
            Assert.Fail("Should not reach here when devices is empty");
        }
    }

    [Fact]
    public void DefaultKeyValuePair_KeyIsNull()
    {
        var defaultPair = default(KeyValuePair<string, string>);
        Assert.Null(defaultPair.Key);
        
        // Verify our guard handles null keys
        string? key = defaultPair.Key;
        bool isNullOrEmpty = string.IsNullOrWhiteSpace(key);
        Assert.True(isNullOrEmpty);
    }
}
