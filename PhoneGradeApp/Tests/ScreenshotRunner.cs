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
                    Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, "main-dark-idle.png"));
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

                    // ============ The export panel ============
                    //
                    // Four states, because the panel has four things to show and a
                    // screenshot of only one of them hides three: nothing chosen
                    // yet, the same in the light theme, files written, and one
                    // format that failed while the others arrived.
                    var empty = BuildDemoViewModel();
                    empty.Theme = "Dark";
                    FillAudit(empty);
                    CaptureExport(empty, outDir, "export-dark-empty.png");
                    CaptureExport(empty, outDir, "export-dark-narrow.png", 850, 620);

                    var light = BuildDemoViewModel();
                    light.Theme = "Light";
                    FillAudit(light);
                    CaptureExport(light, outDir, "export-light-empty.png");

                    var written = BuildDemoViewModel();
                    written.Theme = "Dark";
                    FillAudit(written);
                    CaptureExport(written, outDir, "export-dark-written.png",
                        seed: () => written.ExportViewModel?.Show(SampleBatch(written.DeviceData)));

                    var failed = BuildDemoViewModel();
                    failed.Theme = "Dark";
                    FillAudit(failed);
                    CaptureExport(failed, outDir, "export-dark-failed.png",
            seed: () => failed.ExportViewModel?.Show(SampleBatch(failed.DeviceData, breakTheLabel: true)));
                    // A label that is missing half its values, which is the case the
                    // preview exists for: caught here, not on a device.
                    var bare = BuildDemoViewModel();
                    bare.Theme = "Dark";
                    bare.DeviceData = new DeviceData();
                    CaptureExport(bare, outDir, "export-dark-placeholder.png");

                    // The label with faults on it, which is the label the preview
                    // exists for. Photographed on its own because the point of it is
                    // the sheet: whether the faults are on the preview is the whole
                    // question, and it is lost in a shot of the whole panel.
                    var faulty = BuildDemoViewModel();
                    faulty.Theme = "Dark";
                    faulty.DeviceData = FaultyPhone();
                    CaptureExport(faulty, outDir, "export-dark-faults.png", 1050, 820);

                    // ============ The label, at the settings it can be set to ============
                    //
                    // Six shots, because a preview of a label is only worth having if it
                    // still looks like a label at the settings an operator can pick. The
                    // barcode mode and the stock both change how much of the paper the
                    // words get, and a screenshot of only one setting hides whether the
                    // faults and the locks still fit at the others.
                    CaptureLabelSettings(outDir);
                    // The two fallbacks: a phone whose brand has no menu table of its
                    // own, and the generic pair for a phone nothing recognises.
                    guide.AdbTutorialViewModel.SetDevice("Honor", "");
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-brand-only.png"), 900, 900);

                    guide.AdbTutorialViewModel.SetDevice("", "");
                    Capture(new MainWindow { DataContext = guide }, Path.Combine(outDir, "usb-guide-generic.png"), 900, 900);
                }

                if (section is "" or "lic") CaptureLicensingStates(outDir);
                if (section is "" or "flow") CaptureWorkflowStates(outDir);
                if (section is "" or "settings") CaptureSettingsSections(outDir);

                Console.WriteLine("done");
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                Environment.Exit(0);
            })
            .StartWithClassicDesktopLifetime([]);
    }

    // The export panel, on the report screen it is opened from. The panel is an
    // overlay rather than a window, so the shot has to be of the main window with
    // the panel open; anything else photographs a control nobody ever sees.
    static void CaptureExport(MainWindowViewModel vm, string outDir, string file,
        double width = 1050, double height = 740, Action? seed = null)
    {
        // The state is seeded inside arrange rather than before the window exists.
        // Opening the panel clears whatever it had, so a state set up beforehand is
        // thrown away by the first frame and every shot comes out of the empty
        // panel, which is exactly what three of the first four shots did.
        Action arrange = () =>
        {
            vm.WorkflowState = AppWorkflowState.Summary;
            vm.ExportViewModel?.Open();
            seed?.Invoke();
        };
        Func<bool> still = () => vm.IsExportOpen;

        // Photographed twice. The overlay's visibility follows a flag the panel owns
        // rather than one the window owns, so the very first frame after the press
        // can still be the screen underneath, and a single shot of it is a shot of
        // the wrong screen. The first one absorbs that frame and is thrown away.
        string warmup = Path.Combine(Path.GetTempPath(), "phonegrade-export-warmup.png");
        Capture(new MainWindow { DataContext = vm }, warmup, width, height, arrange, still);
        Capture(new MainWindow { DataContext = vm }, Path.Combine(outDir, file), width, height, arrange, still);
        try { File.Delete(warmup); } catch (IOException) { }

        // What the preview decided its own measurements were. The sheet being the
        // wrong size is a fault a screenshot shows as "too small" and a number says
        // which of the four things that decide it is at fault.
        if (vm.ExportViewModel is { } panel && file.StartsWith("label-", StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"{file,-34} sheet {panel.SheetWidth,6:F0}x{panel.SheetHeight,5:F0}px  " +
                $"band {panel.BarcodeBandHeight,5:F1}px  " +
                $"type {panel.SpecFontSize,4:F1}/{panel.LockFontSize,4:F1}pt-on-sheet  " +
                $"text room {panel.TextRoomHeight,5:F1}px  barcodes {panel.LabelBarcodes.Count}");
        }
    }

    // The label screen at the settings it can be set to. Each shot is the whole
    // panel on the report screen, because that is where an operator meets it, and
    // the sheet is on the left of that panel rather than somewhere a crop can be
    // taken of it.
    //
    // The settings are applied through the view model rather than by writing a
    // settings file, so the panel is seeded the way the pickers seed it and the
    // shot shows what clicking through would produce.
    static void CaptureLabelSettings(string outDir)
    {
        void Shot(string file, string? stock, LabelBarcodeMode? barcode,
            bool cycles = true, bool faults = true, bool locks = true,
            double width = 1050, double height = 820, string theme = "Dark")
        {
            var vm = BuildDemoViewModel();
            vm.Theme = theme;
            vm.DeviceData = FaultyPhone();

            // Applied after the panel is open, because opening it reads the settings
            // and would otherwise overwrite them with whatever came first.
            CaptureExport(vm, outDir, file, width, height, seed: () =>
            {
                if (stock is not null) vm.LabelStockPartNumber = stock;
                if (barcode is not null) vm.LabelBarcodeMode = barcode.Value;
                vm.LabelShowBatteryCycles = cycles;
                vm.LabelShowFaults = faults;
                vm.LabelShowLocks = locks;
            });
        }

        // The roll most shops have, one code, everything on. The baseline.
        Shot("label-address-identifier.png", "1982991", LabelBarcodeMode.Identifier);

        // Two codes on the same roll: the words sit lower and every one of them has
        // to still fit, because this is where the faults line gets squeezed out.
        Shot("label-address-split.png", "1982991", LabelBarcodeMode.Split);

        // A barcode at all, for a till whose scanner cannot read one.
        Shot("label-address-none.png", "1982991", LabelBarcodeMode.None);

        // The narrower stock: a different sheet shape entirely, and a barcode that
        // does not fit on it, which has to be said on the panel rather than left as
        // a missing code.
        Shot("label-narrow-address.png", "30336", LabelBarcodeMode.Identifier);

        // The tall roll, where the same label has a great deal of room and the words
        // have to be set larger or the sheet reads as mostly paper.
        Shot("label-tall-shipping.png", "30256", LabelBarcodeMode.Identifier, width: 1050, height: 620);

        // The locks off. A shop can do this and the panel says so in the wording,
        // which is the last place that could warn about it.
        Shot("label-address-no-locks.png", "1982991", LabelBarcodeMode.Identifier, locks: false);

        // A clean phone with two codes: two bands of barcode and one line of words,
        // which is the case where a fixed layout would look like something is missing.
        var clean = BuildDemoViewModel();
        clean.Theme = "Dark";
        CaptureExport(clean, outDir, "label-address-clean-split.png", 1050, 820, seed: () =>
        {
            clean.LabelStockPartNumber = "1982991";
            clean.LabelBarcodeMode = LabelBarcodeMode.Split;
        });
    }

    // A phone with something wrong on it, which is the case the label's second and
    // third lines exist for. Every kind of fault at once, because the question a
    // screenshot answers is whether they all fit on the paper together and an
    // operator will meet a phone that has all of them.
    static PhoneGrade.Core.DeviceData FaultyPhone() => new()
    {
        Identifier = "356938035643809",
        Model = "iPhone 13 Pro",
        Color = "Graphite",
        Storage = "256GB",
        Memory = "6GB",
        BatteryHealth = "78",
        BatteryCycleCount = 612,
        Quality = "C",
        PayMethod = "Btw",
        FactoryResetProtection = PhoneGrade.Core.SecurityServices.FrpLockService.FrpLockStatus.Locked,
        ActivationLock = PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Locked,
        ComponentChecks =
        [
            new PhoneGrade.Core.ComponentStatus
            {
                Name = "Batterij", SerialRead = "L9", SerialOriginal = "K1",
                Status = PhoneGrade.Core.ComponentStatusType.Mismatch,
            },
        ],
        InteractiveTests = new PhoneGrade.Core.InteractiveTestSuiteResult
        {
            SessionId = "S",
            Tests =
            [
                new PhoneGrade.Core.InteractiveTestResult
                {
                    Id = "camera", Name = "Camera achter",
                    Status = PhoneGrade.Core.TestStatus.Failed,
                },
            ],
        },
    };

    // The files the export panel lists after a one click export, written for real
    // into a scratch folder so the photographed rows are the rows an operator
    // gets. Written rather than described, because a row is drawn from the
    // outcome and a described one would not prove the row draws.
    static LabelWriter.Batch SampleBatch(PhoneGrade.Core.DeviceData data, bool breakTheLabel = false)
    {
        string folder = Path.Combine(Path.GetTempPath(), "phonegrade-shot-exports");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);

        var wanted = new HashSet<ExportFormat>
  {
  ExportFormat.DymoLabel, ExportFormat.LabelPdf, ExportFormat.Json,
     };

        // The failure is produced rather than described. A template that is not
        // there is what an operator hits after moving the file their layout was
        // in, and asking the writer for it gives the panel the same row, the same
        // message and the same status line it would really produce.
        string? template = breakTheLabel
            ? Path.Combine(folder, "a-template-that-was-moved.dymo")
            : null;

        // The wording is handed in, as the panel does. Left out, the failure line
        // comes out in English and a shot of the failed state shows an English
        // sentence on an otherwise Dutch panel, which is the fault these shots
        // exist to catch.
        //
        // Blocking rather than awaiting: this is a console tool with no message loop
        // of its own to post a continuation to, and the work is a few milliseconds
        // of file writing on a background thread.
        return Task.Run(() => LabelWriter.WriteAsync(data, new LabelWriter.Request(
            wanted, folder, "shot", template,
            Messages: PhoneGrade.UI.Services.ExportWordingBuilder.Current())))
            .GetAwaiter().GetResult();
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

    // Every topic in the settings drawer. The drawer keeps one pane and swaps
    // what is inside it, so a section nobody photographs is a section that can
    // lose its layout without anything on screen saying so.
    static void CaptureSettingsSections(string outDir)
    {
        string settingsDir = Path.Combine(outDir, "settings-shots");
        Directory.CreateDirectory(settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", settingsDir);
        File.WriteAllText(Path.Combine(settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");

        string[] sections = { "General", "Workflow", "Connection", "License", "Support", "Advanced", "ImeiApi" };
        foreach (string section in sections)
        {
            var vm = BuildDemoViewModel();
            vm.Theme = "Dark";
            vm.IsSettingsDrawerOpen = true;
            vm.SelectedSettingsSection = section;

            // Tall enough that the whole pane is in the frame rather than the
            // half of it the window happens to show.
            Capture(new MainWindow { DataContext = vm },
                Path.Combine(outDir, $"settings-{section.ToLowerInvariant()}-dark.png"), 1050, 1500);
        }

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

        // The same panel once there is something in it, which is the half that
        // never appears otherwise: the rows it lists, the pill each one wears,
        // what the line at the top says afterwards, and the buttons that follow
        // from it. The report is written rather than scanned for, because a
        // scan runs the platform's own tools and would never finish on the
        // thread that has to sit there waiting for it, and because what it
        // finds depends on the machine doing the capturing. The panel is
        // filled through the same method the scan fills it with.
        var scanned = BuildDemoViewModel();
        scanned.TroubleshootViewModel.PublishReport(SampleReport());
        scanned.IsTroubleshootModalOpen = true;
        Shot(scanned, "flow-troubleshoot-results-dark.png");

        var quality = BuildDemoViewModel();
        quality.IsQualityPopupVisible = true;
        Shot(quality, "flow-quality-dark.png");

        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
    }

    // A report the way the diagnostics service writes one: keyed rather than
    // written out, one row of every severity so all four pills are in the
    // frame, a row for each category, and resolutions on the rows that can be
    // repaired, which is what puts the repair button at the top of the panel.
    static TroubleshootReport SampleReport() => new()
    {
        OverallStatusKey = "Diag_StatusAndroidOnly",
        Checks =
        {
            new DiagnosticCheckItem
            {
                Category = "iOS",
                TitleKey = "Diag_TitleIdeviceinfoFound",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgAvailable",
                MessageArgs = new[] { "C:\\Program Files\\PhoneGrade\\idevice-tools\\ideviceinfo.exe" },
            },
            new DiagnosticCheckItem
            {
                Category = "iOS",
                TitleKey = "Diag_TitleIdeviceIdMissing",
                Severity = DiagnosticSeverity.Fail,
                MessageKey = "Diag_MsgIdeviceIdExitCode",
                MessageArgs = new[] { "C:\\Program Files\\PhoneGrade\\idevice-tools\\idevice_id.exe", "9009" },
                ResolutionKey = "Diag_ResolveIdeviceWin",
                FixActionKey = "install_idevice_tools",
            },
            new DiagnosticCheckItem
            {
                Category = "Android",
                TitleKey = "Diag_TitleAdbMissing",
                Severity = DiagnosticSeverity.Warning,
                MessageKey = "Diag_MsgAdbFailed",
                MessageArgs = new[] { "C:\\Program Files\\PhoneGrade\\platform-tools\\adb.exe" },
                ResolutionKey = "Diag_ResolveAdbWin",
                FixActionKey = "install_adb",
            },
            new DiagnosticCheckItem
            {
                Category = "Connection",
                TitleKey = "Diag_TitleTunnel",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgAvailable",
                MessageArgs = new[] { "C:\\Program Files\\PhoneGrade\\idevice-tools\\cloudflared.exe" },
            },
            new DiagnosticCheckItem
            {
                Category = "Service",
                TitleKey = "Diag_TitleAppleDriverQuery",
                Severity = DiagnosticSeverity.Info,
                MessageKey = "Diag_MsgServiceQueryFailed",
                // The reason is the operating system's own words, so it follows the
                // machine rather than the app language: a Dutch Windows reports
                // error 1062 as this.
                MessageArgs = new[] { "De dienst is niet gestart." },
            },
            new DiagnosticCheckItem
            {
                Category = "Hardware",
                TitleKey = "Diag_TitleUsbDetection",
                Severity = DiagnosticSeverity.Warning,
                MessageKey = "Diag_MsgNoUsbDevice",
                ResolutionKey = "Diag_ResolveUsbCable",
            },
        },
    };

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
