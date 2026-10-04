using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using ReactiveUI;
using Xunit;

namespace Tests;

// ============ The screen a technician lands on ============
//
// The idle screen is where every inspection starts, so the two things on it
// that look the same have to be the same thing. It carried a scan button and a
// second scan button for a while: one went through the current refresh with its
// status line, its adb gate and its way back to idle, the other went through an
// older poll that did none of that. Pressing whichever was lower gave a
// different answer about the same cable, and the wording of the two labels
// gave no hint of that.
//
// So the count is measured on the rendered screen rather than in the source:
// a grep would find the buttons but not whether either of them is reachable.

public class IdleScreenTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"idle-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public IdleScreenTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public void TheIdleScreen_HasExactlyOneWayToScan()
    {
        using var window = ShowIdle();
        var vm = (MainWindowViewModel)window.DataContext!;

        var scanning = IdleButtons(window)
            .Where(b => ReferenceEquals(b.Command, vm.RefreshDevicesCommand))
            .ToList();

        Assert.True(scanning.Count == 1,
            "two controls that both scan but through different code is how the " +
            $"window ends up showing one phone while the cable has another; found {scanning.Count}");

        Assert.Equal(LocalizationManager.GetString("Btn_ScanManually"), scanning[0].Content);
    }

    [AvaloniaFact]
    public void TheMainWindow_KeepsOneCommandForLookingAtTheCable()
    {
        // The rendered count above only sees the buttons that were drawn. This is
        // the same rule one level down: whatever the second button was bound to,
        // there is no second scan to bind it to.
        var scanning = typeof(MainWindowViewModel)
            .GetProperties()
            .Where(p => typeof(IReactiveCommand).IsAssignableFrom(p.PropertyType))
            .Where(p => p.Name.Contains("Scan", StringComparison.Ordinal)
                     || p.Name.Contains("Refresh", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(new[] { nameof(MainWindowViewModel.RefreshDevicesCommand) }, scanning);
    }

    [AvaloniaFact]
    public void TheIdleScreen_StillStartsAnInspection()
    {
        // Removing the duplicate must not have taken the button that matters.
        using var window = ShowIdle();
        var vm = (MainWindowViewModel)window.DataContext!;

        var start = IdleButtons(window)
            .Where(b => ReferenceEquals(b.Command, vm.StartCommand))
            .ToList();

        Assert.Single(start);
        Assert.Equal(LocalizationManager.GetString("Btn_StartInspection"), start[0].Content);
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

    private static MainWindow ShowIdle()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";

        window.Show();
        window.Width = 1050;
        window.Height = 740;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    /// <summary>Every button the idle screen actually put on screen.</summary>
    private static List<Button> IdleButtons(Window window)
    {
        var idle = Descendants(window).OfType<Control>()
            .FirstOrDefault(c => c.Name == "IdleState");
        Assert.NotNull(idle);
        return Descendants(idle!).OfType<Button>().ToList();
    }

    private static IEnumerable<Control> Descendants(Control root) =>
        root.GetVisualDescendants().OfType<Control>();
}
