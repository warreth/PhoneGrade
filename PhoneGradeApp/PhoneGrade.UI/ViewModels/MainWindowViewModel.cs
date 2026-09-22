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

    private KeyValuePair<string, string> _selectedDevice = new KeyValuePair<string, string>("", "");
    public KeyValuePair<string, string> SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDevice, value);
            if (!string.IsNullOrEmpty(value.Key))
            {
                UpdateWebRunnerSession(value.Key);
            }
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

    private string _status = "Sluit een toestel aan om te starten...";
    public string Status
    {
        get => _status;
        set
        {
            string sanitized = SanitizeStatusMessage(value);
            this.RaiseAndSetIfChanged(ref _status, sanitized);
        }
    }

    private static string SanitizeStatusMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (raw.Contains("Could not connect to lockdownd") || raw.Contains("Mux error"))
            return "Verbindingsfout met toestel (lockdownd).";
        if (raw.Contains("PairingDialogResponsePending") || raw.Contains("PasswordProtected"))
            return "Wachten op toestemming op iPhone...";
        if (raw.Contains("unauthorized"))
            return "Wachten op RSA-autorisatie op Android...";
        if (raw.StartsWith("ERROR:") || raw.StartsWith("Fout:"))
        {
            if (raw.Contains("timed out")) return "Time-out bij communicatie.";
            return "Communicatiefout met toestel.";
        }
        return raw;
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

    private bool _autoActivate;
    public bool AutoActivate
    {
        get => _autoActivate;
        set { _settings.AutoActivate = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _autoActivate, value); }
    }

    private bool _autoDetectOnPlug;
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

    private bool _requirePwaTest = true;
    public bool RequirePwaTest
    {
        get => _requirePwaTest;
        set { _settings.RequirePwaTest = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _requirePwaTest, value); }
    }

    private bool _autoFinishAfterTest;
    public bool AutoFinishAfterTest
    {
        get => _autoFinishAfterTest;
        set { _settings.AutoFinishAfterTest = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _autoFinishAfterTest, value); }
    }

    private string _imeiApiKey = "";
    public string ImeiApiKey
    {
        get => _imeiApiKey;
        set { _settings.ImeiApiKey = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _imeiApiKey, value); }
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

    /// <summary>True once real device data has been read: drives the summary grid.</summary>
    public bool HasDevice => DeviceData is { Model: not "NOMODEL", Identifier: not "NOID" };

    // Kiosk state-driven workflow properties
    private AppWorkflowState _workflowState = AppWorkflowState.Idle;
    public AppWorkflowState WorkflowState
    {
        get => _workflowState;
        set
        {
            this.RaiseAndSetIfChanged(ref _workflowState, value);
            this.RaisePropertyChanged(nameof(IsIdleState));
            this.RaisePropertyChanged(nameof(IsActiveState));
            this.RaisePropertyChanged(nameof(IsSummaryState));
        }
    }

    public bool IsIdleState => WorkflowState == AppWorkflowState.Idle;
    public bool IsActiveState => WorkflowState == AppWorkflowState.Active;
    public bool IsSummaryState => WorkflowState == AppWorkflowState.Summary;

    // Overlay Drawer & Modal Flags
    private bool _isSettingsDrawerOpen;
    public bool IsSettingsDrawerOpen
    {
        get => _isSettingsDrawerOpen;
        set => this.RaiseAndSetIfChanged(ref _isSettingsDrawerOpen, value);
    }

    private bool _showAdvancedSettings;
    public bool ShowAdvancedSettings
    {
        get => _showAdvancedSettings;
        set => this.RaiseAndSetIfChanged(ref _showAdvancedSettings, value);
    }

    private bool _isLogsModalOpen;
    public bool IsLogsModalOpen
    {
        get => _isLogsModalOpen;
        set => this.RaiseAndSetIfChanged(ref _isLogsModalOpen, value);
    }

    private bool _isTroubleshootModalOpen;
    public bool IsTroubleshootModalOpen
    {
        get => _isTroubleshootModalOpen;
        set => this.RaiseAndSetIfChanged(ref _isTroubleshootModalOpen, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLogsModalCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseLogsModalCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenTroubleshootModalCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseTroubleshootModalCommand { get; }
    public ReactiveCommand<Unit, Unit> BackToIdleCommand { get; }

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
    public ReactiveCommand<Unit, Unit> FinishInspectionCommand { get; }

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
        _requirePwaTest = _settings.RequirePwaTest;
        _autoFinishAfterTest = _settings.AutoFinishAfterTest;
        _imeiApiKey = _settings.ImeiApiKey ?? "";
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
        FinishInspectionCommand = ReactiveCommand.CreateFromTask(FinishInspectionAsync);

        ToggleSettingsCommand = ReactiveCommand.Create(() => { IsSettingsDrawerOpen = !IsSettingsDrawerOpen; });
        OpenLogsModalCommand = ReactiveCommand.Create(() => { IsLogsModalOpen = true; IsSettingsDrawerOpen = false; });
        CloseLogsModalCommand = ReactiveCommand.Create(() => { IsLogsModalOpen = false; });
        OpenTroubleshootModalCommand = ReactiveCommand.Create(() => { IsTroubleshootModalOpen = true; IsSettingsDrawerOpen = false; });
        CloseTroubleshootModalCommand = ReactiveCommand.Create(() => { IsTroubleshootModalOpen = false; });
        BackToIdleCommand = ReactiveCommand.Create(() => { WorkflowState = AppWorkflowState.Idle; });

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
                        if (AutoFinishAfterTest)
                        {
                            _ = FinishInspectionAsync();
                        }
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

            // If AutoStartWebTest is enabled, send a signal to connected PWA clients to auto-start the test suite
            if (AutoStartWebTest && _webServer != null)
            {
                _webServer.BroadcastMessage(new { type = "auto_start_suite", sessionId = sessionUdid });
            }
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
        _watcher = Observable.Interval(TimeSpan.FromSeconds(2.5)) // Throttled to prevent lockdownd crashes
            .ObserveOn(RxApp.TaskpoolScheduler)
            .SelectMany(_ => Observable.FromAsync(RefreshDeviceListSilentAsync))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(count =>
            {
                // Hot Unplug Detection
                if (count == 0)
                {
                    if (WorkflowState != AppWorkflowState.Idle)
                    {
                        ResetToIdle();
                    }
                    return;
                }

                // Only auto-start if AutoDetectOnPlug is explicitly enabled AND device hasn't started yet
                if (!AutoDetectOnPlug || Busy) { return; }

                string? udid = SelectedDevice.Key;
                if (udid is { Length: > 0 })
                {
                    // Check if this device was disconnected mid-test and can be resumed
                    if (DeviceSessionManager.TryGetPreservedSession(udid, out var preserved) && preserved?.Data != null)
                    {
                        DeviceData = preserved.Data;
                        Progress = preserved.SavedProgress;
                        ComponentChecks.Clear();
                        foreach (var c in DeviceData.ComponentChecks) ComponentChecks.Add(c);
                        WorkflowState = AppWorkflowState.Active;
                        Status = "Toestel heraangesloten: sessie hervat.";
                        DeviceSessionManager.MarkStarted(udid, DeviceData);
                        UpdateWebRunnerSession(udid);
                        return;
                    }

                    if (!DeviceSessionManager.HasStartedOrCompleted(udid))
                    {
                        DeviceSessionManager.MarkStarted(udid);
#pragma warning disable CS4014
                        RunFlowAsync();
#pragma warning restore CS4014
                    }
                }
            });
    }

    private void StopWatcher() => _watcher?.Dispose();

    private void ResetToIdle()
    {
        string? lastUdid = SelectedDevice.Key;
        if (_flowCts != null)
        {
            _flowCts.Cancel();
            _flowCts.Dispose();
            _flowCts = null;
        }

        // Preserve session if unhooked mid-test
        if (WorkflowState == AppWorkflowState.Active && !string.IsNullOrWhiteSpace(lastUdid) && DeviceData != null)
        {
            DeviceSessionManager.PreserveDisconnectedSession(lastUdid, DeviceData, Progress);
            Status = "Toestel losgekoppeld. Sluit hetzelfde toestel opnieuw aan om verder te gaan.";
        }
        else
        {
            Status = "Sluit een toestel aan via USB om te starten...";
        }

        WorkflowState = AppWorkflowState.Idle;
        Busy = false;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
    }

    /// <summary>True when a device list refresh surfaced exactly one usable device.</summary>
    private async Task<int> RefreshDeviceListSilentAsync()
    {
        try
        {
            var devices = await DeviceService.GetConnectedDevicesAsync();
            if (devices.Count == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Devices.Clear();
                    SelectedDevice = new KeyValuePair<string, string>("", "");
                });
                return 0;
            }
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
            // Transition back to Idle if no device is connected
            if (WorkflowState != AppWorkflowState.Idle)
            {
                WorkflowState = AppWorkflowState.Idle;
            }

            var (_, _, diagState) = await DeviceService.ListUdidsSafeAsync();
            Status = diagState switch
            {
                DeviceService.ConnectionState.ToolsMissing =>
                    "USB tools ontbreken: noch libimobiledevice noch adb is geinstalleerd of vindbaar in PATH. Open de Troubleshoot tab voor installatie-instructies.",
                DeviceService.ConnectionState.Unauthorized =>
                    "Wachten op RSA-autorisatie op Android. Accepteer USB-foutopsporing.",
                DeviceService.ConnectionState.PermissionDenied =>
                    "Rechten ontbreken (chmod +x nodig of Gatekeeper waarschuwing).",
                DeviceService.ConnectionState.DriverMissing =>
                    "Apple USB Driver ontbreekt. Installeer iTunes of Apple Mobile Device Support.",
                DeviceService.ConnectionState.DaemonStopped =>
                    OperatingSystem.IsWindows()
                        ? "Apple Mobile Device Service is gestopt: start de Windows service 'Apple Mobile Device Service'."
                        : "usbmuxd daemon draait niet.",
                DeviceService.ConnectionState.NotTrusted =>
                    "Wachten op toestemming op iPhone. Ontgrendel en tik op 'Vertrouw'.",
                _ =>
                    "Geen toestel gevonden. Controleer de kabel of ontgrendel het toestel."
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
        WorkflowState = AppWorkflowState.Active;
        DeviceSessionManager.MarkStarted(udid);
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
                Status = "Wachten op RSA-autorisatie op Android. Accepteer USB-foutopsporing.";
                WorkflowState = AppWorkflowState.Idle; // Fallback to Idle
                return;
            }
            if (state == DeviceService.ConnectionState.NotTrusted)
            {
                Status = "Wachten op toestemming op iPhone. Ontgrendel en tik op 'Vertrouw'.";
                WorkflowState = AppWorkflowState.Idle; // Fallback to Idle
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
                Status = "Toestel niet bereikbaar: probeer een andere kabel of poort.";
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

            // 5. Diagnostics (panic logs & sensors): advisory, not blocking
            if (RunDiagnostics)
            {
                Status = "Diagnostiek draaien (panic-logs)…";
                foreach (var issue in await DiagnosticService.DiagnoseAsync(udid))
                    Issues.Add(issue);
                HasIssues = Issues.Count > 0;
            }
            Progress = 75;

            // Wait for user to explicitly click 'Afronden'
            Status = "Specificaties gelezen. Voer de interactieve test uit en klik op 'Afronden'.";
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
        this.RaisePropertyChanged(nameof(DeviceData));
        this.RaisePropertyChanged("DeviceData.Quality");
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
        this.RaisePropertyChanged(nameof(DeviceData));
        this.RaisePropertyChanged("DeviceData.PayMethod");
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
        Progress = 95;

        // Mark device session as completed to prevent auto-retesting
        string? udid = SelectedDevice.Key;
        if (!string.IsNullOrWhiteSpace(udid))
        {
            DeviceSessionManager.MarkCompleted(udid);
        }

        if (OpenEditorBeforePrint)
        {
            DataEditorRequested?.Invoke(DeviceData);
            return Task.CompletedTask;
        }

        if (ShowSummaryScreenAfterTesting)
        {
            WorkflowState = AppWorkflowState.Summary;
            Progress = 100;
            Status = "Testen voltooid. Controleer de resultaten en print het label.";
        }
        else
        {
            FinishLabel();
            WorkflowState = AppWorkflowState.Summary;
        }
        
        return Task.CompletedTask;
    }

    /// <summary>Generate + open the label; single click path for the user.</summary>
    private async Task FinishInspectionAsync()
    {
        // Check if PWA test is required and not completed
        if (RequirePwaTest && DeviceData.InteractiveTests == null)
        {
            Status = "PWA hardwaretest is verplicht. Voer eerst de interactieve test uit.";
            return;
        }

        // 6. Quality + payment: defaults from settings, else popup
        if (DefaultQuality is { Length: > 0 })
            await ContinueAfterQualityAsync(DefaultQuality);
        else
        {
            IsQualityPopupVisible = true;
            Status = "Kies de kwaliteit...";
        }
    }

    private void FinishLabel()
    {
        try
        {
            Status = "Label genereren…";
            string path = LabelService.GenerateLabel(DeviceData);
            AuditLogService.ExportAuditLog(DeviceData);
            Status = LabelService.OpenLabelFile(path);
            Progress = 100;
            WorkflowState = AppWorkflowState.Summary;
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
