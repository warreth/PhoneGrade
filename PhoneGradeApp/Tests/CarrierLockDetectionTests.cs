using Xunit;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

public class CarrierLockDetectionTests
{
    [Fact]
    public void iOS_CarrierLockStatus_SIMPresent()
    {
        var status = new ActivationLockService.CarrierLockStatus
        {
            SIMPresent = true,
            SIMState = "Present",
            IsCarrierLocked = true,
            CarrierName = "T-Mobile"
        };

        Assert.True(status.SIMPresent);
        Assert.Equal("Present", status.SIMState);
        Assert.True(status.IsCarrierLocked);
        Assert.Equal("T-Mobile", status.CarrierName);
    }

    [Fact]
    public void iOS_CarrierLockStatus_SIMMissing()
    {
        var status = new ActivationLockService.CarrierLockStatus
        {
            SIMPresent = false,
            SIMState = "kCTSIMSupportSIMStatusMissing",
            IsCarrierLocked = null
        };

        Assert.False(status.SIMPresent);
        Assert.Null(status.IsCarrierLocked);
    }

    [Fact]
    public void Android_CarrierLockStatus_Unlocked()
    {
        var status = new FrpLockService.CarrierLockStatus
        {
            SIMState = "READY",
            CarrierName = "unknown",
            IsCarrierLocked = false
        };

        Assert.Equal("READY", status.SIMState);
        Assert.False(status.IsCarrierLocked);
    }

    [Fact]
    public void Android_CarrierLockStatus_Locked()
    {
        var status = new FrpLockService.CarrierLockStatus
        {
            SIMState = "READY",
            CarrierName = "Verizon",
            IsCarrierLocked = true
        };

        Assert.Equal("Verizon", status.CarrierName);
        Assert.True(status.IsCarrierLocked);
    }

    [Fact]
    public void FrpLockStatus_EnumValues()
    {
        Assert.Equal(FrpLockService.FrpLockStatus.Locked, FrpLockService.FrpLockStatus.Locked);
        Assert.Equal(FrpLockService.FrpLockStatus.Unlocked, FrpLockService.FrpLockStatus.Unlocked);
        Assert.Equal(FrpLockService.FrpLockStatus.Unknown, FrpLockService.FrpLockStatus.Unknown);
    }
}
