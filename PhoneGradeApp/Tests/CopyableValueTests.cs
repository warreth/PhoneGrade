using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The values the operator copies off the screen ============
//
// An IMEI, a serial number or a battery percentage is read on this screen and
// then typed into another program. A plain TextBlock cannot be selected, so
// every read-out in MainWindow.axaml is a SelectableTextBlock, which the
// operator selects and copies with Ctrl+C the way the web runner URL already
// could.
//
// The markup is checked rather than the rendered pixels for most of this,
// because the two things that can go wrong are in the source: a value that is
// still a TextBlock gives the operator nothing to select, and a label that was
// swept along with the values turns a heading into a copy target. The rendered
// window is checked at the end, because a source file cannot prove the
// controls reached the visual tree at all.

public class CopyableValueTests
{
    private static string View => RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");

    /// <summary>The number of times a needle occurs, so a partial sweep shows up as a number.</summary>
    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    [Fact]
    public void TheValuesTheOperatorCopiesAreSelectableInEveryRegion()
    {
        string view = View;

        // Top bar identity: the model line and the device identifier.
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData, Converter={StaticResource ModelDisplayConverter}}\"",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Identifier}\" Classes=\"dim\" FontSize=\"12\"",
            view);

        // The spec card, one assertion per value under its label: model with
        // its converter, serial, storage, colour, battery health, battery
        // level, and the grade.
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData, Converter={StaticResource ModelDisplayConverter}}\" FontWeight=\"Bold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Identifier}\" FontWeight=\"SemiBold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Storage}\" FontWeight=\"Bold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Color, Converter={StaticResource ColorConverter}}\" FontWeight=\"SemiBold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.BatteryHealth, Converter={StaticResource BatteryConverter}}\" FontWeight=\"SemiBold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.BatteryLevel, Converter={StaticResource BatteryLevelConverter}}\" Classes=\"dim\" FontSize=\"10\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding SelectedGradeDisplay}\" FontWeight=\"Bold\" FontSize=\"12\"",
            view);

        // Security and locks: the IMEI, the verification source, the
        // activation lock verdict, and the blacklist placeholder.
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Identifier}\" FontWeight=\"SemiBold\" FontSize=\"12\" TextTrimming=\"CharacterEllipsis\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.FmiVerificationSource}\" FontSize=\"8\" Foreground=\"{DynamicResource TextDimColor}\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.ActivationLock, Converter={StaticResource ActivationLockConverter}}\"",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{DynamicResource Spec_NotChecked}\" Classes=\"dim\" FontSize=\"12\"/>",
            view);

        // Component audit rows: the component name and both serials.
        Assert.Contains(
            "<SelectableTextBlock Grid.Column=\"0\" Margin=\"0,0,10,0\" Text=\"{Binding Name}\" FontWeight=\"SemiBold\" FontSize=\"11\" VerticalAlignment=\"Center\" TextTrimming=\"CharacterEllipsis\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding SerialOriginal}\" FontSize=\"10\" TextTrimming=\"CharacterEllipsis\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding SerialRead}\" FontSize=\"10\" TextTrimming=\"CharacterEllipsis\"/>",
            view);

        // The summary: model, then the four values beside their dim labels.
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData, Converter={StaticResource ModelDisplayConverter}}\" FontSize=\"18\" FontWeight=\"Bold\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Identifier}\" Classes=\"dim\" FontSize=\"11\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Storage}\" Classes=\"dim\" FontSize=\"11\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Memory, Converter={StaticResource MemoryConverter}}\" Classes=\"dim\" FontSize=\"11\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.Color, Converter={StaticResource ColorConverter}}\" Classes=\"dim\" FontSize=\"11\"/>",
            view);

        // The checklist: battery health, invoice, sim lock, tests.
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding DeviceData.BatteryHealth, Converter={StaticResource BatteryConverter}}\" FontWeight=\"Bold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding SelectedInvoiceMethodDisplay}\" FontWeight=\"Bold\" FontSize=\"12\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{DynamicResource Summary_SimLockFree}\" FontWeight=\"Bold\" FontSize=\"12\"",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding InteractiveTestSummary}\" FontWeight=\"Bold\" FontSize=\"12\"",
            view);

        // The report rows that carry the serials.
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding SerialOriginal}\" FontSize=\"11\" TextTrimming=\"CharacterEllipsis\"/>",
            view);
        Assert.Contains(
            "<SelectableTextBlock Text=\"{Binding SerialRead}\" FontSize=\"11\" TextTrimming=\"CharacterEllipsis\"/>",
            view);

        // And the other side of the same claim: no value line with these
        // bindings is left as a plain TextBlock anywhere in the file, so a
        // region that gets added later cannot quietly miss out either.
        Assert.DoesNotContain("<TextBlock Text=\"{Binding DeviceData, Converter={StaticResource ModelDisplayConverter}}\"", view);
        Assert.DoesNotContain("<TextBlock Text=\"{Binding DeviceData.Identifier}\"", view);
        Assert.DoesNotContain("<TextBlock Text=\"{Binding SerialOriginal}\"", view);
        Assert.DoesNotContain("<TextBlock Text=\"{Binding SerialRead}\"", view);

        // One number over the whole file, so a sweep that stops halfway is
        // visible as a smaller number rather than as a missed line nobody
        // wrote an assertion for. Thirty values were converted here, plus the
        // web runner URL that was already selectable.
        int selectable = CountOccurrences(view, "<SelectableTextBlock");
        Assert.True(selectable >= 25,
            $"only {selectable} elements in the window are selectable, the values did not all get converted");
    }

    /// <summary>
    /// The labels are what a blanket find-and-replace would take with it. They
    /// name the value; they are not the value, and selecting "Model" instead
    /// of the model is worse than not being able to select at all.
    /// </summary>
    [Fact]
    public void TheLabelsBesideThoseValuesAreStillPlainTextBlocks()
    {
        string view = View;

        // The dim labels of the spec card and of the security card.
        Assert.Contains("<TextBlock Classes=\"dim\" Text=\"{DynamicResource Spec_Model}\" FontSize=\"10\"/>", view);
        Assert.Contains("<TextBlock Classes=\"dim\" Text=\"{DynamicResource Spec_Serial}\" FontSize=\"10\"/>", view);
        Assert.Contains("<TextBlock Classes=\"dim\" Text=\"{DynamicResource Spec_Imei}\" FontSize=\"10\"/>", view);
        Assert.Contains("<TextBlock Classes=\"dim\" Text=\"{DynamicResource Spec_Blacklist}\" FontSize=\"10\"/>", view);

        // The column captions of the component audit rows.
        Assert.Contains("<TextBlock Classes=\"dim\" Text=\"{DynamicResource Active_ColFactory}\" FontSize=\"9\"", view);
        Assert.Contains("<TextBlock Classes=\"dim\" Text=\"{DynamicResource Active_ColRead}\" FontSize=\"9\"", view);

        // The field labels of the summary and of the report rows.
        Assert.Contains("<TextBlock Classes=\"fieldLabel\" Text=\"{DynamicResource Summary_BatteryHealth}\"/>", view);
        Assert.Contains("<TextBlock Classes=\"fieldLabel\" Text=\"{DynamicResource Summary_InvoiceMethod}\"/>", view);
        Assert.Contains("<TextBlock Classes=\"fieldLabel\" Text=\"{DynamicResource Summary_ColFactory}\" FontSize=\"9\"/>", view);
        Assert.Contains("<TextBlock Classes=\"fieldLabel\" Text=\"{DynamicResource Summary_ColRead}\" FontSize=\"9\"/>", view);

        // The card titles.
        Assert.Contains("<TextBlock Classes=\"cardTitle\" Text=\"{DynamicResource Active_SpecsTitle}\" FontSize=\"11\"/>", view);
        Assert.Contains("<TextBlock Classes=\"cardTitle\" Text=\"{DynamicResource Active_SecurityTitle}\" FontSize=\"11\"/>", view);

        // A label class must never end up on the selectable element: that is
        // the shape a future sweep would leave behind.
        Assert.DoesNotContain("<SelectableTextBlock Classes=\"fieldLabel\"", view);
        Assert.DoesNotContain("<SelectableTextBlock Classes=\"cardTitle\"", view);
        Assert.DoesNotContain("<SelectableTextBlock Classes=\"pillText\"", view);
    }

    /// <summary>
    /// State and chips answer "where are we", not "what did we find", and a
    /// pill is a control. Nothing an operator would want to copy lives in
    /// them, so they stay as they were.
    /// </summary>
    [Fact]
    public void TheStateAndChipTextsAreNotSelectionTargets()
    {
        string view = View;

        // The status line in the top bar, and the status line over the
        // progress bar.
        Assert.Contains("<TextBlock Text=\"{Binding Status}\" Classes=\"dim\" FontSize=\"12\"", view);
        Assert.Contains("<TextBlock Text=\"{Binding Status}\" FontWeight=\"SemiBold\" FontSize=\"13\" TextWrapping=\"Wrap\"/>", view);

        // The grade in the summary sits in a pill: the pill keeps its own
        // text element, because the style that colours it is written for a
        // TextBlock child.
        Assert.Contains("<TextBlock Text=\"{Binding SelectedGradeDisplay}\"", view);

        // The component status chip, the report chips, and the interactive
        // session pill.
        Assert.Contains("<TextBlock Text=\"{Binding Status, Converter={StaticResource ComponentStatusConverter}}\"/>", view);
        Assert.Contains("<TextBlock Text=\"{DynamicResource Report_Failed}\"/>", view);
        Assert.Contains("<TextBlock Classes=\"pillText\" Text=\"{Binding InteractiveSessionStatus}\" FontSize=\"10\"/>", view);

        // A selectable control in a pill or a chip would be a value that lost
        // the colour those styles give it.
        Assert.DoesNotContain("<SelectableTextBlock Classes=\"pill\"", view);
        Assert.DoesNotContain("<SelectableTextBlock Classes=\"chip\"", view);
    }

    /// <summary>
    /// The markup says SelectableTextBlock; the operator only gets to select
    /// what the window actually put in its visual tree, with text on it. A
    /// control bound to nothing would be there to click and nothing to copy.
    ///
    /// The colour check is the other half: the type selector in the shared
    /// stylesheet matches the type it names and nothing below it, so a dim
    /// value only stays beside a dim label while the stylesheet carries the
    /// rule for the selectable element too.
    /// </summary>
    [AvaloniaFact]
    public void TheShownWindowHoldsSelectableValuesWithTextOnThem()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.DeviceData = new DeviceData
        {
            Model = "13 Pro",
            Storage = "256GB",
            Color = "Wit",
            BatteryHealth = "90",
            Identifier = "356938035643809",
            Quality = "A",
        };
        // The reading state is shown first: its panel is collapsed the moment
        // the summary takes over, and a collapsed panel never has its template
        // applied, so the labels of the spec card only exist in the tree while
        // this state is on screen.
        vm.WorkflowState = AppWorkflowState.Active;

        window.Show();
        window.Width = 1050;
        window.Height = 740;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        string identifier = vm.DeviceData.Identifier;
        Assert.False(string.IsNullOrWhiteSpace(identifier), "the demo device carries no identifier to copy");

        var activeValues = Descendants(window).OfType<SelectableTextBlock>().ToList();

        Assert.True(activeValues.Count >= 10,
            $"the reading state holds {activeValues.Count} selectable controls, the read-outs should be among them");

        int activeWritten = activeValues.Count(s => !string.IsNullOrWhiteSpace(s.Text));
        Assert.True(activeWritten >= 5,
            $"only {activeWritten} of them carry text, so there is nothing to select for the operator");

        // The identifier is what gets copied most, and it is on screen in the
        // header, the spec card and the security card alike.
        Assert.Contains(activeValues, s => s.Text == identifier);

        // The labels beside them are still plain TextBlock elements. Checking
        // the type rather than OfType<TextBlock>() is the point: a
        // SelectableTextBlock would answer to that too.
        var activeDimLabels = Descendants(window).OfType<TextBlock>()
            .Where(t => t.GetType() == typeof(TextBlock)
                     && t.Classes.Contains("dim")
                     && !string.IsNullOrWhiteSpace(t.Text))
            .ToList();

        string modelLabel = LocalizationManager.GetString("Spec_Model");
        Assert.True(activeDimLabels.Any(t => t.Text == modelLabel),
            $"no dim label reads \"{modelLabel}\"; the dim labels on screen are: " +
            string.Join(", ", activeDimLabels.Select(t => $"<{t.Text}>")));

        var dimValue = activeValues.FirstOrDefault(s => s.Classes.Contains("dim") && !string.IsNullOrWhiteSpace(s.Text));
        Assert.True(dimValue is not null,
            "no dim value reached the tree, so the label colour has nothing to be compared with");

        var modelLabelControl = activeDimLabels.First(t => t.Text == modelLabel);
        var labelColour = modelLabelControl.Foreground as ISolidColorBrush;
        var valueColour = dimValue!.Foreground as ISolidColorBrush;
        Assert.NotNull(labelColour);
        Assert.NotNull(valueColour);
        Assert.Equal(labelColour!.Color, valueColour!.Color);

        // The summary is the state the operator reads the grade from, so it
        // gets its own pass over the same three checks.
        vm.WorkflowState = AppWorkflowState.Summary;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        var summaryValues = Descendants(window).OfType<SelectableTextBlock>().ToList();

        Assert.True(summaryValues.Count >= 10,
            $"the summary holds {summaryValues.Count} selectable controls, the read-outs should be among them");

        int summaryWritten = summaryValues.Count(s => !string.IsNullOrWhiteSpace(s.Text));
        Assert.True(summaryWritten >= 5,
            $"only {summaryWritten} of them carry text, so there is nothing to select for the operator");

        Assert.Contains(summaryValues, s => s.Text == identifier);

        // The ticks above schedule a render pass on the dispatcher, and a pass
        // still queued when this test ends is executed by the NEXT test's
        // session setup, whose locator has not registered the font manager
        // yet - which fails that test for this test's pending work. Run the
        // queue dry here, while this test's own application is the one on
        // duty and every service it needs is registered.
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The window and everything under it, in tree order.</summary>
    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root is null) yield break;
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var grand in Descendants(child)) yield return grand;
        }
    }
}
