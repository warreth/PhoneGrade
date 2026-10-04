using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The two screens at the end of an inspection ============
//
// The report the operator reads and the form that feeds the label. Both were
// drawn once and left alone while the screens around them were rebuilt, and
// both had the same class of fault: space the layout gave them that they had
// nothing to put in. The report stretched a card to the footer to show two
// lines, and the form measured itself against its own content, which put
// every field in the left third of the window.
//
// Everything below is measured on the laid out window, because the fix for
// both is a property on a container that still compiles and still paints
// when it is set back the wrong way.

public class LabelAndReportTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"label-report-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public LabelAndReportTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");
    }

    [AvaloniaFact]
    public void TheReportStopsWhereItsContentDoes_RatherThanCarryingAnEmptyHalf()
    {
        using MainWindow window = Shown(1050, 740);
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.WorkflowState = AppWorkflowState.Summary;
        Layout(window);
        Assert.True(vm.IsSummaryState, "the window did not stay on the report");

        // Nothing has gone wrong with this device: two lines of good news.
        TextBlock heading = window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => block.Text == LocalizationManager.GetString("Summary_ReportTitle"));
        Border card = CardOf(heading);
        Border bar = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("actionBar") && border.IsEffectivelyVisible);

        Assert.True(card.Bounds.Height > 60, $"the report card is {card.Bounds.Height:F0}px tall");

        double gap = bar.Bounds.Top - card.Bounds.Bottom;
        Assert.True(gap >= 40,
            $"the card runs down to the action bar ({gap:F0}px between them), so the lower "
            + "part of it is a frame around nothing");
    }

    [AvaloniaFact]
    public void TheLabelFormFillsTheWindow_InsteadOfTheLeftThirdOfIt()
    {
        DataEditorWindow window = EditAt(900, 820, new DeviceData
        {
            Identifier = "356938035643809",
            Model = "13 Pro",
            Color = "Wit",
            Storage = "256GB",
        });

        // Both columns, not one: a field narrower than this means the grid
        // collapsed to the width of its labels again.
        TextBox first = window.GetVisualDescendants().OfType<TextBox>().First();
        Assert.True(first.Bounds.Width >= 300,
            $"the first field is {first.Bounds.Width:F0}px wide in a {window.Bounds.Width:F0}px window");

        // ...and the second column is there too, to the right of the first.
        // Field bounds are relative to their own label row, so it is the rows
        // that are compared: one of them starting where the other ends means
        // the grid put them side by side rather than one under the other.
        var fields = window.GetVisualDescendants().OfType<TextBox>().ToList();
        Assert.True(fields.Count >= 6, $"the form only has {fields.Count} fields");
        var firstColumn = (Control)first.Parent!;
        var secondColumn = (Control)fields[1].Parent!;
        Assert.True(secondColumn.Bounds.Left > firstColumn.Bounds.Right,
            "the second field is not beside the first, so the two columns are stacked");

        window.Close();
    }

    [AvaloniaFact]
    public void TheAuditIsNotAHeadingOverNothing_WhenNoComponentWasChecked()
    {
        DataEditorWindow empty = EditAt(900, 820, new DeviceData());
        TextBlock blank = windowHeading(empty, "Active_OemTitle");
        Assert.False(blank.IsEffectivelyVisible,
            "an audit with no entries is still offered as one of the sections");

        DataEditorWindow checked_ = EditAt(900, 820, new DeviceData
        {
            ComponentChecks = [new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Passed }],
        });
        TextBlock filled = windowHeading(checked_, "Active_OemTitle");
        Assert.True(filled.IsEffectivelyVisible, "the audit that has entries is not shown");

        empty.Close();
        checked_.Close();
    }

    [AvaloniaFact]
    public void EveryGroupOnTheLabelFormIsInACard()
    {
        DataEditorWindow window = EditAt(900, 820, new DeviceData());

        var headings = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Classes.Contains("sectionTitle"))
            .ToList();
        Assert.True(headings.Count >= 3, $"only {headings.Count} groups on the form");

        foreach (TextBlock heading in headings)
        {
            Assert.True(CardOfOrNull(heading) is not null,
                $"the group headed \"{heading.Text}\" is loose on the window background");
        }

        window.Close();
        HeadlessRender.Drain();

    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }

    // ---- helpers ----

    private static MainWindow Shown(double width, double height)
    {
        var window = new MainWindow { Width = width, Height = height };
        window.Show();
        Layout(window);
        return window;
    }

    private static DataEditorWindow EditAt(double width, double height, DeviceData data)
    {
        var window = new DataEditorWindow
        {
            Width = width,
            Height = height,
            DataContext = new DataEditorViewModel(data),
        };
        window.Show();
        Layout(window);
        return window;
    }

    private static TextBlock windowHeading(DataEditorWindow window, string key) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => block.Text == LocalizationManager.GetString(key));

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    private static Border CardOf(Control node) =>
        CardOfOrNull(node) ?? throw new InvalidOperationException("no card above this element");

    private static Border? CardOfOrNull(Visual? node)
    {
        for (Visual? current = node; current is not null; current = current.GetVisualParent())
        {
            if (current is Border border && border.Classes.Contains("card")) return border;
        }

        return null;
    }
}
