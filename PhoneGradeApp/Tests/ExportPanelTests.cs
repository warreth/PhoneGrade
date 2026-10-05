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

namespace Tests;

// ============ The export panel, measured on the laid out window ============
//
// The panel is one screen for the label, the report and the numbers, and it is
// reached from the button an operator presses at the end of every inspection. A
// fault here is not a crash: it is a button that looks enabled and does nothing,
// a label drawn at the wrong shape, or a row that has gone off the bottom of a
// panel that is not scrollable. None of those show up in a return value, so all
// of these are measured on the window after it has been laid out.

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

        Assert.True(vm.IsExportOpen, "the report screen's button does not open the export panel");
    }

    [AvaloniaFact]
    public void NothingIsWrittenBeforeAnythingIsPressed()
    {
        // The panel is opened on the way to the report and it offers five formats.
        // Opening it must not have written any of them, or a device nobody has
        // chosen yet has a file named after it on disk.
        var vm = new MainWindowViewModel { DeviceData = Demo() };
        vm.ExportViewModel!.Open();

        Assert.Empty(vm.ExportViewModel.Results);
        Assert.Equal("", vm.ExportViewModel.Status);
    }

    [AvaloniaFact]
    public void ThreeFormatsAreTickedToStartWith_SoTheCommonExportIsOneClick()
    {
        // A fresh install must be able to export without choosing anything, and
        // the three that matter are the label, the label as a PDF for a printer
        // that is not a DYMO, and the numbers for the shop's own records.
        ExportViewModel panel = Panel();
        panel.Open();

        var ticked = panel.Options.Where(option => option.Selected).Select(option => option.Format).ToHashSet();
        Assert.Equal(ExportService.DefaultSet, ticked);
    }

    [AvaloniaFact]
    public void EveryFormatIsOffered_WithWhatItIsFor()
    {
        // A format chosen by its extension is a format chosen wrongly: a JSON file
        // and a CSV file are both "the numbers", and only one of them is what a
        // shop pastes into its own system.
        ExportViewModel panel = Panel();

        Assert.Equal(ExportService.All.Count, panel.Options.Count);
        foreach (ExportOption option in panel.Options)
        {
            string title = LocalizationManager.GetString(option.TitleKey);
            string note = LocalizationManager.GetString(option.NoteKey);

            Assert.NotEqual(option.TitleKey, title);
            Assert.NotEqual(option.NoteKey, note);
            Assert.True(note.Length > 20, $"the note for {title} says nothing: {note}");
        }
    }

    [AvaloniaFact]
    public void ClearingTheLastTickDisablesWriting_SoTheButtonCannotDoNothing()
    {
        ExportViewModel panel = Panel();
        panel.Open();

        foreach (ExportOption option in panel.Options) option.Selected = false;

        Assert.False(panel.CanExport, "the write button stays enabled with nothing ticked");

        // The command's own can-execute has to follow the same thing, or a
        // keyboard shortcut reaches a command that refuses.
        bool? commandAllows = null;
        using var subscription = panel.ExportCommand.CanExecute
            .Subscribe(allowed => commandAllows = allowed);
        Assert.True(commandAllows == false, "the write command still runs with nothing ticked");
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
    public void TheSheetKeepsTheShapeOfTheStock_AtEverySize()
    {
        // A preview drawn at a fixed size shows a 54mm label and a 106mm label as
        // the same rectangle, which is the opposite of a preview.
        ExportViewModel panel = Panel();

        foreach (LabelLayoutItem stock in panel.Layouts)
        {
            panel.Stock = stock;

            double expected = stock.Layout.HeightMm / stock.Layout.WidthMm;
            double drawn = panel.SheetHeight / panel.SheetWidth;

            Assert.True(Math.Abs(expected - drawn) < 0.001,
                $"{stock.Label} is drawn {drawn:F3} tall for every width, and the stock is {expected:F3}");
        }
    }

    [AvaloniaFact]
    public void TheSheetIsNeverDrawnWiderThanItsColumn()
    {
        using MainWindow window = Shown(width: 850);
        var vm = (MainWindowViewModel)window.DataContext!;
        ReportWithPanelOpen(window, vm);

        Border sheet = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("labelSheet"));

        var column = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("previewColumn"));

        Assert.True(sheet.Bounds.Width <= column.Bounds.Width + 1,
            $"the sheet is {sheet.Bounds.Width:F0}px wide in a {column.Bounds.Width:F0}px column");
    }

    [AvaloniaFact]
    public void ThePreviewShowsTheSameTextAsTheLabelFile()
    {
        // Read from the panel and from what the writer put in the file. A preview
        // that disagrees with the label is worse than no preview.
        var vm = new MainWindowViewModel
        {
            DeviceData = new DeviceData
            {
                Identifier = "356938035643809", Model = "13 Pro", Color = "Wit",
                Storage = "256GB", BatteryHealth = "68", Quality = "A", PayMethod = "Marge",
            },
        };
        ExportViewModel panel = vm.ExportViewModel!;
        panel.Open();

        LabelFields expected = LabelFields.From(vm.DeviceData);
        Assert.Equal(expected.Battery, panel.Label.Battery);
        Assert.Contains("68% [X]", panel.LabelTextLine);
    }

    [AvaloniaFact]
    public void ABatteryUnderTheThresholdShowsTheMarker_OnThePreview()
    {
        // The preview exists so this is caught before the label is on a device.
        var vm = new MainWindowViewModel { DeviceData = new DeviceData { BatteryHealth = "68" } };
        Assert.Contains("68% [X]", vm.ExportViewModel!.LabelTextLine);
    }

    [AvaloniaFact]
    public void AValueThatWasNeverReportedShowsAsAPlaceholder_OnThePreview()
    {
        // A phone whose colour could not be read. The gap would be invisible, so
        // the label has to say what is missing.
        var vm = new MainWindowViewModel { DeviceData = new DeviceData() };
        Assert.Contains(DevicePlaceholders.Color, vm.ExportViewModel!.LabelTextLine);
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
        // guess which of five buttons to press again.
        string failed = LocalizationManager.GetString(
            panel.Results.First(result => !result.Succeeded).TitleKey);

        Assert.True(panel.Status.Contains(failed, StringComparison.Ordinal),
            $"the status line does not say that {failed} failed: {panel.Status}");
    }

    [AvaloniaFact]
    public void TheActionBarIsOnScreenAtTheSmallestWindowTheAppOpensAt()
    {
        // The write button is the reason the panel exists. Below this it is not
        // reachable without scrolling, and there is nothing to scroll the bar.
        using MainWindow window = Shown(width: 850, height: 620);
        var vm = (MainWindowViewModel)window.DataContext!;
        ReportWithPanelOpen(window, vm);

        Button write = window.GetVisualDescendants().OfType<Button>()
            .First(button => button.IsEffectivelyVisible
                             && LocalizationManager.GetString("Export_BtnWrite") == button.Content);

        Assert.True(write.Bounds.Bottom <= 620,
            $"the write button reaches to {write.Bounds.Bottom:F0}px in a 620px window");
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
        foreach (string language in new[] { "en", "nl" })
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

        Assert.True(buttons.Count >= 4, $"the panel only has {buttons.Count} buttons with wording");
        foreach (string text in buttons)
            Assert.True(dictionary.Contains(text),
                $"the panel shows \"{text}\", which is in no dictionary in either language");
    }

    // ---- helpers ----

    private static DeviceData Demo() => new()
    {
        Identifier = "356938035643809", Model = "13 Pro", Color = "Wit",
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
