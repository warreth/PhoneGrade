using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using AutoDymoLabel.Core;
using AutoDymoLabelApp.UI.ViewModels;
using AutoDymoLabelApp.UI.Views;

// Renders the real windows to PNG so the redesign can be visually verified.
// Manual diagnostic tool — not part of the xUnit suite.
// Usage: dotnet run --project Tests --no-build -- [outputDir]
class ScreenshotRunner
{
    [STAThread]
    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "/tmp/shots";
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<AutoDymoLabel.UI.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                var vm = BuildDemoViewModel();

                vm.IsQualityPopupVisible = true;
                Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-quality.png"));
                vm.IsQualityPopupVisible = false;
                vm.IsPaymentPopupVisible = true;
                Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-payment.png"));
                vm.IsPaymentPopupVisible = false;
                Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-issues.png"));
                Capture(new DataEditorWindow { DataContext = new DataEditorViewModel(vm.DeviceData) }, Path.Combine(outDir, "editor-dark.png"));

                Console.WriteLine("done");
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                Environment.Exit(0);
            })
            .StartWithClassicDesktopLifetime([]);
    }

    static MainWindowViewModel BuildDemoViewModel()
    {
        var vm = new MainWindowViewModel
        {
            DeviceData = new DeviceData
            {
                Model = "13 Pro", Storage = "256GB", Color = "Wit",
                BatteryHealth = "90", Identifier = "356938035643809", Quality = "A",
            },
        };
        vm.Issues.Add(new DiagnosticIssue
        {
            Title = "Batterij-sensor mist (TG0B)",
            Explanation = "Het toestel 'ziet' de batterij niet (Tigris/batterij-temperatuursensor). Leidt tot reboot-loops.",
            Fix = "Controleer de batterijconnector en batterij; vervang de batterij.",
            Level = Severity.Error,
        });
        vm.HasIssues = true;
        return vm;
    }

    static void Capture(Window window, string file, double width = 760, double height = 820)
    {
        try
        {
            window.Show();
            window.Width = width;
            window.Height = height;
            // Two ticks: one to lay out at the new size, one to paint the frame.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var shot = HeadlessWindowExtensions.CaptureRenderedFrame(window);
            shot.Save(file);
            Console.WriteLine($"saved {file}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"capture failed for {file}: {ex.Message}");
        }
        finally
        {
            window.Hide();
        }
    }
}
