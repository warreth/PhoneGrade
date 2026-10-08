using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

/// <summary>
/// Covers the first-run introduction screen: it shows on a fresh install, it is
/// remembered once dismissed so it never interrupts the same operator twice, and
/// a key that turns out to be valid closes it on its own. The tests run the real
/// MainWindowViewModel over a temp settings folder, with the license client
/// pointed at a fake transport so no network is involved.
/// </summary>
public class IntroScreenTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"intro-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public IntroScreenTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        WriteSettings("{}");
    }

    [AvaloniaFact]
    public void FreshInstall_ShowsTheIntroductionScreen()
    {
        using var vm = new MainWindowViewModel();

        Assert.True(vm.IsIntroVisible);
        Assert.False(vm.IsIntroActivationVisible); // the key box stays folded away
    }

    [AvaloniaFact]
    public void DismissIntro_HidesItAndWritesTheFlagToDisk()
    {
        using var vm = new MainWindowViewModel();

        // The terms first. Dismissal is refused until they are accepted, so a test that
        // wants to know whether the flag reaches disk has to accept them like an
        // operator does.
        vm.IsLicenceAccepted = true;
        vm.DismissIntroCommand.Execute().Subscribe();

        Assert.False(vm.IsIntroVisible);

        // The flag has to survive a restart, or every launch would nag again.
        AppSettings reloaded = AppSettings.Load();
        Assert.True(reloaded.IntroSeen);
        Assert.Contains("\"IntroSeen\": true", File.ReadAllText(Path.Combine(_settingsDir, "settings.json")));
    }

    [AvaloniaFact]
    public void StartupAfterDismissing_DoesNotShowTheIntroductionAgain()
    {
        using (var first = new MainWindowViewModel())
        {
            first.IsLicenceAccepted = true;
            first.DismissIntroCommand.Execute().Subscribe();
        }

        using var second = new MainWindowViewModel();

        Assert.False(second.IsIntroVisible);
    }

    [AvaloniaFact]
    public void AnUntickedOperatorCannotDismissTheIntroduction()
    {
        // The counterpart to the tests above, and the reason they had to change. Skip
        // used to be a way past this and now is not, which is the whole point of the
        // page it sits on.
        using var vm = new MainWindowViewModel();
        Assert.True(vm.IsIntroVisible);

        vm.DismissIntroCommand.Execute().Subscribe();

        Assert.True(vm.IsIntroVisible);
        Assert.False(AppSettings.Load().IntroSeen);
    }

    [AvaloniaFact]
    public void ShowIntroActivation_RevealsTheKeyBoxWithoutDismissing()
    {
        using var vm = new MainWindowViewModel();

        vm.ShowIntroActivationCommand.Execute().Subscribe();

        Assert.True(vm.IsIntroActivationVisible);
        Assert.True(vm.IsIntroVisible); // asking for the box is not a goodbye
    }

    [AvaloniaFact]
    public void TheIntroductionCountsItsSteps()
    {
        // The footer used to carry the five page names as a row of tabs. The
        // wizard shows a progress line and a number now, and the number has to
        // follow the page rather than lag a click behind.
        using var vm = new MainWindowViewModel();

        Assert.Equal(1, vm.IntroStepNumber);
        Assert.Equal("1 / 5", vm.IntroStepDisplay);

        vm.IntroPage = "Workflow";

        Assert.Equal(3, vm.IntroStepNumber);
        Assert.Equal("3 / 5", vm.IntroStepDisplay);

        vm.IntroPage = MainWindowViewModel.LicencePage;

        Assert.Equal("4 / 5", vm.IntroStepDisplay);
    }

    [AvaloniaFact]
    public void TheIntroductionCanBeReplayedFromSettings()
    {
        using var vm = new MainWindowViewModel();

        // Get past the first run the way an operator does.
        vm.IsLicenceAccepted = true;
        vm.DismissIntroCommand.Execute().Subscribe();
        Assert.False(vm.IsIntroVisible);

        vm.IsSettingsDrawerOpen = true;
        vm.ReplayIntroCommand.Execute().Subscribe();

        Assert.False(vm.IsSettingsDrawerOpen);
        Assert.True(vm.IsIntroVisible);
        Assert.Equal("Language", vm.IntroPage);

        // The terms were accepted on this machine to get this far, so a second
        // look is a look and not a second agreement.
        Assert.True(vm.CanSkipIntro, "the replay asked for the terms again");

        vm.DismissIntroCommand.Execute().Subscribe();

        Assert.False(vm.IsIntroVisible);
        Assert.True(AppSettings.Load().IntroSeen);
    }

    [AvaloniaFact]
    public async Task ValidKeyOnTheIntroductionScreen_TurnsProAndKeepsTheTermsInFront()
    {
        using var server = new LicensingTestContext.FakeLicenseServer
        {
            ResponseJson = LicensingTestContext.FakeLicenseServer.ActiveJson
        };
        using var client = new PhoneGrade.Core.Licensing.LemonSqueezyClient(server);
        using var vm = new MainWindowViewModel(client);

        Assert.True(vm.IsIntroVisible);

        vm.ShowIntroActivationCommand.Execute().Subscribe();
        Assert.True(vm.IsIntroActivationVisible);

        vm.Licensing!.LicenseKeyInput = "KEY-VALID-1";
        await vm.Licensing.ValidateCommand.Execute();

        Assert.True(vm.IsProLicenseActive);

        // The screen used to fold itself away the moment a key turned out to be valid,
        // on the reasoning that it exists to explain the free tier. It does not any
        // more, because the operator is now a paying customer and a paying customer
        // is the one who has to be shown the commercial end user licence agreement.
        // Closing the screen here would grant a Pro licence under terms nobody agreed
        // to, which is precisely the gap the document was written to close.
        Assert.True(vm.IsIntroVisible, "a valid key dismissed the screen before the terms were accepted");

        // And the page it lands on is the one carrying the commercial terms, not the
        // repository licence a free operator sees.
        Assert.Equal(MainWindowViewModel.LicencePage, "Licence");
        Assert.Contains("COMMERCIAL", PhoneGrade.UI.Services.LicenceTerms.DocumentFor(vm.Licensing.IsPro));
        Assert.False(AppSettings.Load().IntroSeen);
    }

    [AvaloniaFact]
    public async Task ValidKeyOnTheIntroductionScreen_ClosesItOnceTheTermsAreAccepted()
    {
        using var server = new LicensingTestContext.FakeLicenseServer
        {
            ResponseJson = LicensingTestContext.FakeLicenseServer.ActiveJson
        };
        using var client = new PhoneGrade.Core.Licensing.LemonSqueezyClient(server);
        using var vm = new MainWindowViewModel(client);

        vm.ShowIntroActivationCommand.Execute().Subscribe();
        vm.Licensing!.LicenseKeyInput = "KEY-VALID-1";
        await vm.Licensing.ValidateCommand.Execute();

        Assert.True(vm.IsProLicenseActive);

        vm.IsLicenceAccepted = true;
        vm.DismissIntroCommand.Execute().Subscribe();

        Assert.False(vm.IsIntroVisible);
        Assert.True(AppSettings.Load().IntroSeen);
    }

    [AvaloniaFact]
    public async Task InvalidKeyOnTheIntroductionScreen_LeavesItOpen()
    {
        using var server = new LicensingTestContext.FakeLicenseServer
        {
            ResponseJson = LicensingTestContext.FakeLicenseServer.InvalidKeyJson
        };
        using var client = new PhoneGrade.Core.Licensing.LemonSqueezyClient(server);
        using var vm = new MainWindowViewModel(client);

        vm.ShowIntroActivationCommand.Execute().Subscribe();
        vm.Licensing!.LicenseKeyInput = "KEY-NOPE";
        await vm.Licensing.ValidateCommand.Execute();

        Assert.False(vm.IsProLicenseActive);
        Assert.True(vm.IsIntroVisible); // a failed attempt still needs the screen that explains the plans
        Assert.False(AppSettings.Load().IntroSeen);
    }

    [AvaloniaFact]
    public void PricingLink_PointsAtThePricingPageAndOpensThroughTheShell()
    {
        var startInfo = PhoneGrade.UI.Services.PricingLink.BuildOpen(PhoneGrade.UI.Services.PricingLink.Url);

        Assert.Equal("https://phonegrade.app/pricing", startInfo.FileName);
        // Shell execute is what makes the link land in the default browser on
        // Windows, macOS and Linux alike; without it the OS looks up an app for
        // the scheme and fails on the desktops this app ships to.
        Assert.True(startInfo.UseShellExecute);
    }

    [AvaloniaFact]
    public void PricingLink_RefusesAnEmptyTarget()
    {
        Assert.Throws<ArgumentException>(() => PhoneGrade.UI.Services.PricingLink.BuildOpen(""));
        Assert.Throws<ArgumentException>(() => PhoneGrade.UI.Services.PricingLink.BuildOpen("   "));
    }

    private void WriteSettings(string json) =>
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), json);

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        try
        {
            if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
        }
        catch { /* temp cleanup never fails a test */ }
    }
}
