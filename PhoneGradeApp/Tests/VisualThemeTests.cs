using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ Visual contract tests: rendered pixels prove theme correctness ============
// These render the real MainWindow through the headless Skia pipeline and check
// average background luminance - dark theme must actually be dark, light light.

public class VisualThemeTests : IDisposable
{
    private readonly SettingsScratch _settings = new();

    [AvaloniaFact]
    public void DarkTheme_RendersDarkBackground()
    {
        using var window = new MainWindow();
        try
        {
            // Guarantee dark even if construction raced with settings
            ((MainWindowViewModel)window.DataContext!).Theme = "Dark";
            double lum = RenderAverageLuminance(window);
            Assert.True(lum < 60, $"dark theme too bright: {lum:F0}");
        }
        finally
        {
            // The queue is drained whatever the assertion did. A pass left over
            // from here runs during the next test's setup and fails it for this
            // window's work, which turns one bad frame into an unrelated failure.
            HeadlessRender.Drain();
        }
    }

    [AvaloniaFact]
    public void LightTheme_RendersLightBackground()
    {
        using var window = new MainWindow();
        try
        {
            ((MainWindowViewModel)window.DataContext!).Theme = "Light";
            double lum = RenderAverageLuminance(window);
            Assert.True(lum > 150, $"light theme too dark: {lum:F0}");
        }
        finally
        {
            HeadlessRender.Drain();
        }
    }

    private static double RenderAverageLuminance(Window window)
    {
        window.Show();
        window.Width = 760;
        window.Height = 820;

        // Waits for the first pass rather than capturing once and assuming one
        // happened: capturing too early reads a null frame, and the null then
        // fails whichever assertion happens to touch it first.
        using WriteableBitmap frame = HeadlessRender.Capture(window);
        window.Hide();

        // Save the frame as PNG and decode with a real PNG reader (System.IO.Compression
        // handles the IDAT zlib stream) - sampling every 8th pixel for average luminance.
        using var ms = new System.IO.MemoryStream();
        frame.Save(ms);
        return PngLuminance.Average(ms.ToArray());
    }

    /// <summary>Restores the settings variable and deletes the scratch folder.</summary>
    public void Dispose() => _settings.Dispose();
}
