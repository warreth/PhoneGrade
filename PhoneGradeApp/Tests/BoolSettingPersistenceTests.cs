using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

// ============ Turning a boolean off has to reach the settings ============
//
// ReactiveUI 20 returns the newly set value from RaiseAndSetIfChanged instead of
// a changed flag, so a setter guarded with the return value skipped its work
// whenever the new value happened to be false. That is the one value a switch
// exists to set, and it went nowhere: the field still updated, but the settings
// write and the dependent raises that follow the guard did not. The cable route
// and the public tunnel broke outright (their settings never reached the file),
// and the licence tick, the barcode-mode availability and the panel's filing
// switch kept stale dependents. These tests pin the field, the file and the
// notifications for the sites that were fixed.

public class BoolSettingPersistenceTests : IDisposable
{
    private readonly SettingsScratch _settings = new("""{"Theme":"Dark","IntroSeen":true}""");

    [AvaloniaFact]
    public void SwitchingTheCableRouteOff_ReachesTheSettingsFile()
    {
        using var vm = new MainWindowViewModel();

        vm.UseSecureOrigin = false;

        Assert.False(vm.UseSecureOrigin);
        Assert.False(AppSettings.Load().UseSecureOrigin);
    }

    [AvaloniaFact]
    public void SwitchingThePublicTunnelOff_ReachesTheSettingsFile()
    {
        using var vm = new MainWindowViewModel();

        vm.UsePublicTunnel = false;

        Assert.False(vm.UsePublicTunnel);
        Assert.False(AppSettings.Load().UsePublicTunnel);
    }

    [AvaloniaFact]
    public void ClearingTheLicenceTick_TellsTheIntroductionItCanNoLongerFinish()
    {
        using var vm = new MainWindowViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        vm.IsLicenceAccepted = true;
        raised.Clear();
        vm.IsLicenceAccepted = false;

        Assert.False(vm.IsLicenceAccepted);
        Assert.Contains(nameof(MainWindowViewModel.CanFinishIntro), raised);
    }

    [AvaloniaFact]
    public void TurningFilingOnlyOff_BringsThePrintWordingBack()
    {
        using var vm = new MainWindowViewModel();
        ExportViewModel panel = vm.ExportViewModel!;

        panel.SaveOnly = true;
        Assert.Equal("Export_BtnSaveAndFinish", panel.PrimaryLabelKey);

        var raised = new List<string>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        panel.SaveOnly = false;

        Assert.False(panel.SaveOnly);
        Assert.Equal("Export_BtnPrintAndFinish", panel.PrimaryLabelKey);
        Assert.Contains(nameof(ExportViewModel.PrimaryLabelKey), raised);
    }

    [AvaloniaFact]
    public void TurningTheNumberFilesOff_RecomputesTheButtonState()
    {
        using var vm = new MainWindowViewModel();
        ExportViewModel panel = vm.ExportViewModel!;

        panel.ExtraFiles = true;
        var raised = new List<string>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        panel.ExtraFiles = false;

        Assert.False(panel.ExtraFiles);
        Assert.Contains(nameof(ExportViewModel.HasFormats), raised);
    }

    /// <summary>Restores the settings variable and deletes the scratch folder.</summary>
    public void Dispose() => _settings.Dispose();
}
