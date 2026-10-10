using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
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

// ============ The diagnostics panel ============
//
// Two things decide whether this panel is any use. The first is whether it can
// be reached at all: it used to sit behind the settings drawer, which is the
// last place an operator looks when the screen in front of them will not find
// their phone. The second is what it shows before it has run, which was an
// empty list under a sentence cut off where the buttons began.
//
// Everything here is measured on the rendered panel, because the bindings are
// the part that breaks: a missing IsVisible still compiles and still draws
// both buttons at once.

public class TroubleshootModalTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"troubleshoot-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public TroubleshootModalTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");
    }

    [AvaloniaFact]
    public void TheStartScreen_HasAWayIntoTheDiagnostics()
    {
        using var window = Opened();
        var vm = (MainWindowViewModel)window.DataContext!;

        var button = Descendants(window).OfType<Button>().FirstOrDefault(candidate =>
            candidate.IsEffectivelyVisible && ReferenceEquals(candidate.Command, vm.OpenTroubleshootModalCommand));

        Assert.NotNull(button);
        Assert.True(IsUnder(button!, "IdleState"),
            "the diagnostics are only offered from the settings drawer, which is where nobody goes first");

        Assert.False(vm.IsTroubleshootModalOpen);
        vm.OpenTroubleshootModalCommand.Execute().Subscribe();
        Assert.True(vm.IsTroubleshootModalOpen, "pressing it did not open the panel");
    }

    [AvaloniaFact]
    public void TheStatusLine_KeepsTheWholeSentence()
    {
        using var window = Opened();
        var vm = (MainWindowViewModel)window.DataContext!;
        var panel = vm.TroubleshootViewModel;
        vm.IsTroubleshootModalOpen = true;

        panel.OverallStatus = "Scanned.";
        Layout(window);
        double oneLine = StatusLine(window, panel).Bounds.Height;

        panel.OverallStatus = string.Join(" ", Enumerable.Repeat("diagnostic", 30));
        Layout(window);
        double wrapped = StatusLine(window, panel).Bounds.Height;

        Assert.True(wrapped > oneLine + 8,
            $"a 300 character status still renders {wrapped:F0}px tall against {oneLine:F0}px for a short one, "
            + "so it is being cut off rather than wrapped");
    }

    [AvaloniaFact]
    public void BeforeTheFirstScan_ThePanelExplainsItself_RatherThanListingNothing()
    {
        using var window = Opened();
        var vm = (MainWindowViewModel)window.DataContext!;
        var panel = vm.TroubleshootViewModel;
        vm.IsTroubleshootModalOpen = true;
        Layout(window);

        Assert.True(panel.AwaitingFirstScan, "nothing has been scanned yet");

        // What it is about to look for, in the middle where the list will be.
        var promise = Descendants(Modal(window)).OfType<TextBlock>()
            .First(block => block.Text == LocalizationManager.GetString("Troubleshoot_Desc"));
        Assert.True(promise.IsEffectivelyVisible, "the panel does not say what it will look for");

        // No list until there is something to list.
        Assert.False(SingleList(window).IsVisible, "an empty list is drawn where the explanation should be");

        // One way to start it, named for starting it.
        var starters = Shown(Buttons(window, panel.RunDiagnosticsCommand));
        Assert.Single(starters);
        Assert.Equal(LocalizationManager.GetString("Btn_RunDiagnostics"), starters[0].Content);
    }

    [AvaloniaFact]
    public void AfterAScan_ThePanelOffersARescan_AndShowsTheList()
    {
        using var window = Opened();
        var vm = (MainWindowViewModel)window.DataContext!;
        var panel = vm.TroubleshootViewModel;
        vm.IsTroubleshootModalOpen = true;
        panel.HasResults = true;
        Layout(window);

        Assert.False(panel.AwaitingFirstScan);

        var promise = Descendants(Modal(window)).OfType<TextBlock>()
            .First(block => block.Text == LocalizationManager.GetString("Troubleshoot_Desc"));
        Assert.False(promise.IsEffectivelyVisible, "the explanation is still on top of the results");
        Assert.True(SingleList(window).IsVisible, "the results are not shown once they exist");

        var starters = Shown(Buttons(window, panel.RunDiagnosticsCommand));
        Assert.Single(starters);
        Assert.Equal(LocalizationManager.GetString("Btn_Rescan"), starters[0].Content);
    }

    /// <summary>
    /// Three of the four severities are painted by the pill stylesheet and the
    /// fourth has no colour of its own, so it has to ask for the neutral badge
    /// by name. Without that it draws a loose word beside three badges, which
    /// reads as something missing from the row rather than a fourth answer.
    /// </summary>
    [AvaloniaFact]
    public void EverySeverity_WearsTheBadgeItIsGiven()
    {
        using var window = Opened();
        var vm = (MainWindowViewModel)window.DataContext!;
        var panel = vm.TroubleshootViewModel;
        vm.IsTroubleshootModalOpen = true;
        Layout(window);

        panel.PublishReport(new TroubleshootReport
        {
            OverallStatus = "Two checks.",
            Checks =
            {
                new DiagnosticCheckItem
                {
                    Category = "Connection",
                    Title = "Cloudflared Connector",
                    Severity = DiagnosticSeverity.Info,
                    Message = "not found",
                },
                new DiagnosticCheckItem
                {
                    Category = "Hardware",
                    Title = "USB Device Enumeration",
                    Severity = DiagnosticSeverity.Fail,
                    Message = "nothing found",
                },
            },
        });
        Layout(window);

        Assert.True(PillFor(window, panel.Checks[1].SeverityLabel).Classes.Contains("danger"),
            "a failed check does not wear the badge it asks for");
        Assert.True(PillFor(window, panel.Checks[0].SeverityLabel).Classes.Contains("neutral"),
            "an informational check is a loose word where the others wear a badge");
    }

    [AvaloniaFact]
    public void FixAll_WaitsUntilThereIsSomethingToFix()
    {
        using var window = Opened();
        var vm = (MainWindowViewModel)window.DataContext!;
        var panel = vm.TroubleshootViewModel;
        vm.IsTroubleshootModalOpen = true;
        Layout(window);

        var fixAll = Buttons(window, panel.FixAllCommand).Single();
        Assert.False(fixAll.IsEffectivelyVisible,
            "an operator who has not scanned anything is offered fixes for issues nobody has reported");

        panel.HasFixableIssues = true;
        Layout(window);
        Assert.True(fixAll.IsEffectivelyVisible, "the fix button did not appear once there was something to fix");
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

    /// <summary>The window, laid out, with the panel shut.</summary>
    private static MainWindow Opened()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        window.Show();
        window.Width = 1200;
        window.Height = 900;
        Layout(window);
        return window;
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    /// <summary>The card the diagnostics panel is drawn in, found by its title.</summary>
    private static Border Modal(Visual root)
    {
        var title = Descendants(root).OfType<TextBlock>()
            .First(block => block.Text == LocalizationManager.GetString("Troubleshoot_Title"));

        for (Visual? node = title; node is not null; node = node.GetVisualParent())
        {
            if (node is Border border && border.Classes.Contains("card")) return border;
        }

        throw new InvalidOperationException("the diagnostics title is not inside a card");
    }

    /// <summary>The one list in the panel, empty or not.</summary>
    private static ScrollViewer SingleList(Visual root) =>
        Descendants(Modal(root)).OfType<ScrollViewer>().Single();

    private static TextBlock StatusLine(Visual root, TroubleshootViewModel panel) =>
        Descendants(Modal(root)).OfType<TextBlock>().First(block => block.Text == panel.OverallStatus);

    /// <summary>The badge a check wears, found by the label printed on it.</summary>
    private static Border PillFor(Visual root, string label)
    {
        var text = Descendants(root).OfType<TextBlock>()
            .First(block => block.Classes.Contains("pillText") && block.Text == label);

        for (Visual? node = text; node is not null; node = node.GetVisualParent())
        {
            if (node is Border border && border.Classes.Contains("pill")) return border;
        }

        throw new InvalidOperationException($"the '{label}' label is not inside a badge");
    }

    /// <summary>Every button bound to a command, whether it is drawing or not.</summary>
    private static List<Button> Buttons(Visual root, System.Windows.Input.ICommand command) =>
        Descendants(root).OfType<Button>()
            .Where(button => ReferenceEquals(button.Command, command))
            .ToList();

    /// <summary>Of those, the ones the operator can actually see.</summary>
    private static List<Button> Shown(List<Button> buttons) =>
        buttons.Where(button => button.IsEffectivelyVisible).ToList();

    /// <summary>Whether anything at or above this point carries that name.</summary>
    private static bool IsUnder(Visual? node, string name)
    {
        for (Visual? current = node; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && control.Name == name) return true;
        }

        return false;
    }

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
