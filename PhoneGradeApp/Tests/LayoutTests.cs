using System;
using System.Collections.Generic;
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

// ============ Layout contract tests: the window at its smallest ============
// Everything the redesign touched has to still fit when the operator drags the
// window down to MinWidth/MinHeight. Overflow shows up as a settings button
// pushed off the right edge, which nobody notices until it is the only way in.

public class LayoutTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"layout-settings-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public LayoutTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");
    }

    [AvaloniaFact]
    public void TopBarFitsTheSmallestWindow()
    {
        using MainWindow window = ShowAtMinimumSize();
        var main = (MainWindow)window;

        // The right-hand cluster holds the license pill and the only door into
        // settings. If it starts outside the window, the button is unreachable.
        var right = main.GetVisualDescendants()
            .OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Children.OfType<Button>()
                .Any(button => button.Classes.Contains("headerBtn") || button.Classes.Contains("statusPill")));

        Assert.NotNull(right);
        Rect rightBounds = right!.Bounds;
        Assert.True(rightBounds.Right <= window.Bounds.Width + 0.5,
            $"right-hand buttons run off the edge: right={rightBounds.Right} window={window.Bounds.Width}");

        // The clipped center must not reach into that cluster either.
        var center = main.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Count == 0
                && border.MaxWidth == 460 && border.ClipToBounds);
        if (center is not null)
        {
            Assert.True(center.Bounds.Right <= rightBounds.Left + 0.5,
                $"center text overlaps the buttons: center right={center.Bounds.Right} buttons left={rightBounds.Left}");
        }

        // And every header button on screen has to be wide enough to hit.
        foreach (var button in main.GetVisualDescendants().OfType<Button>()
                     .Where(b => b.Classes.Contains("headerBtn") && b.IsEffectivelyVisible))
        {
            Assert.True(button.Bounds.Width >= 32 && button.Bounds.Height >= 32,
                $"header button is too small to hit: {button.Bounds.Width}x{button.Bounds.Height}");
        }
    }

    [AvaloniaFact]
    public void SettingsPageFitsTheSmallestWindow()
    {
        using MainWindow window = ShowAtMinimumSize();
        var main = (MainWindow)window;
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.SelectedSettingsSection = "Connection";
        vm.IsSettingsDrawerOpen = true;
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();

        // The topic rail is 220 wide and the pane next to it scrolls. Neither may
        // be laid out past the right edge of the window.
        var navItems = main.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("navItem"))
            .ToList();
        Assert.True(navItems.Count >= 6, "the settings topics are not all on screen");

        foreach (var item in navItems)
        {
            Rect bounds = item.Bounds;
            Assert.True(bounds.Right <= window.Bounds.Width + 0.5,
                $"topic \"{item.Content}\" runs off the edge: right={bounds.Right} window={window.Bounds.Width}");
            Assert.True(bounds.Width >= 100,
                $"topic \"{item.Content}\" is squeezed to {bounds.Width} wide");
        }

        // The close button is the only way out of the page.
        var close = main.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("headerBtn")
                && button.Classes.Contains("on") == false
                && button.IsEffectivelyVisible);
        Assert.NotNull(close);
        Assert.True(close!.Bounds.Right <= window.Bounds.Width + 0.5,
            $"the settings close button is off the edge: right={close.Bounds.Right}");
        HeadlessRender.Drain();

    }

    [AvaloniaFact]
    public void EachStatePanelLaysOutAtTheSmallestWindow()
    {
        // At 850x620 the screens share one slot, so each is checked in turn. What
        // this guards against is a panel collapsing to zero height, which a
        // fixed-height footer or an over-large fixed row causes silently.
        using MainWindow window = ShowAtMinimumSize();
        var main = (MainWindow)window;
        var vm = (MainWindowViewModel)window.DataContext!;

        foreach (var state in new[]
        {
            (name: "IdleState", value: AppWorkflowState.Idle),
            (name: "ActiveState", value: AppWorkflowState.Active),
            (name: "SummaryState", value: AppWorkflowState.Summary),
        })
        {
            vm.WorkflowState = state.value;
            window.UpdateLayout();
            for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();

            var panel = main.FindControl<Control>(state.name);
            Assert.NotNull(panel);
            Assert.True(panel!.IsVisible, $"{state.name} is not visible when the window is on that state");
            Assert.True(panel.Bounds.Height > 100,
                $"{state.name} collapsed to {panel.Bounds.Height} tall at the minimum size");
            Assert.True(panel.Bounds.Width > 200,
                $"{state.name} collapsed to {panel.Bounds.Width} wide at the minimum size");
        }
        HeadlessRender.Drain();

    }

    [AvaloniaFact]
    public void TheSettingsPage_PaintsOverWhatIsBehindIt()
    {
        // The page drew on top of the idle screen with no surface of its own,
        // so both screens were visible at once: the topic rail and the settings
        // cards mixed with the scan button and the waiting text under them.
        using MainWindow window = ShowAtMinimumSize();
        var main = (MainWindow)window;
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.IsSettingsDrawerOpen = true;
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();

        var settings = main.GetVisualDescendants().OfType<Panel>()
            .FirstOrDefault(panel => panel.Name == "SettingsPanel");
        Assert.NotNull(settings);
        Assert.True(settings!.IsVisible);

        // An opaque fill, not a translucent one and not the theme's default:
        // either of those still shows the screen underneath.
        var brush = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(settings.Background);
        Assert.Equal(1.0, brush.Opacity);
        Assert.Equal(255, brush.Color.A);
        HeadlessRender.Drain();
    }

    [AvaloniaFact]
    public void TheIntroductionFitsTheSmallestWindow()
    {
        // The five page names used to be laid out as one row wider than the
        // card, so the last one ran past the window edge and pointed at a page
        // nothing could reach. The wizard has a progress line and a foot now,
        // and both have to hold at the smallest window the kiosk opens at.
        using MainWindow window = ShowAtMinimumSize();
        var main = (MainWindow)window;
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.IsIntroVisible = true;
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();

        var intro = main.GetVisualDescendants().OfType<PhoneGrade.UI.Views.IntroView>().FirstOrDefault();
        Assert.NotNull(intro);
        var card = intro!.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("introCard"));
        Assert.NotNull(card);

        Point origin = card!.TranslatePoint(default, window)!.Value;
        Assert.True(origin.X >= -0.5 && origin.Y >= -0.5,
            $"the introduction starts outside the window at {origin.X:F0},{origin.Y:F0}");
        Assert.True(origin.X + card.Bounds.Width <= window.Bounds.Width + 0.5,
            $"the introduction is wider than the window: {card.Bounds.Width:F0} in {window.Bounds.Width:F0}");
        Assert.True(origin.Y + card.Bounds.Height <= window.Bounds.Height + 0.5,
            $"the introduction is taller than the window: {card.Bounds.Height:F0} in {window.Bounds.Height:F0}");

        // The progress line says which step is on screen.
        var progress = card.GetVisualDescendants().OfType<ProgressBar>().FirstOrDefault();
        Assert.NotNull(progress);
        Assert.Equal(1.0, progress!.Value);
        Assert.Equal(5.0, progress.Maximum);

        // And the foot carries a way forward.
        string next = PhoneGrade.UI.Services.LocalizationManager.GetString("Intro_Next");
        Assert.Contains(main.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible && (button.Content as string) == next);
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

    /// <summary>Opens the window at the size the title bar clamps it to.</summary>
    private static MainWindow ShowAtMinimumSize()
    {
        using var window = new MainWindow();
        window.Show();
        window.Width = 850;
        window.Height = 620;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }
}
