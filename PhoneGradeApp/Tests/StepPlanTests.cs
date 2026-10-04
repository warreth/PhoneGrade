using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The step plan both how-tos are drawn with ============
//
// The USB debugging how-to and the IMEI key setup hand out the same kind of
// instruction: a title and four numbered things to do. They used to be drawn
// two different ways, a badge in one and the words "Step 1:" spelled into the
// sentence in the other, so reading one screen taught an operator nothing
// about how to read the other.
//
// What is checked here is the rendered result rather than the markup, because
// a class in the source file proves nothing about whether a style ever matched
// it, and the rail only means anything once it has been through layout.

public class StepPlanTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"step-plan-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    private static readonly string[] PlanClasses =
    {
        "stepPlan", "stepPlanTitle", "stepItem", "stepRail", "stepBadge", "stepBody",
    };

    public StepPlanTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public void BothHowTos_DrawTheirStepsWithTheSameStyles()
    {
        using var usb = Opened(vm => vm.ShowAdbWarning = true);
        using var imei = Opened(vm =>
        {
            vm.IsSettingsDrawerOpen = true;
            vm.SelectedSettingsSection = "ImeiApi";
        });

        // Screen by screen rather than in one tree: what makes these two the
        // same design is that each of them carries the whole set on its own.
        var onUsbHowTo = ClassesOn(usb);
        var onImeiSetup = ClassesOn(imei);

        foreach (string name in PlanClasses)
        {
            Assert.Contains(name, onUsbHowTo);
            Assert.Contains(name, onImeiSetup);
        }
    }

    [AvaloniaFact]
    public void TheImeiSetup_NumbersItsStepsWithBadges_RatherThanWordsInTheText()
    {
        using var window = Opened(vm =>
        {
            vm.IsSettingsDrawerOpen = true;
            vm.SelectedSettingsSection = "ImeiApi";
        });

        var badges = Descendants(window).OfType<Border>()
            .Where(border => border.Classes.Contains("stepBadge") && border.IsEffectivelyVisible)
            .ToList();

        // Four of them, in order, read from the badges themselves. A number
        // written into the sentence makes the reader parse the sentence to work
        // out where they are in the list, and the circle is what the USB how-to
        // already puts next to each of its lines.
        Assert.Equal(new[] { "1", "2", "3", "4" }, badges.Select(BadgeText).ToArray());
    }

    [AvaloniaFact]
    public void TheRailRunsTheHeightOfItsRow_SoTheStepsReadAsOneList()
    {
        using (var usb = Opened(vm => vm.ShowAdbWarning = true))
            AssertRailsRunTheirWholeRow(usb);

        using (var imei = Opened(vm =>
               {
                   vm.IsSettingsDrawerOpen = true;
                   vm.SelectedSettingsSection = "ImeiApi";
               }))
            AssertRailsRunTheirWholeRow(imei);
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

    private static void AssertRailsRunTheirWholeRow(Visual root)
    {
        var rows = Descendants(root).OfType<Border>()
            .Where(border => border.Classes.Contains("stepItem") && border.IsEffectivelyVisible)
            .ToList();
        Assert.True(rows.Count >= 4, $"only {rows.Count} steps are on screen");

        foreach (var row in rows)
        {
            var rail = Descendants(row).OfType<Border>().First(b => b.Classes.Contains("stepRail"));
            var badge = Descendants(row).OfType<Border>().First(b => b.Classes.Contains("stepBadge"));

            // Both ends of the row, so the line reaches the badge above it and
            // the badge below. A rail only as tall as its own badge leaves a
            // gap between every pair of steps and the list falls apart into
            // four boxes instead of one path to follow.
            Assert.True(rail.Bounds.Height >= row.Bounds.Height - 0.5,
                $"the rail runs {rail.Bounds.Height:F1} of a row {row.Bounds.Height:F1} tall");

            // And down the middle of the number standing on it, which is the
            // part a mis-sized badge column takes away without anything else
            // on screen looking wrong.
            double badgeCentre = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, 0), row)!.Value.X;
            double railCentre = rail.TranslatePoint(new Point(rail.Bounds.Width / 2, 0), row)!.Value.X;
            Assert.True(Math.Abs(badgeCentre - railCentre) < 0.51,
                $"the rail is {railCentre - badgeCentre:F1}px off the centre of its badge");
        }
    }

    /// <summary>The window on one screen, laid out and ready to measure.</summary>
    private static MainWindow Opened(Action<MainWindowViewModel> state)
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        window.Show();
        window.Width = 1200;
        window.Height = 1000;
        state(vm);
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        return window;
    }

    /// <summary>The number inside a step badge.</summary>
    private static string BadgeText(Border badge) =>
        (Descendants(badge).OfType<TextBlock>().FirstOrDefault()?.Text ?? "").Trim();

    private static HashSet<string> ClassesOn(Visual root) =>
        Descendants(root).OfType<Control>()
            .Where(control => control.IsEffectivelyVisible)
            .SelectMany(control => control.Classes)
            .ToHashSet(StringComparer.Ordinal);

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
