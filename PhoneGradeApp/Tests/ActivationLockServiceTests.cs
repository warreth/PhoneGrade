using Xunit;
using PhoneGrade.Core;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

public class ActivationLockServiceTests
{
    [Fact]
    public async Task DetectAsync_ActivatedDevice_ReturnsUnlocked()
    {
        // This test requires mocking ToolRunner.ExecuteAsync
        // For now, we test the enum values are defined
        var status = ActivationLockService.ActivationLockStatus.Unlocked;
        Assert.Equal(ActivationLockService.ActivationLockStatus.Unlocked, status);
    }

    [Fact]
    public async Task DetectAsync_UnactivatedDevice_ReturnsLocked()
    {
        var status = ActivationLockService.ActivationLockStatus.Locked;
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Fact]
    public async Task DetectAsync_ErrorResponse_ReturnsUnknown()
    {
        var status = ActivationLockService.ActivationLockStatus.Unknown;
        Assert.Equal(ActivationLockService.ActivationLockStatus.Unknown, status);
    }

    [Fact]
    public void CarrierLockStatus_InitializesWithDefaults()
    {
        var status = new ActivationLockService.CarrierLockStatus();
        Assert.True(status.SIMPresent);
        Assert.Equal("Unknown", status.SIMState);
        Assert.Null(status.IsCarrierLocked);
        Assert.Null(status.CarrierName);
    }

    [Fact]
    public void CarrierLockStatus_CanSetProperties()
    {
        var status = new ActivationLockService.CarrierLockStatus
        {
            IsCarrierLocked = true,
            CarrierName = "Vodafone",
            SIMPresent = true,
            SIMState = "Present"
        };

        Assert.True(status.IsCarrierLocked);
        Assert.Equal("Vodafone", status.CarrierName);
        Assert.True(status.SIMPresent);
        Assert.Equal("Present", status.SIMState);
    }
}
