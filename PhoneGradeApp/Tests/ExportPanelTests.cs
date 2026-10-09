using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;
using PhoneGrade.Tests;

namespace Tests;

// ============ The finish panel, measured on the laid out window ============
//
// The panel is the last screen of an inspection: the label as it will print, and
// one button that prints it and files the record. A fault here is not a crash: it
// is a button that looks enabled and does nothing, a label drawn from settings the
// files do not use, or a phone that gets labelled without a serial number. None of
// those show up in a return value, so all of these are measured on the window after
// it has been laid out.

[Collection(LanguageCollection.Name)]
public class ExportPanelTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"export-panel-{Guid.NewGuid():N}");
    private readonly string? _origSettingsDir = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public ExportPanelTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"),
            """{"Theme":"Dark","IntroSeen":true,"Language":"en"}""");
        LocalizationManager.SetLanguage("en");
    }

    [AvaloniaFact]
    public void ThePanelIsReachableFromTheInspectionReport_ThroughOneButton()
    {
        using MainWindow window = Shown();
        var vm = (MainWindowViewModel)window.DataContext!;

        // The button on the report screen is the only way in. It used to open a
        // file with whatever application the system had registered for it, which
        // meant the flow ended in the DYMO editor or in an error dialog.
        Assert.False(vm.IsExportOpen);
        vm.WorkflowState = AppWorkflowState.Summary;
        vm.OpenExportCommand.Execute().Subscribe();
        Layout(window);

        Assert.True(vm.IsExportOpen, "the report screen's button does not open the finish panel");
    }

    [AvaloniaFact]
    public void NothingIsWrittenBeforeAnythingIsPressed()
    {
        // Opening the panel must not write anything, or a device nobody has chosen
        // yet has a file named after it on disk.
        var vm = new MainWindowViewModel { DeviceData = Demo() };
        vm.ExportViewModel!.Open();

        Assert.Empty(vm.ExportViewModel.Results);
        Assert.Equal("", vm.ExportViewModel.Status);
    }

    [AvaloniaFact]
    public void TheLabelAndTheReportAreWrittenToStartWith_SoTheCommonFinishIsOneClick()
    {
        // A fresh install must be able to finish without choosing anything: the
        // label for the printer in front of the operator and the report the shop
        // keeps. The second label format and the raw numbers are opt in, because
        // they are for a shop with a use for them.
        var vm = new MainWindowViewModel { DeviceData = Demo() };

        Assert.Equal(
            new[] { ExportFormat.DymoLabel, ExportFormat.ReportPdf },
            vm.ExportFormats.OrderBy(format => format.ToString()).ToArray());
    }

    [AvaloniaFact]
    public void TheConfiguredListNamesWhatEveryFinishWrites()
    {
        // The panel never asks what to write; it shows what the settings say it
        // writes. A list that cannot resolve a name is a list an operator cannot
        // check.
        ExportViewModel panel = Panel();

        string formats = panel.ConfiguredFormats;
        Assert.Contains(LocalizationManager.GetString("Export_FormatDymo"), formats, StringComparison.Ordinal);
        Assert.Contains(LocalizationManager.GetString("Export_FormatReportPdf"), formats, StringComparison.Ordinal);

        Assert.NotEqual("Settings_LabelVariantClean", panel.ConfiguredVariant);
        Assert.False(string.IsNullOrWhiteSpace(panel.ConfiguredRoll));
        Assert.Contains("export", panel.FolderLine, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void NothingToWriteDisablesTheButton_SoItCannotDoNothing()
    {
        var vm = new MainWindowViewModel { DeviceData = Demo() };
        ExportViewModel panel = vm.ExportViewModel!;
        panel.Open();

        vm.ExportFormats = [];
        panel.ExtraFiles = false;
        Assert.False(panel.CanFinish, "the finish button stays enabled with nothing to write");

        // The command's own can-execute has to follow the same thing, or a keyboard
        // shortcut reaches a command that refuses.
        bool? commandAllows = null;
        using var subscription = panel.FinishCommand.CanExecute
            .Subscribe(allowed => commandAllows = allowed);
        Assert.True(commandAllows == false, "the finish command still runs with nothing to write");

        panel.ExtraFiles = true;
        Assert.True(panel.CanFinish, "the extra files do not count as something to write");
    }

    [AvaloniaFact]
    public void TheAddressLabelIsChosen_SoThePreviewMatchesTheTemplateThatShipped()
    {
        // The template that came with the app describes address stock. A preview
        // of one size with the files written at another is a preview of nothing.
        ExportViewModel panel = Panel();

        Assert.Equal(LabelLayout.Address, panel.Layout);
    }

    [AvaloniaFact]
    public void TheFoldCanChangeTheRollForOneInspection_WithoutChangingTheStandard()
    {
        // "Deze keer anders" is for one phone. A roll chosen in the fold that wrote
        // itself back into the settings would silently move every label after it.
        ExportViewModel panel = Panel();
        LabelLayoutItem other = panel.Layouts.First(item => item.Stock.PartNumber == "30336");

        panel.Stock = other;

        Assert.Equal(other.Stock, panel.Layout.Stock);
        Assert.NotEqual("30336", panel.ConfiguredRoll);
    }

    [AvaloniaFact]
    public void ThePreviewShowsTheSameValuesAsTheLabelFile()
    {
        // Read from the panel and from what the writer would put in the file. A
        // preview that disagrees with the label is worse than no preview.
        var vm = new MainWindowViewModel
        {
            DeviceData = new DeviceData
            {
                Identifier = "356938035643809", Model = "13 Pro", Color = ColorKeys.White,
                Storage = "256GB", BatteryHealth = "68", Quality = "A", PayMethod = "Marge",
            },
        };
        ExportViewModel panel = vm.ExportViewModel!;
        panel.Open();

        LabelFields expected = LabelFields.From(vm.DeviceData);
        Assert.Equal(expected.Battery, panel.Label.Battery);

        string all = string.Join(" | ", panel.Plate!.Lines.Select(line => line.Text));
        Assert.Contains("68% [X]", all, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ABatteryUnderTheThresholdShowsTheMarker_AtTheShopThreshold()
    {
        // The threshold is a shop norm, not a constant: 68 percent is a tired
        // battery to one shop and a normal one to another, and the setting decides.
        var vm = new MainWindowViewModel { DeviceData = new DeviceData { BatteryHealth = "68" } };
        ExportViewModel panel = vm.ExportViewModel!;

        Assert.Contains("68% [X]", string.Join(" ", panel.Plate!.Lines.Select(line => line.Text)), StringComparison.Ordinal);

        vm.LabelBatteryThreshold = 50;

        Assert.DoesNotContain("[X]", string.Join(" ", panel.Plate!.Lines.Select(line => line.Text)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void CyclesUnderTheThresholdAreLeftOffTheLabel()
    {
        // Below the floor the number says nothing and the paper is worth more to
        // the faults; above it, it is the thing a battery percentage cannot say.
        var vm = new MainWindowViewModel
        {
            DeviceData = new DeviceData
            {
                Identifier = "356938035643809", Model = "13 Pro", Color = ColorKeys.White,
                Storage = "256GB", BatteryHealth = "90", Quality = "A", BatteryCycleCount = 300,
            },
        };
        ExportViewModel panel = vm.ExportViewModel!;

        Assert.DoesNotContain("300 CYCLES", string.Join(" ", panel.Plate!.Lines.Select(line => line.Text)), StringComparison.Ordinal);

        vm.DeviceData.BatteryCycleCount = 612;
        vm.ExportViewModel!.ReloadLabelSettings();

        Assert.Contains("612 CYCLES", string.Join(" ", panel.Plate!.Lines.Select(line => line.Text)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void AValueThatWasNeverReportedIsLeftOff_AndBlocksTheLabel()
    {
        // A placeholder on paper is a word a customer reads as part of what the
        // shop is selling. The four values a label cannot do without block the
        // print instead, and the panel says which one is missing.
        var vm = new MainWindowViewModel { DeviceData = new DeviceData() };
        ExportViewModel panel = vm.ExportViewModel!;

        string all = string.Join(" ", panel.Plate!.Lines.Select(line => line.Text));
        Assert.DoesNotContain(DevicePlaceholders.Model, all, StringComparison.Ordinal);
        Assert.DoesNotContain(DevicePlaceholders.PayMethod, all, StringComparison.Ordinal);

        Assert.True(panel.IsLabelBlocked, "a phone with no serial can be labelled");
        Assert.Contains(LabelField.Identifier, panel.MissingFields);
        Assert.Contains(LabelField.Model, panel.MissingFields);
        Assert.Contains(LabelField.Grade, panel.MissingFields);
        Assert.Contains(LabelField.Color, panel.MissingFields);

        Assert.NotEqual("LabelField_Identifier", LocalizationManager.GetString("LabelField_Identifier"));
        Assert.Contains(LocalizationManager.GetString("LabelField_Identifier"), panel.MissingFieldsText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheFinishPanelAndTheLabelSettingsDrawTheSamePlate()
    {
        // The settings preview and the finish preview are the same drawing of the
        // same plate. Two renderers is how a shop configures one label and prints
        // another, and this is the test that says there is one.
        var vm = new MainWindowViewModel
        {
            DeviceData = new DeviceData
            {
                Identifier = "356938035643809", Model = "13 Pro", Color = ColorKeys.White,
                Storage = "256GB", BatteryHealth = "90", Quality = "A", BatteryCycleCount = 612,
            },
        };
        vm.LabelVariant = LabelVariant.GradeBlock;

        ExportViewModel panel = vm.ExportViewModel!;
        panel.Open();

        string fromPanel = string.Join("|", panel.Plate!.Lines.Select(line => line.Text));
        string fromSettings = string.Join("|", vm.LabelSettings!.Plate!.Lines.Select(line => line.Text));

        Assert.Equal(fromPanel, fromSettings);
        Assert.Equal(LabelVariant.GradeBlock, vm.LabelSettings.Plate.Variant);
    }

    [AvaloniaFact]
    public void OpeningThePanelForAnotherDeviceDropsWhatTheLastOneWrote()
    {
        // A preview and a file list from the last phone are worse than none.
        ExportViewModel panel = Panel();
        panel.Show(new LabelWriter.Batch(
            [new LabelWriter.Outcome(ExportFormat.Json, "C:\\x.json", true, null)], "C:\\"));

        panel.Open();

        Assert.Empty(panel.Results);
        Assert.False(panel.HasResults);
    }

    [AvaloniaFact]
    public void OnlyFilesThatCanDoSomethingOfferToDoIt()
    {
        // A print button on a JSON file is a button that can only fail.
        var written = new ExportResultItem(
            new LabelWriter.Outcome(ExportFormat.DymoLabel, "C:\\a.dymo", true, null), "C:\\");
        var labelPdf = new ExportResultItem(
            new LabelWriter.Outcome(ExportFormat.LabelPdf, "C:\\a-label.pdf", true, null), "C:\\");
        var json = new ExportResultItem(
            new LabelWriter.Outcome(ExportFormat.Json, "C:\\a.json", true, null), "C:\\");
        var failed = new ExportResultItem(
            new LabelWriter.Outcome(ExportFormat.DymoLabel, null, false, "no template"), "C:\\");

        Assert.True(written.CanOpenInApp);
        Assert.False(written.CanPrint);
        Assert.True(labelPdf.CanPrint);
        Assert.False(json.CanPrint);
        Assert.False(json.CanOpenInApp);
        Assert.False(failed.CanPrint);
        Assert.False(failed.CanOpenInApp);
    }

    [AvaloniaFact]
    public void EveryWrittenRowIsOnThePanel_WithItsFileNameAndWhatCanBeDoneToIt()
    {
        using MainWindow window = Shown();
        var vm = (MainWindowViewModel)window.DataContext!;
        ReportWithPanelOpen(window, vm);

        ExportViewModel panel = vm.ExportViewModel!;
        panel.Show(Sample());
        Drain();
        Layout(window);

        var rows = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Text == "shot-label.pdf")
            .ToList();

        Assert.True(rows.Count > 0, "the written files are not named on the panel");
    }

    [AvaloniaFact]
    public void AFailedFormatKeepsItsRow_SoTheFilesThatDidWriteAreStillSeen()
    {
        ExportViewModel panel = Panel();
        panel.Show(Sample(breakTheLabel: true));

        Assert.Equal(3, panel.Results.Count);
        Assert.True(panel.Results.Count(result => !result.Succeeded) == 1);
        Assert.True(panel.Results.Count(result => result.Succeeded) == 2);
        Assert.True(panel.StatusIsError);
    }

    [AvaloniaFact]
    public void EveryFormatIsStillTriedAfterOneHasFailed()
    {
        // A report that drops the formats that worked when one fails reads as
        // "nothing was written".
        var vm = new MainWindowViewModel { DeviceData = Demo() };
        LabelWriter.Batch batch = Task.Run(() => LabelWriter.WriteAsync(vm.DeviceData,
            new LabelWriter.Request(ExportService.All, TempFolder(),
                "panel", TemplatePath: Path.Combine(TempFolder(), "absent.dymo"))))
            .GetAwaiter().GetResult();

        Assert.Equal(ExportService.All.Count, batch.Files.Count);
        Assert.Single(batch.Failures);
    }

    [AvaloniaFact]
    public void ThePanelStaysOpenAfterAFailedExport_SoTheReasonIsStillThere()
    {
        // Closing on failure would hide the one line that says why.
        ExportViewModel panel = Panel();
        panel.Open();
        panel.Show(Sample(breakTheLabel: true));

        Assert.True(panel.IsOpen, "the panel closed itself after a failed export");
        Assert.True(panel.StatusIsError, "the failure is not marked as one");

        // The line has to name the format that failed, or the operator has to
        // guess which file to look at again.
        string failed = LocalizationManager.GetString(
            panel.Results.First(result => !result.Succeeded).TitleKey);

        Assert.True(panel.Status.Contains(failed, StringComparison.Ordinal),
            $"the status line does not say that {failed} failed: {panel.Status}");
    }

    [AvaloniaFact]
    public void ThePrimaryButtonIsOnScreenAtTheSmallestWindowTheAppOpensAt()
    {
        // The finish button is the reason the panel exists. Below this it is not
        // reachable without scrolling. The device has to be a real one: a phone
        // without a serial gets the blocked card instead of the button.
        using MainWindow window = Shown(width: 850, height: 620);
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.DeviceData = Demo();
        ReportWithPanelOpen(window, vm);

        string wanted = LocalizationManager.GetString("Export_BtnPrintAndFinish");
        var visible = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .Select(button => button.Content?.ToString() ?? "")
            .ToList();

        Assert.True(visible.Contains(wanted),
            $"the finish button ('{wanted}') is not on screen; visible buttons: {string.Join(" | ", visible)}");

        Button finish = window.GetVisualDescendants().OfType<Button>()
            .First(button => button.IsEffectivelyVisible && Equals(button.Content, wanted));

        Assert.True(finish.Bounds.Bottom <= 620,
            $"the finish button reaches to {finish.Bounds.Bottom:F0}px in a 620px window");
    }

    [AvaloniaFact]
    public void NoButtonInThePanelCarriesWordingOfItsOwn()
    {
        using MainWindow window = Shown();
        var vm = (MainWindowViewModel)window.DataContext!;
        ReportWithPanelOpen(window, vm);

        // Scoped to the panel itself rather than the whole window: the screen behind
        // the overlay is the idle screen, and its wording is checked there. Measured
        // over everything, this test only proves that some other screen in the app
        // has a button that comes from a dictionary.
        ExportPanel panel = window.GetVisualDescendants().OfType<ExportPanel>().Single();
        Assert.True(panel.IsEffectivelyVisible, "the panel was not the thing on screen");

        // A bound button holds the wording the dictionary resolved to, not the key
        // it came from, so the check is that the wording is one the dictionary
        // holds. A hardcoded sentence is not in the dictionary under any key, which
        // is exactly what this catches, and it stays Dutch forever.
        HashSet<string> dictionary = new(StringComparer.Ordinal);
        foreach (string language in PhoneGrade.UI.Services.SupportedLanguages.Codes)
        {
            var loaded = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri($"avares://PhoneGrade.UI/Resources/Strings.{language}.axaml"));
            foreach (KeyValuePair<object, object?> entry in loaded)
                if (entry.Value is string text)
                    dictionary.Add(text);
        }

        var buttons = panel.GetVisualDescendants().OfType<Button>()
            .Select(button => button.Content)
            .OfType<string>()
            .Where(text => text.Length > 0)
            .ToList();

        Assert.True(buttons.Count >= 3, $"the panel only has {buttons.Count} buttons with wording");
        foreach (string text in buttons)
            Assert.True(dictionary.Contains(text),
                $"the panel shows \"{text}\", which is in no dictionary in either language");
    }

    // ---- helpers ----

    private static DeviceData Demo() => new()
    {
        Identifier = "356938035643809", Model = "13 Pro", Color = ColorKeys.White,
        Storage = "256GB", BatteryHealth = "90", Quality = "A", PayMethod = "Marge",
    };

    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), $"export-panel-{Guid.NewGuid():N}");

    /// <summary>
    /// A real batch, written for real. The rows are drawn from the outcome, so a
    /// described one would not prove the panel draws what an operator sees.
    /// </summary>
    private static LabelWriter.Batch Sample(bool breakTheLabel = false)
    {
        string folder = TempFolder();
        string? template = breakTheLabel ? Path.Combine(folder, "moved.dymo") : null;

        return Task.Run(() => LabelWriter.WriteAsync(Demo(), new LabelWriter.Request(
            new HashSet<ExportFormat>
            {
                ExportFormat.DymoLabel, ExportFormat.LabelPdf, ExportFormat.Json,
            },
            folder, "shot", template))).GetAwaiter().GetResult();
    }

    private static ExportViewModel Panel(Action<MainWindowViewModel>? arrange = null)
    {
        var vm = new MainWindowViewModel { DeviceData = Demo() };
        arrange?.Invoke(vm);
        return vm.ExportViewModel!;
    }

    private static MainWindow Shown(double width = 1050, double height = 740)
    {
        var window = new MainWindow { Width = width, Height = height };
        window.Show();
        Layout(window);
        return window;
    }

    /// <summary>
    /// Puts the window on the report screen with the panel open, and lets it lay
    /// out.
    ///
    /// The commands run through the dispatcher, so the dispatcher has to be pumped
    /// between the press and the layout. Without it the window is still on the
    /// idle screen while the test believes it pressed the button, and every
    /// assertion after that is measuring the wrong screen.
    /// </summary>
    private static void ReportWithPanelOpen(Window window, MainWindowViewModel vm)
    {
        vm.WorkflowState = AppWorkflowState.Summary;
        vm.OpenExportCommand.Execute().Subscribe();
        Drain();
        Layout(window);
    }

    private static void Drain()
    {
        for (int i = 0; i < 4; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        for (int i = 0; i < 6; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        window.UpdateLayout();
    }

    public void Dispose()
    {
        if (_origSettingsDir is null) Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origSettingsDir);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }
}
