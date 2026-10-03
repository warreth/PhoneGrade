using PhoneGrade.Core;
using PhoneGrade.Core.Usb;
using Xunit;

namespace Tests;

// ============ When the USB debugging how-to belongs on screen ============
//
// One flag decides that, and three different things write it: the USB event
// stream, the device probe, and the operator's own two buttons. Left to each
// of them they disagree, and the disagreement is visible: pressing
// "Opnieuw zoeken" closed the how-to for an answer that said nothing about the
// phone, which dropped an operator out of the middle of the instructions they
// had come there to read.
//
// So the decision is one pure function. It is tested as a table because that
// is what it is: every answer adb or libimobiledevice can give, and what the
// screen does with it.

public class AdbGuidePolicyTests
{
    [Fact]
    public void APhoneThatDoesNotTrustThisComputer_GetsTheHowTo()
    {
        Assert.Equal(AdbGuideDecision.Show,
            AdbGuidePolicy.Decide(DeviceService.ConnectionState.Unauthorized, dismissed: false));
    }

    [Fact]
    public void APhoneTheOperatorAlreadyDismissed_DoesNotGetItAgain()
    {
        // The dismissal has to survive a refresh, or closing the how-to would
        // only close it until the next probe opened it over the settings page.
        Assert.Equal(AdbGuideDecision.Hide,
            AdbGuidePolicy.Decide(DeviceService.ConnectionState.Unauthorized, dismissed: true));
    }

    [Fact]
    public void APhoneThatIsAlreadyTrusted_TakesItOffScreen()
    {
        // Tapping "always allow" on the phone is what the how-to asked for, so
        // the answer that it worked is the answer that ends it.
        Assert.Equal(AdbGuideDecision.Hide,
            AdbGuidePolicy.Decide(DeviceService.ConnectionState.Connected, dismissed: false));
    }

    [Theory]
    [InlineData(DeviceService.ConnectionState.NotFound)]
    [InlineData(DeviceService.ConnectionState.ToolsMissing)]
    [InlineData(DeviceService.ConnectionState.DaemonStopped)]
    [InlineData(DeviceService.ConnectionState.PermissionDenied)]
    [InlineData(DeviceService.ConnectionState.DriverMissing)]
    [InlineData(DeviceService.ConnectionState.NotTrusted)]
    [InlineData(DeviceService.ConnectionState.NotActivated)]
    public void AnAnswerThatIsNotAboutThisPhone_LeavesTheHowToWhereItIs(DeviceService.ConnectionState state)
    {
        // "adb ran and listed nothing" is not the same claim as "the phone is
        // fine". It is what adb says while a phone is still enumerating, and
        // what it says before the RSA prompt has been answered, and every one
        // of those is a phone the how-to is for. The same goes for a missing
        // tool: the tools being absent says nothing about the phone on the
        // cable. Closing on these is how the guide disappeared mid-read.
        Assert.Equal(AdbGuideDecision.Keep, AdbGuidePolicy.Decide(state, dismissed: false));
        Assert.Equal(AdbGuideDecision.Keep, AdbGuidePolicy.Decide(state, dismissed: true));
    }

    [Fact]
    public void ATrustedPhone_NeverLeavesTheHowToOnScreen()
    {
        // The other half of the same rule, from the other side: once adb has a
        // phone it can talk to, there is nothing left to explain whatever else
        // is in the list.
        Assert.Equal(AdbGuideDecision.Hide,
            AdbGuidePolicy.Decide(DeviceService.ConnectionState.Connected, dismissed: true));
    }
}
