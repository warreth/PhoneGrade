using PhoneGrade.Core.Usb;
using Xunit;

namespace Tests;

// ============ What `adb devices` actually prints ============
//
// Two places read this list: the director that decides whether to open the
// how-to, and the device probe that decides what the status line says. They
// each parsed it themselves, which is how one list came to be read two ways,
// so it is parsed once here.
//
// The state that matters is `authorizing`, the one a phone sits in while it is
// showing the RSA prompt. It is one word away from `unauthorized`, and the
// probe used to fall past it to "nothing found" - so the phone asking for
// permission was the one phone the app could not see.

public class AdbDeviceListParseTests
{
    private const string Header = "List of devices attached";

    [Fact]
    public void APhoneShowingTheRsaPrompt_CountsAsUnanswered()
    {
        var (state, trusted) = AdbDeviceList.Parse(
            $"{Header}\nR58M123456A\tauthorizing\n", exitCode: 0);

        Assert.True(state.Ran);
        Assert.Empty(trusted);
        Assert.False(state.AnyAuthorized);
        Assert.True(state.AnyUnauthorized,
            "a phone waiting for the operator to tap Allow is the exact case the " +
            "how-to exists for, and here it reads as an empty bench");
    }

    [Fact]
    public void APhoneThatAlreadyAllowedThisComputer_IsListedBySerial()
    {
        var (state, trusted) = AdbDeviceList.Parse(
            $"{Header}\nR58M123456A\tdevice\n", exitCode: 0);

        Assert.True(state.Ran);
        Assert.True(state.AnyAuthorized);
        Assert.False(state.AnyUnauthorized);
        Assert.Equal(new[] { "R58M123456A" }, trusted);
    }

    [Fact]
    public void APhoneThatRefused_StillCountsAsUnanswered()
    {
        var (state, _) = AdbDeviceList.Parse(
            $"{Header}\nR58M123456A\tunauthorized\n", exitCode: 0);

        Assert.True(state.AnyUnauthorized);
        Assert.False(state.AnyAuthorized);
    }

    [Fact]
    public void AnEmptyList_IsADifferentAnswerFromAToolThatDidNotRun()
    {
        // The two are not interchangeable: one is a bench with no phone on it,
        // the other is a missing adb. The guide reads them the same way but the
        // status line does not, so the distinction has to survive the parse.
        var (empty, _) = AdbDeviceList.Parse($"{Header}\n", exitCode: 0);
        var (failed, _) = AdbDeviceList.Parse("", exitCode: 1);

        Assert.True(empty.Ran);
        Assert.False(failed.Ran);
    }

    [Fact]
    public void AnAnswerWithNoHeaderLine_IsStillRead()
    {
        // adb is asked for the list with an attached device sometimes in the
        // same breath as it starts its daemon, and the header is the first
        // thing to go when the output is truncated. Dropping the rest of it
        // would hide a phone that is right there.
        var (state, trusted) = AdbDeviceList.Parse("R58M123456A\tdevice\n", exitCode: 0);

        Assert.True(state.Ran);
        Assert.Equal(new[] { "R58M123456A" }, trusted);
    }
}
