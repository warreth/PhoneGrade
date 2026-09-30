using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;

// Renders the real windows to PNG so the redesign can be visually verified.
// Manual diagnostic tool, not part of the xUnit suite.
// Usage: dotnet run --project Tests --no-build -- [outputDir]
class ScreenshotRunner
{
    [STAThread]
    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "/tmp/shots";
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<PhoneGrade.UI.App>()
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

                // The USB debugging guide, in both themes. This card used to be a
                // light yellow block in an otherwise dark window, which no assertion
                // about theme resources would have caught.
                var idle = BuildDemoViewModel();
                idle.WorkflowState = AppWorkflowState.Idle;
                idle.ShowAdbWarning = true;
                Capture(new MainWindow { DataContext = idle }, Path.Combine(outDir, "usb-guide-dark.png"), 900, 900);

                idle.Theme = "Light";
                Capture(new MainWindow { DataContext = idle }, Path.Combine(outDir, "usb-guide-light.png"), 900, 900);

                CaptureLicensingStates(outDir);

                Console.WriteLine("done");
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                Environment.Exit(0);
            })
            .StartWithClassicDesktopLifetime([]);
    }

    // The licensing screens: the introduction a fresh install gets, the title bar
    // pill in each colour it can take, the panel that pill opens, and the settings
    // row that points at it. Each state is seeded through real settings files so
    // the shots show what an operator in that state actually sees.
    static void CaptureLicensingStates(string outDir)
    {
        string settingsDir = Path.Combine(outDir, "lic-settings");
        Directory.CreateDirectory(settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", settingsDir);

        void Seed(int scans, bool introSeen)
        {
            // The store keeps a second copy of the state next to settings.json and
            // takes the higher of the two readings, so a leftover from an earlier
            // shot would otherwise decide what this one shows. That high-water
            // mark is the point of the backup in the product; here the file just
            // has to go.
            string backup = Path.Combine(settingsDir, "sys_cache.dat");
            if (File.Exists(backup)) File.Delete(backup);

            string token = PhoneGrade.Core.Licensing.TrialStateCipher.Encrypt(
                new PhoneGrade.Core.Licensing.TrialState { ScanCount = scans });
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"),
                $"{{\"Theme\":\"Dark\",\"IntroSeen\":{(introSeen ? "true" : "false")},\"TrialToken\":\"{token}\"}}");
        }

        // The theme setter is the only thing that reaches the application, so it
        // has to be called for every shot: a VM built from a settings file that
        // says "Dark" never applies it, and the previous shot's theme would
        // otherwise carry over and make every picture look the same.
        void Shot(MainWindowViewModel vm, string file, int w, int h, string theme = "Dark")
        {
            vm.Theme = theme;
            Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, file), w, h);
        }

        // The introduction screen, in both themes.
        Seed(0, introSeen: false);
        var intro = BuildDemoViewModel();
        Shot(intro, "intro-dark.png", 900, 760);
        Shot(intro, "intro-light.png", 900, 760, theme: "Light");

        // The same screen with the key box already asked for.
        intro.IsIntroActivationVisible = true;
        Shot(intro, "intro-activation-dark.png", 900, 760);

        // The pill: quiet, warning, blocked.
        Seed(3, introSeen: true);
        Shot(BuildDemoViewModel(), "pill-free-dark.png", 760, 200);

        Seed(8, introSeen: true);
        Shot(BuildDemoViewModel(), "pill-warn-dark.png", 760, 200);

        Seed(10, introSeen: true);
        Shot(BuildDemoViewModel(), "pill-limit-dark.png", 760, 200);

        // Pro, after a key validates against the stub.
        Seed(10, introSeen: true);
        var pro = new MainWindowViewModel(new PhoneGrade.Core.Licensing.LemonSqueezyClient(new StubLicenseServer()))
        {
            DeviceData = DemoDevice(),
        };
        ActivatePro(pro, "PRO-KEY-1234");
        Shot(pro, "pill-pro-dark.png", 760, 200);

        // The panel the pill opens, and the settings row that leads to it.
        Seed(3, introSeen: true);
        var panel = BuildDemoViewModel();
        panel.IsLicensePanelOpen = true;
        Shot(panel, "license-panel-dark.png", 760, 560);
        Shot(panel, "license-panel-light.png", 760, 560, theme: "Light");

        var drawer = BuildDemoViewModel();
        drawer.IsSettingsDrawerOpen = true;
        // Tall enough that the whole drawer is in frame, licensing row included.
        Shot(drawer, "settings-drawer-dark.png", 900, 1400);

        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
    }

    // ReactiveCommand answers on the dispatcher, so the tick loop is what makes
    // the activation land before the shot is taken.
    static void ActivatePro(MainWindowViewModel vm, string key)
    {
        vm.Licensing!.LicenseKeyInput = key;
        bool done = false;
        vm.Licensing.ValidateCommand.Execute().Subscribe(_ => done = true, () => done = true);

        for (int i = 0; i < 400 && !done; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            System.Threading.Thread.Sleep(5);
        }
        if (!done) Console.WriteLine("activation did not finish; Pro shot may show the free tier");
    }

    sealed class StubLicenseServer : System.Net.Http.HttpMessageHandler
    {
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            const string json =
                """{"valid":true,"license_key":{"id":1,"status":"active"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
            return System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    static DeviceData DemoDevice() => new()
    {
        Model = "13 Pro", Storage = "256GB", Color = "Wit",
        BatteryHealth = "90", Identifier = "356938035643809", Quality = "A",
    };

    static MainWindowViewModel BuildDemoViewModel()
    {
        var vm = new MainWindowViewModel
        {
            DeviceData = DemoDevice(),
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
            // Size before Show: the headless window takes its client size from
            // the values it is created with, so a resize afterwards silently
            // did nothing and every picture came out at the XAML size.
            window.Width = width;
            window.Height = height;
            window.Show();
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
