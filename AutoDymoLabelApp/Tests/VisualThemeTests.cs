using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AutoDymoLabelApp.UI.ViewModels;
using AutoDymoLabelApp.UI.Views;
using Xunit;

namespace Tests;

// ============ Visual contract tests: rendered pixels prove theme correctness ============
// These render the real MainWindow through the headless Skia pipeline and check
// average background luminance — dark theme must actually be dark, light light.

public class VisualThemeTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"visual-settings-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public VisualThemeTests()
    {
        // Isolate settings so a previous test's Theme=Light can't leak in.
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark"}""");
    }

    [AvaloniaFact]
    public void DarkTheme_RendersDarkBackground()
    {
        var window = new MainWindow();
        // Guarantee dark even if construction raced with settings
        ((MainWindowViewModel)window.DataContext!).Theme = "Dark";
        double lum = RenderAverageLuminance(window);
        Assert.True(lum < 60, $"dark theme too bright: {lum:F0}");
    }

    [AvaloniaFact]
    public void LightTheme_RendersLightBackground()
    {
        var window = new MainWindow();
        ((MainWindowViewModel)window.DataContext!).Theme = "Light";
        double lum = RenderAverageLuminance(window);
        Assert.True(lum > 150, $"light theme too dark: {lum:F0}");
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }

    private static double RenderAverageLuminance(Window window)
    {
        window.Show();
        window.Width = 760;
        window.Height = 820;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bmp = HeadlessWindowExtensions.CaptureRenderedFrame(window);
        window.Hide();

        // Save the frame as PNG and decode with a real PNG reader (System.IO.Compression
        // handles the IDAT zlib stream) — sampling every 8th pixel for average luminance.
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms);
        return PngLuminance.Average(ms.ToArray());
    }
}
