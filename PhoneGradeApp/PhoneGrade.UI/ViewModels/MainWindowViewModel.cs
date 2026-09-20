using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Core.Diagnostics;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.Web;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DynamicData;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// Main window logic. One button for the user: the flow starts automatically when a
/// device is plugged in (when AutoDetectOnPlug is on), walks connect → activate →
/// read → diagnose → label, and asks only the questions settings can't answer
/// (quality, payment) unless defaults are configured.
/// </summary>
public class MainWindowViewModel : ReactiveObject
{
    private readonly AppSettings _settings;
    private CancellationTokenSource? _flowCts;
    private IDisposable? _watcher;

    public ObservableCollection<DiagnosticIssue> Issues { get; } = [];
    public ObservableCollection<ComponentStatus> ComponentChecks { get; } = [];
    public UnifiedLogsViewModel LogsViewModel { get; } = new();
    public TroubleshootViewModel TroubleshootViewModel { get; } = new();

    private ClientTelemetry? _currentTelemetry;
    public ClientTelemetry? CurrentTelemetry
    {
        get => _currentTelemetry;
        set => this.RaiseAndSetIfChanged(ref _currentTelemetry, value);
    }

    private ObservableCollection<KeyValuePair<string, string>> _devices = [];
    public ObservableCollection<KeyValuePair<string, string>> Devices
    {
        get => _devices;
        set => this.RaiseAndSetIfChanged(ref _devices, value);
    }

    private KeyValuePair<string, string> _selectedDevice;
    public KeyValuePair<string, string> SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDevice, value);
            UpdateWebRunnerSession(value.Key);
        }
    }

    // Web Test Runner (PWA) Properties
    private TestRunnerServer? _webServer;
    private string _webRunnerUrl = "";
    public string WebRunnerUrl
    {
        get => _webRunnerUrl;
        set => this.RaiseAndSetIfChanged(ref _webRunnerUrl, value);
    }

    private Bitmap? _qrCodeBitmap;
    public Bitmap? QrCodeBitmap
    {
        get => _qrCodeBitmap;
        set => this.RaiseAndSetIfChanged(ref _qrCodeBitmap, value);
    }

    private string _interactiveSessionStatus = "Web runner standby";
    public string InteractiveSessionStatus
    {
        get => _interactiveSessionStatus;
        set => this.RaiseAndSetIfChanged(ref _interactiveSessionStatus, value);
    }

    private string _interactiveTestSummary = "";
    public string InteractiveTestSummary
    {
        get => _interactiveTestSummary;
        set => this.RaiseAndSetIfChanged(ref _interactiveTestSummary, value);
    }

    private int _progress;
    public int Progress { get => _progress; set => this.RaiseAndSetIfChanged(ref _progress, value); }

    private string _status = "Plug een iPhone/iPad in om te starten…";
    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private string _theme = "Dark";
    public string Theme
    {
        get => _theme;
        set
        {
            _settings.Theme = value;
            _settings.Save();
            PhoneGrade.UI.App.ApplyTheme(value);
            this.RaiseAndSetIfChanged(ref _theme, value);
        }
    }

    private string _language = "Nederlands";
    public string Language
    {
        get => _language;
        set
        {
            string code = value == "English" ? "en" : "nl";
            _settings.Language = code;
            _settings.Save();
            Services.LocalizationManager.SetLanguage(code);
            this.RaiseAndSetIfChanged(ref _language, value);
        }
    }
    
    public string[] LanguageOptions { get; } = { "Nederlands", "English" };

    private bool _autoActivate = true;
    public bool AutoActivate
    {
        get => _autoActivate;
        set { _settings.AutoActivate = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _autoActivate, value); }
    }

    private bool _autoDetectOnPlug = true;
    public bool AutoDetectOnPlug
    {
        get => _autoDetectOnPlug;
        set
        {
            _settings.AutoDetectOnPlug = value;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _autoDetectOnPlug, value);
            if (value) StartWatcher(); else StopWatcher();
        }
    }

    private bool _runDiagnostics = true;
    public bool RunDiagnostics
    {
        get => _runDiagnostics;
        set { _settings.RunDiagnostics = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _runDiagnostics, value); }
    }

    private bool _enable85PercentChecker = true;
    public bool Enable85PercentChecker
    {
        get => _enable85PercentChecker;
        set { _settings.Enable85PercentChecker = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _enable85PercentChecker, value); }
    }

    private bool _openEditorBeforePrint;
    public bool OpenEditorBeforePrint
    {
        get => _openEditorBeforePrint;
        set { _settings.OpenEditorBeforePrint = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _openEditorBeforePrint, value); }
    }

    private bool _autoStartWebTest;
    public bool AutoStartWebTest
    {
        get => _autoStartWebTest;
        set { _settings.AutoStartWebTest = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _autoStartWebTest, value); }
    }

    private bool _showSummaryScreenAfterTesting = true;
    public bool ShowSummaryScreenAfterTesting
    {
        get => _showSummaryScreenAfterTesting;
        set { _settings.ShowSummaryScreenAfterTesting = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _showSummaryScreenAfterTesting, value); }
    }

    private string _defaultQuality = "";
    public string DefaultQuality
    {
        get => _defaultQuality;
        set { _settings.DefaultQuality = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _defaultQuality, value); }
    }

    private string _defaultPaymentMethod = "";
    public string DefaultPaymentMethod
    {
        get => _defaultPaymentMethod;
        set { _settings.DefaultPaymentMethod = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _defaultPaymentMethod, value); }
    }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        set => this.RaiseAndSetIfChanged(ref _busy, value);
    }

    private DeviceData _deviceData = new();
    public DeviceData DeviceData
    {
        get => _deviceData;
        set
        {
            this.RaiseAndSetIfChanged(ref _deviceData, value);
            this.RaisePropertyChanged(nameof(HasDevice));
        }
    }

    /// <summary>True once real device data has been read — drives the summary grid.</summary>
    public bool HasDevice => DeviceData is { Model: not "NOMODEL", Identifier: not "NOID" };

    // Popups kept minimal: only quality & payment, and only when no default is set.
    private bool _isQualityPopupVisible;
    public bool IsQualityPopupVisible { get => _isQualityPopupVisible; set => this.RaiseAndSetIfChanged(ref _isQualityPopupVisible, value); }

    private bool _isPaymentPopupVisible;
    public bool IsPaymentPopupVisible { get => _isPaymentPopupVisible; set => this.RaiseAndSetIfChanged(ref _isPaymentPopupVisible, value); }

    private bool _hasIssues;
    public bool HasIssues { get => _hasIssues; set => this.RaiseAndSetIfChanged(ref _hasIssues, value); }

    public string[] ThemeOptions { get; } = ["Dark", "Light", "System"];
    public string[] QualityOptions { get; } = ["", "A", "B", "C"];
    public string[] PaymentOptions { get; } = ["", "Marge", "BTW"];

    public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> RetestCommand { get; }
    public ReactiveCommand<string, Unit> SetQualityCommand { get; }
    public ReactiveCommand<string, Unit> SetPaymentMethodCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLabelCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenEditorCommand { get; }

    public event Action<DeviceData>? DataEditorRequested;

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        _theme = _settings.Theme;
        _language = _settings.Language == "en" ? "English" : "Nederlands";
        _autoActivate = _settings.AutoActivate;
        _autoDetectOnPlug = _settings.AutoDetectOnPlug;
        _runDiagnostics = _settings.RunDiagnostics;
        _enable85PercentChecker = _settings.Enable85PercentChecker;
        _openEditorBeforePrint = _settings.OpenEditorBeforePrint;
        _autoStartWebTest = _settings.AutoStartWebTest;
        _showSummaryScreenAfterTesting = _settings.ShowSummaryScreenAfterTesting;
        _defaultQuality = _settings.DefaultQuality;
        _defaultPaymentMethod = _settings.DefaultPaymentMethod;
        LabelService.ConfiguredTemplatePath = _settings.TemplatePath;

        var canStart = this.WhenAnyValue(x => x.Busy).Select(b => !b);
        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(RefreshDeviceListAsync);
        StartCommand = ReactiveCommand.CreateFromTask(() => RunFlowAsync(), canStart);
        RetestCommand = ReactiveCommand.CreateFromTask(RetestCurrentDeviceAsync, canStart);
        SetQualityCommand = ReactiveCommand.Create<string>(q => _ = ContinueAfterQualityAsync(q));
        SetPaymentMethodCommand = ReactiveCommand.Create<string>(p => ContinueAfterPaymentAsync(p));
        OpenLabelCommand = ReactiveCommand.Create(OpenLabel);
        OpenEditorCommand = ReactiveCommand.Create(() => DataEditorRequested?.Invoke(DeviceData));

        _ = RefreshDeviceListAsync();
        if (_autoDetectOnPlug) StartWatcher();
        StartWebRunnerServer();
    }

    private void StartWebRunnerServer()
    {
        try
        {
            _webServer = new TestRunnerServer(5055);
            _webServer.DeviceConnected += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    InteractiveSessionStatus = $"Toestel verbonden voor webtest ({e.SessionId})";
                });
            };

            _webServer.MessageReceived += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (e.Message?.Type == "test_progress")
                    {
                        InteractiveSessionStatus = $"Test bezig: {e.Message.TestName} ({e.Message.Progress}%)";
                    }
                    else if (e.Message?.Type == "test_complete")
                    {
                        InteractiveSessionStatus = $"Test afgerond: {e.Message.TestName} -> {e.Message.Status}";
                    }
                });
            };

            _webServer.SuiteCompleted += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (e.Message?.Payload != null)
                    {
                        ApplyInteractiveResults(e.Message.Payload);
                    }
                });
            };

            _webServer.LogEventReceived += (s, e) =>
            {
                SystemEventLogger.Log(e.LogEvent.Level, LogSource.PwaClient, e.LogEvent.Message ?? "", e.SessionId);
            };

            _webServer.TelemetryReceived += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CurrentTelemetry = e.Telemetry;
                });
                SystemEventLogger.Info(
                    LogSource.PwaClient,
                    $"Telemetry received: {e.Telemetry.Browser} {e.Telemetry.BrowserVersion} on {e.Telemetry.Os} {e.Telemetry.OsVersion} ({e.Telemetry.ScreenWidth}x{e.Telemetry.ScreenHeight} @{e.Telemetry.PixelRatio}x, Touch: {e.Telemetry.TouchSupport})",
                    e.SessionId);
            };

            _ = _webServer.StartAsync().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateWebRunnerSession(SelectedDevice.Key);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            InteractiveSessionStatus = $"Web runner fout: {ex.Message}";
        }
    }

    public void UpdateWebRunnerSession(string? udid)
    {
        try
        {
            var ip = QrCodeService.GetLocalIpAddress();
            int port = _webServer?.BoundPort > 0 ? _webServer.BoundPort : 5055;
            var sessionUdid = !string.IsNullOrWhiteSpace(udid) ? udid : (DeviceData.Identifier != "NOID" ? DeviceData.Identifier : "DEMO");
            WebRunnerUrl = QrCodeService.GenerateSessionUrl(ip, port, sessionUdid);
            QrCodeBitmap = QrCodeService.GenerateQrCodeBitmap(WebRunnerUrl);
            InteractiveSessionStatus = $"Scan QR of open: {WebRunnerUrl}";
        }
        catch (Exception ex)
        {
            InteractiveSessionStatus = $"QR fout: {ex.Message}";
        }
    }

    public void ApplyInteractiveResults(InteractiveTestSuiteResult suite)
    {
        DeviceData.InteractiveTests = suite;
        var newIssues = DeviceData.MergeInteractiveResults(Issues.ToList());
        Issues.Clear();
        foreach (var iss in newIssues)
        {
            Issues.Add(iss);
        }
        HasIssues = Issues.Count > 0;

        int passed = suite.Tests.Count(t => t.Status == TestStatus.Passed);
        int failed = suite.Tests.Count(t => t.Status == TestStatus.Failed);
        InteractiveTestSummary = $"Interactieve tests: {passed} geslaagd, {failed} gefaald ({suite.Platform})";
        InteractiveSessionStatus = suite.AllPassed
            ? "Interactieve hardwaretest: Alles geslaagd!"
            : $"Interactieve hardwaretest voltooid: {failed} fout(en)";
    }

    /// <summary>Polls for device changes every 2s; starts the auto flow on first sight of a device.</summary>
    private void StartWatcher()
    {
        StopWatcher();
        _watcher = Observable.Interval(TimeSpan.FromSeconds(2))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .SelectMany(_ => Observable.FromAsync(RefreshDeviceListSilentAsync))
            .Where(c => c > 0)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (Busy) { return; }
                string? udid = SelectedDevice.Key;
                if (udid is { Length: > 0 })
                {
                    // Only run flow if device hasn't already completed testing
                    if (!DeviceSessionManager.IsDeviceCompleted(udid))
                    {
                        // Fire-and-forget: RunFlowAsync reports all failures through Status.
#pragma warning disable CS4014
                        RunFlowAsync();
#pragma warning restore CS4014
                    }
                }
            });
    }

    private void StopWatcher() => _watcher?.Dispose();

    /// <summary>True when a device list refresh surfaced exactly one usable device.</summary>
    private async Task<int> RefreshDeviceListSilentAsync()
    {
        try
        {
            var devices = await DeviceService.GetConnectedDevicesAsync();
            if (devices.Count == 0) return 0;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Devices = new ObservableCollection<KeyValuePair<string, string>>(devices);
                // auto-select the single/first device if available
                if (Devices.Count > 0)
                {
                    string currentKey = SelectedDevice.Key ?? "";
                    if (!devices.ContainsKey(currentKey))
                        SelectedDevice = Devices[0];
                }
            });
            return devices.Count;
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.UsbDetector, $"Silent refresh caught error: {ex.Message}");
            return 0;
        }
    }

    public async Task RefreshDeviceListAsync()
    {
        int count = await RefreshDeviceListSilentAsync();
        if (count == 0)
        {
            var (_, _, diagState) = await DeviceService.ListUdidsSafeAsync();
            Status = diagState switch
            {
                DeviceService.ConnectionState.ToolsMissing =>
                    "USB tools ontbreken: noch libimobiledevice noch adb is geinstalleerd of vindbaar in PATH. Open de Troubleshoot tab voor installatie-instructies.",
                DeviceService.ConnectionState.Unauthorized =>
                    "ADB unauthorized: ontgrendel Android toestel en accepteer USB-foutopsporing (RSA-sleutel).",
                DeviceService.ConnectionState.PermissionDenied =>
                    "Executable permissions missing: voer chmod +x uit op de tools of controleer Gatekeeper.",
                DeviceService.ConnectionState.DriverMissing =>
                    "Apple USB Driver missing: installeer iTunes of Apple Mobile Device Support.",
                DeviceService.ConnectionState.DaemonStopped =>
                    OperatingSystem.IsWindows()
                        ? "Apple Mobile Device Service is gestopt: start de Windows service 'Apple Mobile Device Service'."
                        : "usbmuxd daemon draait niet: start usbmuxd via launchctl of systemctl.",
                DeviceService.ConnectionState.NotTrusted =>
                    "Waiting for trust confirmation on device: ontgrendel toestel en tik op 'Vertrouwen'.",
                _ =>
                    "Geen toestel gevonden. Kabel/poort proberen of toestel ontgrendelen en 'Vertrouwen' tikken."
            };
            return;
        }

        Status = count switch
        {
            1 => "Eén toestel gevonden en geselecteerd.",
            _ => $"{count} toestellen gevonden: kies er één.",
        };
    }

    /// <summary>Resets the current device session and retests it.</summary>
    private async Task RetestCurrentDeviceAsync()
    {
        string? udid = SelectedDevice.Key;
        if (!string.IsNullOrWhiteSpace(udid))
        {
            DeviceSessionManager.ResetDevice(udid);
            await RunFlowAsync();
        }
    }

    /// <summary>The whole pipeline for one device.</summary>
    private async Task RunFlowAsync()
    {
        string? udid = SelectedDevice.Key;
        if (udid is not { Length: > 0 })
        {
            Status = "Geen toestel geselecteerd.";
            return;
        }

        Busy = true;
        _flowCts = new CancellationTokenSource();
        Issues.Clear();
        ComponentChecks.Clear();
        HasIssues = false;
        Progress = 5;
        try
        {
            // 1. Trust / connectivity
            Status = "Verbinding controleren…";
            var state = await DeviceService.GetConnectionStateAsync(udid);
            if (state == DeviceService.ConnectionState.Unauthorized)
            {
                Status = "ADB unauthorized: ontgrendel Android toestel en accepteer USB-foutopsporing (RSA-sleutel).";
                return;
            }
            if (state == DeviceService.ConnectionState.NotTrusted)
            {
                Status = "Waiting for trust confirmation on device: ontgrendel toestel en tik op 'Vertrouwen'.";
                return;
            }
            if (state == DeviceService.ConnectionState.PermissionDenied)
            {
                Status = "Executable permissions missing: bestandspermissies ontoereikend.";
                return;
            }
            if (state == DeviceService.ConnectionState.DriverMissing)
            {
                Status = "Apple USB Driver missing: installeer Apple Mobile Device Support.";
                return;
            }
            if (state == DeviceService.ConnectionState.DaemonStopped)
            {
                Status = OperatingSystem.IsWindows()
                    ? "Apple Mobile Device Service is gestopt: start de service in Windows Services."
                    : "usbmuxd daemon draait niet: start usbmuxd.";
                return;
            }
            if (state != DeviceService.ConnectionState.Connected)
            {
                Status = "Toestel niet bereikbaar: andere kabel/poort proberen.";
                return;
            }
            Progress = 20;

            // 2. Activation bypass (optional)
            if (AutoActivate && state == DeviceService.ConnectionState.NotActivated ||
                AutoActivate && await NeedsActivationAsync(udid))
            {
                Status = "Toestel activeren (bypass)…";
                string result = await ActivationService.SkipActivationAsync(udid);
                Status = result;
            }
            Progress = 40;

            // 3. Read device data
            Status = "Toesteldata uitlezen…";
            DeviceData = await DeviceService.GetDeviceDataAsync(udid);
            ComponentChecks.Clear();
            foreach (var check in DeviceData.ComponentChecks)
                ComponentChecks.Add(check);
            Progress = 60;

            // 4. Battery checker
            if (Enable85PercentChecker && int.TryParse(DeviceData.BatteryHealth, out int health) && health < 85)
                DeviceData.BatteryHealth = "100%-X";

            // 5. Diagnostics (panic logs & sensors) — advisory, not blocking
            if (RunDiagnostics)
            {
                Status = "Diagnostiek draaien (panic-logs)…";
                foreach (var issue in await DiagnosticService.DiagnoseAsync(udid))
                    Issues.Add(issue);
                HasIssues = Issues.Count > 0;
            }
            Progress = 75;

            // 6. Quality + payment: defaults from settings, else popup
            if (DefaultQuality is { Length: > 0 })
                await ContinueAfterQualityAsync(DefaultQuality);
            else
            {
                IsQualityPopupVisible = true;
                Status = "Kies de kwaliteit…";
            }
        }
        catch (Exception ex)
        {
            Status = $"Fout: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task<bool> NeedsActivationAsync(string udid) =>
        (await DeviceService.GetKeyAsync(udid, "ActivationState")).Contains("Unactivated");

    private async Task ContinueAfterQualityAsync(string quality)
    {
        DeviceData.Quality = quality;
        Progress = 85;
        if (DefaultPaymentMethod is { Length: > 0 })
            await ContinueAfterPaymentAsync(DefaultPaymentMethod);
        else
        {
            IsQualityPopupVisible = false;
            IsPaymentPopupVisible = true;
        }
    }

    private Task ContinueAfterPaymentAsync(string method)
    {
        DeviceData.PayMethod = method;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
        Progress = 95;

        if (OpenEditorBeforePrint)
        {
            DataEditorRequested?.Invoke(DeviceData);
            return Task.CompletedTask;
        }

        FinishLabel();
        
        // Mark device session as completed to prevent auto-retesting
        string? udid = SelectedDevice.Key;
        if (!string.IsNullOrWhiteSpace(udid))
        {
            DeviceSessionManager.MarkCompleted(udid, wasSuccessful: true);
        }
        
        return Task.CompletedTask;
    }

    /// <summary>Generate + open the label; single click path for the user.</summary>
    public void FinishLabel()
    {
        try
        {
            Status = "Label genereren…";
            string path = LabelService.GenerateLabel(DeviceData);
            AuditLogService.ExportAuditLog(DeviceData);
            Status = LabelService.OpenLabelFile(path);
            Progress = 100;
        }
        catch (Exception ex)
        {
            Status = $"Label mislukt: {ex.Message}";
        }
    }

    private void OpenLabel()
    {
        if (File.Exists(LabelService.OutputPath))
            Status = LabelService.OpenLabelFile();
        else
            Status = "Nog geen label gegenereerd.";
    }
}
