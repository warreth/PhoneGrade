using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core.Licensing;
using PhoneGrade.UI.ViewModels;
using Xunit;
using static Tests.LicensingTestContext;

namespace Tests;

/// <summary>
/// Covers what the license panel says about the seat and what the Release seat
/// button does, driven through the real view model over the real gate and a fake
/// transport.
///
/// The release button is the part a shop needs when a bench is retired, and the
/// fault it must not have is firing on a first click: that would put a paying
/// customer back on the free tier with no way back from the panel.
/// </summary>
public class LicenseSeatPanelTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"seats-{Guid.NewGuid():N}");
    private readonly string? _originalSettingsDir = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public LicenseSeatPanelTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    // ---- what the panel says -----------------------------------------------------

    [AvaloniaFact]
    public async Task AfterActivating_ThePanelShowsTheSeatCountAndThisComputer()
    {
        using var server = new FakeLicenseServer
        {
            ActivateResponseJson = FakeLicenseServer.ActivatedWithSeatsJson
        };
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        vm.Licensing!.LicenseKeyInput = "KEY-1";
        await vm.Licensing.ValidateCommand.Execute();

        Assert.True(vm.IsProLicenseActive);

        // Both numbers come from the vendor's answer, so "3 of 5" is drawn as sent.
        // Asserting on the raw numbers rather than the sentence keeps this from
        // breaking on a wording change while still pinning that the app did not
        // substitute a number of its own.
        Assert.Equal(3, vm.Licensing!.MachineCount);
        Assert.Equal(5, vm.Licensing.MachineLimit);
        Assert.True(vm.Licensing.HasSeatNumbers);
        Assert.Contains("3", vm.Licensing.SeatUsageText);
        Assert.Contains("5", vm.Licensing.SeatUsageText);
        // One line, naming this machine. The short form comes from the machine the test
        // is running on rather than from a fixture, because this view model is
        // built by MainWindowViewModel and reads the real fingerprint. What is
        // pinned here is that the panel and the gate agree, not what this
        // particular computer is called.
        Assert.Equal(1, vm.Licensing.SeatLines.Count); // one instance per answer
        Assert.Contains(MachineFingerprint.Current.ShortValue, vm.Licensing.SeatLines[0]);
        Assert.True(vm.Licensing.CanReleaseSeat);
        Assert.False(vm.Licensing.CannotIdentifyMachine);
    }

    [AvaloniaFact]
    public void OnTheFreeTier_ThePanelOffersNoSeatLineAndNoReleaseButton()
    {
        using var server = new FakeLicenseServer();
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        Assert.False(vm.Licensing!.HasSeatNumbers);
        Assert.Equal("", vm.Licensing.SeatUsageText);
        Assert.False(vm.Licensing.CanReleaseSeat);
    }

    [AvaloniaFact]
    public async Task WhenTheVendorReportsNoLimit_ThePanelDrawsNoSeatFraction()
    {
        // "0 of 0 computers in use" would be a lie: zero here means the API did not
        // say, not that the plan allows nothing.
        using var server = new FakeLicenseServer
        {
            ActivateResponseJson = FakeLicenseServer.ActivatedNoLimitJson
        };
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        vm.Licensing!.LicenseKeyInput = "KEY-1";
        await vm.Licensing.ValidateCommand.Execute();

        Assert.True(vm.IsProLicenseActive);
        Assert.Equal(0, vm.Licensing.MachineLimit);
        Assert.False(vm.Licensing.HasSeatNumbers);
        Assert.Equal("", vm.Licensing.SeatUsageText);
        Assert.True(vm.Licensing.CanReleaseSeat); // a seat was still claimed
    }

    [AvaloniaFact]
    public async Task AtTheSeatLimit_ThePanelSaysSoRatherThanClaimingAnInvalidKey()
    {
        // The operator has to know this computer is the one over the limit. An
        // "invalid key" message would send them looking for a typo in a good key.
        using var server = new FakeLicenseServer
        {
            ActivateResponseJson = FakeLicenseServer.ActivationLimitJson
        };
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        vm.Licensing!.LicenseKeyInput = "KEY-AT-LIMIT";
        await vm.Licensing.ValidateCommand.Execute();

        Assert.False(vm.IsProLicenseActive);

        // The key comes back as the key itself when a resource is missing, so
        // comparing against the key name is how this tells "the panel said the
        // limit" apart from "the panel said nothing useful".
        Assert.NotEqual("Settings_ActivationInvalid", vm.Licensing.StatusMessage);
        Assert.NotEqual("Settings_ActivationLimitReached", vm.Licensing.StatusMessage);
        Assert.False(vm.Licensing.CanReleaseSeat);

        // The counters from the refusal are worth showing.
        Assert.Equal(2, vm.Licensing.MachineLimit);
    }

    // ---- the release button -------------------------------------------------------

    [AvaloniaFact]
    public async Task ReleaseSeatButton_FirstClickOnlyAsksForConfirmation()
    {
        using var server = new FakeLicenseServer();
        using var vm = await ActivatedAsync(server);

        await vm.Licensing!.ReleaseSeatCommand.Execute();

        Assert.True(vm.Licensing.IsReleaseArmed);
        Assert.True(vm.IsProLicenseActive);      // nothing happened yet
        Assert.False(vm.Licensing.IsReleasing);
        Assert.DoesNotContain(LemonSqueezyClient.DeactivateEndpoint, server.Endpoints);
    }

    [AvaloniaFact]
    public async Task ReleaseSeatButton_SecondClickHandsTheSeatBackAndDropsToFree()
    {
        using var server = new FakeLicenseServer();
        using var vm = await ActivatedAsync(server);

        await vm.Licensing!.ReleaseSeatCommand.Execute(); // ask
        await vm.Licensing.ReleaseSeatCommand.Execute();  // do it

        Assert.False(vm.IsProLicenseActive);
        Assert.False(vm.Licensing.CanReleaseSeat);
        Assert.False(vm.Licensing.IsReleaseArmed);
        Assert.False(vm.Licensing.IsReleasing);
        Assert.Contains(LemonSqueezyClient.DeactivateEndpoint, server.Endpoints);
        Assert.NotEqual("", vm.Licensing.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ReleaseSeatButton_CancelKeepsTheSeat()
    {
        using var server = new FakeLicenseServer();
        using var vm = await ActivatedAsync(server);

        await vm.Licensing!.ReleaseSeatCommand.Execute();
        await vm.Licensing.CancelReleaseCommand.Execute();

        Assert.False(vm.Licensing.IsReleaseArmed);
        Assert.True(vm.IsProLicenseActive);
        Assert.DoesNotContain(LemonSqueezyClient.DeactivateEndpoint, server.Endpoints);
    }

    [AvaloniaFact]
    public async Task ReleaseSeatButton_AfterARelease_AFreshScanIsAFreeScan()
    {
        // The button has to actually take effect on the next scan, not only in the
        // panel: a released seat that still scans for free would make the whole
        // button cosmetic.
        using var server = new FakeLicenseServer();
        using var vm = await ActivatedAsync(server);

        await vm.Licensing!.ReleaseSeatCommand.Execute();
        await vm.Licensing.ReleaseSeatCommand.Execute();

        // The key itself is still valid on the vendor's side, so this is the case
        // where a gate that only asked about the key would hand out Pro again.
        server.ResponseJson = FakeLicenseServer.ActiveJson;

        Assert.True(await vm.PassScanGateAsync());
        Assert.Equal(1, vm.TrialScanCount); // the released seat costs a free scan
        Assert.False(vm.IsTrialLimitReached);
    }

    [AvaloniaFact]
    public async Task ReleaseSeatButton_WithNoSeat_DoesNothingAndSaysNothing()
    {
        using var server = new FakeLicenseServer();
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        await vm.Licensing!.ReleaseSeatCommand.Execute();
        await vm.Licensing.ReleaseSeatCommand.Execute();

        Assert.False(vm.Licensing.IsReleaseArmed);
        Assert.Equal(0, server.CallCount);
    }

    [AvaloniaFact]
    public async Task ActivatingThenReopeningThePanel_ShowsTheSeatWithoutActivatingAgain()
    {
        // The startup path asks about the stored seat rather than taking a new one,
        // so a shop that opens the app every morning does not burn a seat a day.
        using var server = new FakeLicenseServer();
        using var vm = await ActivatedAsync(server);

        // A first scan on the new instance is what asks the gate about the seat, so
        // that is the call this goes through rather than a private refresh entry
        // point that does not exist in the view model.
        server.ResponseJson = FakeLicenseServer.SeatHeldJson;
        int callsBefore = server.Endpoints.Count;
        using var reopened = new MainWindowViewModel(new LemonSqueezyClient(server));

        Assert.True(await reopened.PassScanGateAsync());

        Assert.True(reopened.IsProLicenseActive);
        Assert.True(reopened.Licensing!.CanReleaseSeat);
        Assert.DoesNotContain(
            LemonSqueezyClient.ActivateEndpoint,
            server.Endpoints.Skip(callsBefore));
    }

    /// <summary>A view model whose panel already holds an activated seat.</summary>
    private static async Task<MainWindowViewModel> ActivatedAsync(FakeLicenseServer server)
    {
        var vm = new MainWindowViewModel(new LemonSqueezyClient(server));
        vm.Licensing!.LicenseKeyInput = "KEY-1";
        await vm.Licensing.ValidateCommand.Execute();
        Assert.True(vm.IsProLicenseActive, "the fixture must start on Pro");
        return vm;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _originalSettingsDir);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }
}