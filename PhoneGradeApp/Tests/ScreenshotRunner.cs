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
        // A second argument picks one section while iterating: "flow" runs only
        // the workflow states, "lic" only the licensing screens.
        string section = args.Length > 1 ? args[1] : "";
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<PhoneGrade.UI.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .AfterSetup(_ =>
            {
                var vm = BuildDemoViewModel();

                if (section is "" or "main")
                {
                    vm.IsQualityPopupVisible = true;
                    Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-quality.png"));
                    vm.IsQualityPopupVisible = false;
                    vm.IsPaymentPopupVisible = true;
                    Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-payment.png"));
                    vm.IsPaymentPopupVisible = false;
                    Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-issues.png"));
                    Capture(new DataEditorWindow { DataContext = new DataEditorViewModel(vm.DeviceData) }, Path.Combine(outDir, "editor-dark.png"));

                    // The USB debugging overlay, in both themes. This card used to be
                    // a light yellow block in an otherwise dark window, which no
                    // assertion about theme resources would have caught. The model
                    // comes from the native port, so these show what an operator sees
                    // with a phone on the cable rather than a placeholder.
                    var guide = BuildDemoViewModel();
                    // The theme setter is the only thing that reaches the application:
                    // a view model built from a settings file that already says Dark
                    // never applies it, and the shot comes out in whatever the previous
                    // picture left behind.
                    guide.Theme = "Dark";
                    guide.WorkflowState = AppWorkflowState.Idle;
                    guide.ShowAdbWarning = true;

                    guide.AdbTutorialViewModel.SetDevice("Honor", "HONOR 600 Lite");
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-dark.png"), 900, 900);

                    guide.Theme = "Light";
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-light.png"), 900, 900);
                    guide.Theme = "Dark";

                    // The kiosk window size, where the old inline card ran off the
                    // bottom of the idle screen.
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-kiosk.png"), 850, 620);

                    // The two fallbacks: a phone whose brand has no menu table of its
                    // own, and the generic pair for a phone nothing recognises.
                    guide.AdbTutorialViewModel.SetDevice("Honor", "");
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-brand-only.png"), 900, 900);

                    guide.AdbTutorialViewModel.SetDevice("", "");
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-generic.png"), 900, 900);
                }

                if (section is "" or "lic") CaptureLicensingStates(outDir);
                if (section is "" or "flow") CaptureWorkflowStates(outDir);

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

    // The three states an inspection walks through, plus the overlays that sit
    // on top of them, all at the size the window actually opens at. The narrow
    // shot is the minimum width: that is where a header bar stops fitting.
    static void CaptureWorkflowStates(string outDir)
    {
        string settingsDir = Path.Combine(outDir, "flow-settings");
        Directory.CreateDirectory(settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");

        void Shot(MainWindowViewModel vm, string file, double width = 1050, double height = 740,
                  AppWorkflowState? state = null)
        {
            vm.Theme = "Dark";
            Action arrange = () =>
            {
                if (state is { } workflow) vm.WorkflowState = workflow;
            };
            Func<bool> still = () => state is null || vm.WorkflowState == state;

            // The first window built for a view model whose state was set before
            // the window existed comes out painted with the state the model had
            // at construction, however often the frame is taken afterwards; the
            // second window, built once the state has stood for a while, paints
            // what is actually set. So the first one is spent here and only the
            // second one is saved.
            string warmup = Path.Combine(Path.GetTempPath(), "phonegrade-warmup.png");
            if (state is not null)
            {
                Capture(new MainWindow { DataContext = vm }, warmup,
                    width, height, arrange: arrange, stillWanted: still);
            }

            Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, file),
                width, height, arrange: arrange, stillWanted: still);

            try { File.Delete(warmup); } catch (IOException) { }
        }

        var idle = BuildDemoViewModel();
        Shot(idle, "flow-idle-dark.png");
        Shot(idle, "flow-idle-narrow.png", 850, 620);
        idle.Theme = "Light";
        Capture(new MainWindow { DataContext = idle }, Path.Combine(outDir, "flow-idle-light.png"), 1050, 740);

        var active = BuildDemoViewModel();
        active.WorkflowState = AppWorkflowState.Active;
        active.Progress = 62;
        active.Status = "Batterij en beveiliging uitlezen...";
        FillAudit(active);
        Shot(active, "flow-active-dark.png", state: AppWorkflowState.Active);
        Shot(active, "flow-active-narrow.png", 850, 620, state: AppWorkflowState.Active);

        var summary = BuildDemoViewModel();
        summary.WorkflowState = AppWorkflowState.Summary;
        summary.Status = "Inspectie afgerond";
        FillAudit(summary);
        summary.FailedInteractiveTests.Add(new InteractiveTestResult
        {
            Name = "Touchscreen",
            Status = TestStatus.Failed,
            Notes = "Linkeronderhoek reageert niet, ongeveer 4 cm breed.",
        });
        summary.SkippedInteractiveTests.Add(new InteractiveTestResult
        {
            Name = "Nabijheidssensor",
            Status = TestStatus.Skipped,
            Notes = "De browser weigerde de toegang tot de sensor.",
        });
        Shot(summary, "flow-summary-dark.png", state: AppWorkflowState.Summary);
        Shot(summary, "flow-summary-narrow.png", 850, 620, state: AppWorkflowState.Summary);

        var clean = BuildDemoViewModel();
        clean.WorkflowState = AppWorkflowState.Summary;
        clean.Status = "Inspectie afgerond";
        FillAudit(clean);
        Shot(clean, "flow-summary-clean-dark.png", state: AppWorkflowState.Summary);

        var settings = BuildDemoViewModel();
        settings.IsSettingsDrawerOpen = true;
        Shot(settings, "flow-settings-dark.png");

        var logs = BuildDemoViewModel();
        logs.IsLogsModalOpen = true;
        Shot(logs, "flow-logs-dark.png");

        var trouble = BuildDemoViewModel();
        trouble.IsTroubleshootModalOpen = true;
        Shot(trouble, "flow-troubleshoot-dark.png");

        var quality = BuildDemoViewModel();
        quality.IsQualityPopupVisible = true;
        Shot(quality, "flow-quality-dark.png");

        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
    }

    // Rows in the OEM audit: one part that matches, one that does not, one that
    // nothing was read for. All three have to fit the same row without clipping.
    static void FillAudit(MainWindowViewModel vm)
    {
        vm.ComponentChecks.Add(new ComponentStatus
        {
            Name = "Batterij",
            SerialRead = "F2LXG0A3Q1G6",
            SerialOriginal = "F2LXG0A3Q1G6",
            Status = ComponentStatusType.Match,
        });
        vm.ComponentChecks.Add(new ComponentStatus
        {
            Name = "Scherm",
            SerialRead = "C3X9P2LM4K1Q",
            SerialOriginal = "C3X9P2LM4K8Z",
            Status = ComponentStatusType.Mismatch,
        });
        vm.ComponentChecks.Add(new ComponentStatus
        {
            Name = "Camera achter",
            SerialRead = "",
            SerialOriginal = "DNL7H2M3P9R1",
            Status = ComponentStatusType.Unknown,
        });

        foreach (ComponentStatus check in vm.ComponentChecks)
            vm.DeviceData.ComponentChecks.Add(check);
        vm.DeviceData.ComponentChecks.Add(new ComponentStatus
        {
            Name = "Scherm",
            SerialRead = "C3X9P2LM4K1Q",
            SerialOriginal = "C3X9P2LM4K8Z",
            Status = ComponentStatusType.Mismatch,
        });

        vm.DefectiveComponents.Add(new ComponentStatus
        {
            Name = "Scherm",
            SerialRead = "C3X9P2LM4K1Q",
            SerialOriginal = "C3X9P2LM4K8Z",
            Status = ComponentStatusType.Mismatch,
        });
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

    // The window that is currently on screen. The headless platform paints the
    // window it considers foremost, and a second window shown next to the first
    // one kept coming back as the frame of the first: every wide shot of the
    // active and summary screens was byte for byte the idle screen. Taking the
    // earlier window off the screen before the next one goes up makes the frame
    // belong to the window that was asked for. Hiding, not closing, because
    // closing runs the shutdown of the view model and several shots share one.
    static Window? _onScreen;

    static void Capture(Window window, string file, double width = 760, double height = 820,
                        Action? arrange = null, Func<bool>? stillWanted = null)
    {
        try
        {
            // Size before Show: the headless window takes its client size from
            // the values it is created with, so a resize afterwards silently
            // did nothing and every picture came out at the XAML size.
            window.Width = width;
            window.Height = height;
            if (_onScreen is not null && !ReferenceEquals(_onScreen, window)) _onScreen.IsVisible = false;
            _onScreen = window;
            window.Show();

            // Two ticks: one to lay out at the new size, one to paint the frame.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            // The state the picture is meant to show is applied over and over
            // until it survives a stretch of ticks untouched: the refresh the
            // view model starts on construction walks back to Idle whenever it
            // likes, and setting the state once is answered by that continuation
            // landing a moment later. Only a state that is still standing after
            // a run of ticks is the one the frame will actually paint.
            for (int attempt = 0; attempt < 400; attempt++)
            {
                arrange?.Invoke();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                System.Threading.Thread.Sleep(5);
                if (arrange is null) break;
                if (stillWanted?.Invoke() != true) continue;

                bool held = true;
                for (int hold = 0; hold < 25; hold++)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    System.Threading.Thread.Sleep(4);
                    if (stillWanted?.Invoke() != true) { held = false; break; }
                }
                if (held) break;
            }
            if (arrange is not null && stillWanted?.Invoke() == false)
                Console.WriteLine($"warning: {file} never settled on the state it was meant to show");

            for (int i = 0; i < 3; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (arrange is not null && stillWanted is not null)
                Console.WriteLine($"state for {Path.GetFileName(file)}: {(stillWanted() ? "as intended" : "lost again")} visible={window.IsVisible}");

            // Which of the three screens the window itself is showing, so a frame
            // that comes out wrong can be told apart from a state that never
            // reached the visual tree.
            if (window is MainWindow main)
            {
                var idlePanel = main.FindControl<Control>("IdleState");
                var activePanel = main.FindControl<Control>("ActiveState");
                var summaryPanel = main.FindControl<Control>("SummaryState");
                Console.WriteLine($"    panels idle={idlePanel?.IsVisible} active={activePanel?.IsVisible} summary={summaryPanel?.IsVisible} bounds={main.Bounds.Width}x{main.Bounds.Height}");
            }

            using (var shot = HeadlessWindowExtensions.CaptureRenderedFrame(window))
            {
                shot.Save(file);
                Console.WriteLine($"saved {file} (lum={AverageLuminance(file):F0})");
            }
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

    /// <summary>Mean brightness of a saved frame, so a theme mix-up is visible in
    /// the log rather than only in the picture.</summary>
    static double AverageLuminance(string file)
    {
        try
        {
            return Tests.PngLuminance.Average(File.ReadAllBytes(file));
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
