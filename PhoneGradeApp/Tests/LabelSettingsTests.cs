using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using Xunit;
using PhoneGrade.Tests;

namespace Tests;

// ============ The label settings and their live preview ============
//
// The settings page and the finish panel edit the same stored values and both draw
// through the same plate. These tests keep the two ends of that together: a picker
// that writes a value nobody renders, or a preview that keeps showing the old one,
// is a shop configuring one label and printing another.

[Collection(LanguageCollection.Name)]
public class LabelSettingsTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"label-settings-{Guid.NewGuid():N}");
    private readonly string? _origSettingsDir = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public LabelSettingsTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"),
            """{"Theme":"Dark","IntroSeen":true,"Language":"en"}""");
        LocalizationManager.SetLanguage("en");
    }

    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809", Model = "iPhone 13 Pro", Color = ColorKeys.Graphite,
        Storage = "256GB", BatteryHealth = "78", BatteryCycleCount = 612, Quality = "C", PayMethod = "BTW",
    };

    private static MainWindowViewModel Window() => new() { DeviceData = Phone() };

    [AvaloniaFact]
    public void ThePreviewFollowsTheVariantPicker()
    {
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        settings.VariantItem = settings.Variants.First(item => item.Variant == LabelVariant.GradeBlock);

        Assert.Equal(LabelVariant.GradeBlock, vm.LabelVariant);
        Assert.Equal(LabelVariant.GradeBlock, settings.Plate!.Variant);
        Assert.Equal("C", settings.Plate.Grade);
    }

    [AvaloniaFact]
    public void ThePreviewFollowsTheStockPicker()
    {
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        settings.Stock = settings.Layouts.First(item => item.Stock.PartNumber == "30256");

        Assert.Equal("30256", vm.LabelStockPartNumber);
        Assert.Equal(LabelStock.FromPartNumber("30256"), settings.Layout.Stock);
    }

    [AvaloniaFact]
    public void TheBarcodeSwitchOffMeansNoCodesOnThePreview()
    {
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        Assert.NotEmpty(settings.Plate!.Barred);

        settings.BarcodeEnabled = false;

        Assert.False(vm.LabelBarcodeEnabled);
        Assert.Empty(settings.Plate!.Barred);
    }

    [AvaloniaFact]
    public void TheCyclesSwitchTakesTheCountOffThePreview()
    {
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        Assert.Contains("612", string.Join(" ", settings.Plate!.Lines.Select(line => line.Text)), StringComparison.Ordinal);

        settings.ShowCycles = false;

        Assert.DoesNotContain("612", string.Join(" ", settings.Plate!.Lines.Select(line => line.Text)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheThresholdsAreHeldBetweenTheSensibleEnds()
    {
        // A threshold is a shop norm with a floor and a ceiling: 10 percent marks
        // every phone and 100 marks none, and both are settings an operator can hit
        // by dragging a slider to the end.
        var vm = Window();

        vm.LabelBatteryThreshold = 10;
        Assert.Equal(50, vm.LabelBatteryThreshold);

        vm.LabelBatteryThreshold = 500;
        Assert.Equal(95, vm.LabelBatteryThreshold);

        vm.LabelCyclesThreshold = -5;
        Assert.Equal(0, vm.LabelCyclesThreshold);

        vm.LabelCyclesThreshold = 99999;
        Assert.Equal(5000, vm.LabelCyclesThreshold);
    }

    [AvaloniaFact]
    public void TheBatteryExampleReadsThePhoneOnScreen()
    {
        // The sentence beside the slider says what the threshold does to the device
        // in front of the operator, not what a threshold is.
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        Assert.Contains("78% [X]", settings.BatteryExample, StringComparison.Ordinal);

        settings.BatteryThreshold = 50;
        Assert.DoesNotContain("[X]", settings.BatteryExample, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheFolderExampleNamesTheFolderTheSchemeWouldUse()
    {
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        settings.FolderScheme = settings.FolderSchemes.First(item => item.Scheme == ExportFolderScheme.Inspection);

        Assert.Equal(ExportFolderScheme.Inspection, vm.ExportFolderScheme);
        Assert.Contains("356938035643809", settings.FolderExample, StringComparison.Ordinal);

        settings.FolderScheme = settings.FolderSchemes.First(item => item.Scheme == ExportFolderScheme.Day);
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), settings.FolderExample, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheFileTicksWriteThroughToWhatEveryFinishWrites()
    {
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        Assert.True(settings.WritesDymo);
        Assert.False(settings.WritesJson);

        settings.WritesJson = true;
        Assert.True(vm.WritesFormat(ExportFormat.Json));

        settings.WritesDymo = false;
        Assert.False(vm.WritesFormat(ExportFormat.DymoLabel));
    }

    [AvaloniaFact]
    public void TheBarcodePickerCannotChooseAModeTheRollCannotDraw()
    {
        // The combined code does not fit any roll in Code39. The picker greys it
        // out, and a settings file naming it anyway falls back rather than writing a
        // label whose barcode does not scan.
        var vm = Window();
        LabelSettingsViewModel settings = vm.LabelSettings!;

        LabelBarcodeItem combined = settings.BarcodeModes.First(item => item.Mode == LabelBarcodeMode.Combined);
        Assert.False(combined.IsAvailable, "the combined code is offered in Code39 on an address label");

        settings.Barcode = combined;

        Assert.NotEqual(LabelBarcodeMode.Combined, vm.LabelBarcodeMode);
        Assert.Equal(LabelBarcodeMode.Identifier, vm.LabelBarcodeMode);
    }

    public void Dispose()
    {
        if (_origSettingsDir is null) Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origSettingsDir);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }
}
