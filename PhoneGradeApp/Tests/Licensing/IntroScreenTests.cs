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
            first.DismissIntroCommand.Execute().Subscribe();
        }

        using var second = new MainWindowViewModel();

        Assert.False(second.IsIntroVisible);
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
    public async Task ValidKeyOnTheIntroductionScreen_ClosesItAndTurnsPro()
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
        Assert.False(vm.IsIntroVisible); // the screen explains the free tier, Pro needs no explanation
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
