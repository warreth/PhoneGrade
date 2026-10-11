using PhoneGrade.Core;
using PhoneGrade.Core.Android;
using PhoneGrade.Core.Apple;
using Xunit;

namespace Tests;

// ============ What the device data says about locks ============
//
// The summary screen used to print "SIM-free" for every phone because nothing
// was bound to the question at all: a simlocked handset carried a green
// "unlocked" beside a label that printed CARRIER!. These tests pin the three
// answers, and the rule that keeps the Find My source badge off an Unknown.

public class DeviceDataStateTests
{
    [Fact]
    public void ASimLockIsLockedWhenASourceSaysSo()
    {
        var phone = new DeviceData
        {
            CarrierLockIOS = new ActivationLockService.CarrierLockStatus { IsCarrierLocked = true },
        };

        Assert.True(phone.SimLockState);
        Assert.True(phone.SimLockIsLocked);
        Assert.False(phone.SimLockIsFree);
        Assert.False(phone.SimLockIsUnknown);
    }

    [Fact]
    public void ASimLockIsFreeOnlyWhenASourceSaidSo()
    {
        var phone = new DeviceData
        {
            CarrierLockAndroid = new FrpLockService.CarrierLockStatus { IsCarrierLocked = false },
        };

        Assert.False(phone.SimLockState);
        Assert.True(phone.SimLockIsFree);
    }

    [Fact]
    public void WithoutAnAnswerTheSimLockIsUnknown()
    {
        var phone = new DeviceData();
        Assert.Null(phone.SimLockState);
        Assert.True(phone.SimLockIsUnknown);

        // A carrier name with no lock verdict is still no verdict: this is the
        // shape the iPhone read produces, where a card in the tray is not a lock.
        var named = new DeviceData
        {
            CarrierLockIOS = new ActivationLockService.CarrierLockStatus
            {
                CarrierName = "Vodafone",
                IsCarrierLocked = null,
            },
        };
        Assert.True(named.SimLockIsUnknown);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(ActivationLockService.ActivationLockStatus.Unknown, false)]
    [InlineData(ActivationLockService.ActivationLockStatus.Locked, true)]
    [InlineData(ActivationLockService.ActivationLockStatus.Unlocked, true)]
    public void TheFmiSourceBadgeOnlyShowsWithAVerdict(
        ActivationLockService.ActivationLockStatus? status, bool expected)
        => Assert.Equal(expected, new DeviceData { ActivationLock = status }.HasFmiVerdict);
}
