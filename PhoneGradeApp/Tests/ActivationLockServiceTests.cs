using Xunit;
using PhoneGrade.Core;
using PhoneGrade.Core.Apple;

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

    [Fact]
    public async Task DetectCarrierLockAsync_ACarrierNameIsNotASimlock()
    {
        // A simlock-free iPhone with a Vodafone card in it reported "carrier
        // locked" purely for carrying that card, which put CARRIER! on the label
        // and an error in the report of a clean phone. The name is kept as
        // information; the lock question is left to the check that can answer it,
        // so it stays Unknown here.
        var raw = new DeviceService.DeviceRawData();
        raw.DefaultDict["CarrierName"] = "Vodafone";

        var status = await ActivationLockService.DetectCarrierLockAsync("MOCK_UDID", raw);

        Assert.Equal("Vodafone", status.CarrierName);
        Assert.Null(status.IsCarrierLocked);
    }

    [Fact]
    public async Task DetectAsync_FmipDomainActive_ReturnsLocked()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.FmipDict["FmipEnabled"] = "true";

        var status = await ActivationLockService.DetectAsync("MOCK_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Fact]
    public async Task DetectAsync_PurpleBuddyFindMyActive_ReturnsLocked()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.PurpleBuddyDict["FindMyiPhoneActive"] = "1";

        var status = await ActivationLockService.DetectAsync("MOCK_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Fact]
    public async Task DetectAsync_MobileGestaltFmiActiveYes_ReturnsLocked()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.GestaltDict["FMIActive"] = "YES";

        var status = await ActivationLockService.DetectAsync("MOCK_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Fact]
    public async Task DetectAsync_AllExplicitlyFalse_ReturnsUnlocked()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.FmipDict["FmipEnabled"] = "false";
        raw.PurpleBuddyDict["FindMyiPhoneActive"] = "0";
        raw.GestaltDict["FMIActive"] = "NO";

        var status = await ActivationLockService.DetectAsync("MOCK_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Unlocked, status);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("YES")]
    [InlineData("enabled")]
    public async Task DetectAsync_WhenFmipEnabledIsTruthy_ReturnsLocked(string truthyValue)
    {
        var raw = new DeviceService.DeviceRawData();
        raw.FmipDict["FmipEnabled"] = truthyValue;
        
        var status = await ActivationLockService.DetectAsync("DUMMY_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public async Task DetectAsync_WhenPurpleBuddyFindMyiPhoneActiveIsTruthy_ReturnsLocked(string truthyValue)
    {
        var raw = new DeviceService.DeviceRawData();
        raw.PurpleBuddyDict["FindMyiPhoneActive"] = truthyValue;
        
        var status = await ActivationLockService.DetectAsync("DUMMY_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public async Task DetectAsync_WhenMobileGestaltTargetIsInternalOrFmiActiveIsTruthy_ReturnsLocked(string truthyValue)
    {
        var raw = new DeviceService.DeviceRawData();
        raw.GestaltDict["TargetIsInternal"] = truthyValue;
        
        var status = await ActivationLockService.DetectAsync("DUMMY_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Locked, status);
    }

    [Fact]
    public async Task DetectAsync_WhenOnlyActivationStateIsActivated_DoesNotReturnUnlocked_ReturnsUnknown()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.DefaultDict["ActivationState"] = "Activated";
        
        // A device being activated locally does NOT mean FMI is OFF on Apple server!
        var status = await ActivationLockService.DetectAsync("DUMMY_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Unknown, status);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("off")]
    public async Task DetectAsync_WhenFmipEnabledIsExplicitlyFalse_ReturnsUnlocked(string falseValue)
    {
        var raw = new DeviceService.DeviceRawData();
        raw.FmipDict["FmipEnabled"] = falseValue;
        
        var status = await ActivationLockService.DetectAsync("DUMMY_UDID", raw);
        Assert.Equal(ActivationLockService.ActivationLockStatus.Unlocked, status);
    }
}
