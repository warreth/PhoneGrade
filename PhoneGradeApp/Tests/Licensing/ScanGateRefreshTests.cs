using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

/// <summary>
/// Covers the fix for the stale scan counter: the gate spends a free scan during
/// PassScanGateAsync, and the view model has to raise the licensing properties
/// afterwards or the settings card and banner keep showing the count the app
/// started with. These tests drive the real MainWindowViewModel and the real
/// TrialGate over temp files, then watch the PropertyChanged events a binding
/// would follow.
/// </summary>
public class ScanGateRefreshTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"scan-gate-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public ScanGateRefreshTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public async Task PassScanGateAsync_AfterAFreeScan_RaisesTheCountAndStatusText()
    {
        using var vm = new MainWindowViewModel();
        Assert.Equal(0, vm.TrialScanCount);

        var raised = new List<string?>();
        void Recorder(object? _, PropertyChangedEventArgs e) => raised.Add(e.PropertyName);
        vm.PropertyChanged += Recorder;
        try
        {
            bool proceed = await vm.PassScanGateAsync();
            vm.PropertyChanged -= Recorder;

            Assert.True(proceed); // first scan is inside the free tier
            Assert.Equal(1, vm.TrialScanCount);
            Assert.False(vm.IsTrialLimitReached);

            // A binding only follows these if the properties actually raised.
            Assert.Contains(nameof(MainWindowViewModel.TrialScanCount), raised);
            Assert.Contains(nameof(MainWindowViewModel.LicensingStatusText), raised);
            Assert.Contains(nameof(MainWindowViewModel.IsTrialLimitReached), raised);
            Assert.Contains("1/10", vm.LicensingStatusText);
        }
        catch
        {
            vm.PropertyChanged -= Recorder;
            throw;
        }
    }

    [AvaloniaFact]
    public async Task PassScanGateAsync_WritesTheNewCountToDiskImmediately()
    {
        using var vm = new MainWindowViewModel();

        Assert.True(await vm.PassScanGateAsync());

        // Both storage locations must hold the incremented count right away: a
        // crash before the next ordinary settings save may not lose the scan.
        string settingsPath = Path.Combine(_settingsDir, "settings.json");
        string token = System.Text.Json.Nodes.JsonNode
            .Parse(File.ReadAllText(settingsPath))!["TrialToken"]!.GetValue<string>();
        var fromSettings = PhoneGrade.Core.Licensing.TrialStateCipher.TryDecryptState(token);
        Assert.Equal(1, fromSettings!.ScanCount);

        string backupPath = Path.Combine(_settingsDir, "sys_cache.dat");
        var fromBackup = PhoneGrade.Core.Licensing.TrialStateCipher.TryDecryptState(File.ReadAllText(backupPath));
        Assert.Equal(1, fromBackup!.ScanCount);
    }

    [AvaloniaFact]
    public async Task PassScanGateAsync_AtTheLimit_BlocksAndRaisesTheLimitFlag()
    {
        // A user who already spent all ten scans must see the limit immediately.
        var exhausted = PhoneGrade.Core.Licensing.TrialStateCipher.Encrypt(
            new PhoneGrade.Core.Licensing.TrialState { ScanCount = 10 });
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"),
            $"{{\"Theme\":\"Dark\",\"TrialToken\":\"{exhausted}\"}}");

        using var vm = new MainWindowViewModel();
        Assert.True(vm.IsTrialLimitReached); // read directly at construction

        var raised = new List<string?>();
        void Recorder(object? _, PropertyChangedEventArgs e) => raised.Add(e.PropertyName);
        vm.PropertyChanged += Recorder;
        try
        {
            bool proceed = await vm.PassScanGateAsync();
            vm.PropertyChanged -= Recorder;

            Assert.False(proceed);
            Assert.True(vm.IsTrialLimitReached);
            Assert.Contains(nameof(MainWindowViewModel.IsTrialLimitReached), raised);
            Assert.Equal(10, vm.TrialScanCount); // blocked scans never spend more
        }
        catch
        {
            vm.PropertyChanged -= Recorder;
            throw;
        }
    }

    [AvaloniaFact]
    public void MainWindowViewModel_ReportsFreeTierStatusForTheSettingsCard()
    {
        using var vm = new MainWindowViewModel();

        Assert.False(vm.IsProLicenseActive);
        Assert.Contains("/10", vm.LicensingStatusText);
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }
}
