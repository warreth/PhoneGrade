using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Core.Diagnostics;
using PhoneGrade.Core.Licensing;
using PhoneGrade.Core.Usb;
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
public class MainWindowViewModel : ReactiveObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly DevicePresenceTracker _presence = new();
    private CancellationTokenSource? _flowCts;
    private IDisposable? _watcher;

    // Licensing
    private readonly LemonSqueezyClient _licenseClient;
    private readonly TrialGate _trialGate;
    private LicensingViewModel? _licensingViewModel;

    public ObservableCollection<DiagnosticIssue> Issues { get; } = [];
    public ObservableCollection<ComponentStatus> ComponentChecks { get; } = [];
    public ObservableCollection<ComponentStatus> DefectiveComponents { get; } = [];
    public ObservableCollection<InteractiveTestResult> FailedInteractiveTests { get; } = [];

    /// <summary>
    /// The tests the phone did not run. Not a failure, but not a pass either, and
    /// leaving them out of the report is what makes a run look complete when part
    /// of it never happened.
    /// </summary>
    public ObservableCollection<InteractiveTestResult> SkippedInteractiveTests { get; } = [];

    /// <summary>
    /// The rows the operator took out of the report. Kept as rows rather than
    /// as a number so the count cannot drift from what is actually hidden.
    /// </summary>
    public ObservableCollection<InteractiveTestResult> DismissedInteractiveTests { get; } = [];

    /// <summary>True when the report hides at least one row, so the undo line has something to restore.</summary>
    public bool HasDismissedInteractiveTests => DismissedInteractiveTests.Count > 0;

    /// <summary>How many rows the report is leaving out. The line that says so needs the figure.</summary>
    public int DismissedInteractiveTestCount => DismissedInteractiveTests.Count;

    /// <summary>
    /// True when the inspection report has nothing to show about the phone's
    /// own tests, the dismissed rows included.
    ///
    /// A run whose failures were all dismissed is not a clean run, and the
    /// green line saying so would have the report misinform whoever reads it
    /// next.
    /// </summary>
    public bool NoInteractiveTestProblems =>
        FailedInteractiveTests.Count == 0
        && SkippedInteractiveTests.Count == 0
        && DismissedInteractiveTests.Count == 0;
    public UnifiedLogsViewModel LogsViewModel { get; } = new();
    public TroubleshootViewModel TroubleshootViewModel { get; } = new();

    /// <summary>
    /// The export panel. Owned here because it needs the device on screen, the
    /// settings and the battery threshold, and because the summary screen is where
    /// it is opened from. Declared nullable and set in the constructor so that the
    /// compiler is forced to say when it is missing.
    /// </summary>
    public ExportViewModel? ExportViewModel { get; private set; }

    // USB Event Monitoring & ADB Tutorial
    private readonly AdbDeviceDirector _adbDirector;
    public AdbTutorialViewModel AdbTutorialViewModel { get; }

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
    private readonly AdbReverseTunnel _adbTunnel = AdbReverseTunnel.CreateDefault();
    private readonly QuickTunnel _quickTunnel = QuickTunnel.CreateDefault();
    private readonly WebRunnerOriginResolver _originResolver;

    /// <summary>
    /// Counts the session requests, so an answer that arrives after the next one
    /// is dropped instead of overwriting it.
    /// </summary>
    private int _webRunnerGeneration;
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

    private string _interactiveSessionStatus = LocalizationManager.GetString("Status_WebStandby");
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

    private string _status = LocalizationManager.GetString("Status_ConnectToStart");
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
            return LocalizationManager.GetString("Status_LockdownError");
        if (raw.Contains("PairingDialogResponsePending") || raw.Contains("PasswordProtected"))
            return LocalizationManager.GetString("Status_PairingPending");
        if (raw.Contains("unauthorized"))
            return LocalizationManager.GetString("Status_RsaPending");
        if (raw.StartsWith("ERROR:") || raw.StartsWith("Fout:") || raw.StartsWith("Error:"))
        {
            if (raw.Contains("timed out")) return LocalizationManager.GetString("Status_Timeout");
            return LocalizationManager.GetString("Status_CommsError");
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
            // The how-to holds a formatted heading and a list of keys rather
            // than text, so neither re-reads itself when the dictionary swaps.
            AdbTutorialViewModel?.RefreshLocalization();
            // The grade and invoice method are formatted from the dictionary rather
            // than stored, so the two summary rows have to be asked to re-read.
            this.RaisePropertyChanged(nameof(SelectedGradeDisplay));
            this.RaisePropertyChanged(nameof(SelectedInvoiceMethodDisplay));
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

    private bool _isDebugMode;
    public bool IsDebugMode
    {
        get => _isDebugMode;
        set 
        { 
            _settings.IsDebugMode = value; 
            _settings.EnableVerboseNetworkLogging = value;
            _settings.Save(); 
            TestRunnerServer.EnableVerboseNetworkLogging = value;
            ToolRunner.EnableVerboseNetworkLogging = value;
            this.RaiseAndSetIfChanged(ref _isDebugMode, value); 
        }
    }

    private bool _useSecureOrigin = true;

    /// <summary>
    /// Whether the phone is served over the cable, through the adb reverse
    /// tunnel Android keeps. Turning it off skips that route and the public
    /// https tunnel answers instead; the LAN address is only what is left when
    /// the tunnel is switched off as well.
    /// </summary>
    public bool UseSecureOrigin
    {
        get => _useSecureOrigin;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _useSecureOrigin, value)) return;
            _settings.UseSecureOrigin = value;
            _settings.Save();
            RefreshWebRunnerAddress();
        }
    }

    private bool _usePublicTunnel = true;

    /// <summary>
    /// Whether a public https address may be opened for a phone that has no other
    /// way to reach a secure origin. Android does not need it, an iPhone does.
    /// </summary>
    public bool UsePublicTunnel
    {
        get => _usePublicTunnel;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _usePublicTunnel, value)) return;
            _settings.UsePublicTunnel = value;
            _settings.Save();
            if (!value) _ = _quickTunnel.StopAsync();
            RefreshWebRunnerAddress();
        }
    }

    private bool _openEditorBeforePrint;
    public bool OpenEditorBeforePrint
    {
        get => _openEditorBeforePrint;
        set { _settings.OpenEditorBeforePrint = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _openEditorBeforePrint, value); }
    }

    /// <summary>
    /// The label template the export writes from, as the settings hold it. Empty
    /// means the template that came with the app, which is the right default and
    /// the reason a fresh install can print a label with nothing set up.
    /// </summary>
    public string LabelTemplatePath
    {
        get => _settings.TemplatePath ?? "";
        set
        {
            string chosen = value ?? "";
            if (_settings.TemplatePath == chosen) return;
            _settings.TemplatePath = chosen.Length > 0 ? chosen : null;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _labelTemplatePath, chosen);
        }
    }

    private string _labelTemplatePath = "";

    /// <summary>
    /// Whether the label that was written can be printed at all. False means no
    /// printer is known, which is worth saying on the summary screen rather than
    /// finding out at the till.
    /// </summary>
    public bool HasPrinter => PrintService.ListQueues().Count > 0;

    /// <summary>
    /// Whether the export panel is open. The panel is a full overlay rather than a
    /// window, because the summary screen underneath it is where the operator came
    /// from and where they go back to, and a separate window would put two
    /// windows on top of each other with no visual order between them.
    /// </summary>
    public bool IsExportOpen
    {
        get => ExportViewModel?.IsOpen ?? false;
        set
        {
            if (ExportViewModel is null) return;
            if (value) ExportViewModel.Open();
            else ExportViewModel.Close();
        }
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

    // IMEI.info BYOK Settings
    private string _imeiInfoApiKey = "";
    public string ImeiInfoApiKey
    {
        get => _imeiInfoApiKey;
        set { _settings.ImeiInfoApiKey = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _imeiInfoApiKey, value); }
    }

    private List<string> _selectedImeiChecks = new();
    public List<string> SelectedImeiChecks
    {
        get => _selectedImeiChecks;
        set { _settings.SelectedImeiChecks = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _selectedImeiChecks, value); }
    }

    private int _estimatedAppleDevices = 10;
    public int EstimatedAppleDevices
    {
        get => _estimatedAppleDevices;
        set { _settings.EstimatedAppleDevices = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _estimatedAppleDevices, value); UpdateCostCalculator(); }
    }

    private int _estimatedAndroidDevices = 10;
    public int EstimatedAndroidDevices
    {
        get => _estimatedAndroidDevices;
        set { _settings.EstimatedAndroidDevices = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _estimatedAndroidDevices, value); UpdateCostCalculator(); }
    }

    // IMEI.info individual check checkboxes (bound to SelectedImeiChecks list)
    public bool ImeiCheckAppleCarrierLockFmi
    {
        get => SelectedImeiChecks.Contains("apple_carrier_lock_fmi");
        set { UpdateCheckSelection("apple_carrier_lock_fmi", value); }
    }

    public bool ImeiCheckBlacklistSimple
    {
        get => SelectedImeiChecks.Contains("blacklist_simple");
        set { UpdateCheckSelection("blacklist_simple", value); }
    }

    public bool ImeiCheckBlacklistPremium
    {
        get => SelectedImeiChecks.Contains("blacklist_premium");
        set { UpdateCheckSelection("blacklist_premium", value); }
    }

    public bool ImeiCheckSamsungInfoKnox
    {
        get => SelectedImeiChecks.Contains("samsung_info_knox");
        set { UpdateCheckSelection("samsung_info_knox", value); }
    }

    private void UpdateCheckSelection(string serviceCode, bool isChecked)
    {
        var list = new List<string>(SelectedImeiChecks);
        if (isChecked && !list.Contains(serviceCode))
            list.Add(serviceCode);
        else if (!isChecked)
            list.Remove(serviceCode);
        SelectedImeiChecks = list;
        UpdateCostCalculator();
    }

    // API Key test status
    private string _imeiInfoApiKeyStatus = "";
    public string ImeiInfoApiKeyStatus
    {
        get => _imeiInfoApiKeyStatus;
        set => this.RaiseAndSetIfChanged(ref _imeiInfoApiKeyStatus, value);
    }

    private string _imeiInfoApiKeyStatusColor = "Transparent";
    public string ImeiInfoApiKeyStatusColor
    {
        get => _imeiInfoApiKeyStatusColor;
        set => this.RaiseAndSetIfChanged(ref _imeiInfoApiKeyStatusColor, value);
    }

    // Cost Calculator computed properties
    private string _estimatedMonthlyCostDisplay = "";
    public string EstimatedMonthlyCostDisplay
    {
        get => _estimatedMonthlyCostDisplay;
        set => this.RaiseAndSetIfChanged(ref _estimatedMonthlyCostDisplay, value);
    }

    private string _estimatedPerDeviceDisplay = "";
    public string EstimatedPerDeviceDisplay
    {
        get => _estimatedPerDeviceDisplay;
        set => this.RaiseAndSetIfChanged(ref _estimatedPerDeviceDisplay, value);
    }

    private void UpdateCostCalculator()
    {
        var checks = new List<PhoneGrade.Core.SecurityServices.ImeiInfoApiService.ImeiCheckType>();
        if (ImeiCheckAppleCarrierLockFmi) checks.Add(PhoneGrade.Core.SecurityServices.ImeiInfoApiService.ImeiCheckType.AppleCarrierLockFmi);
        if (ImeiCheckBlacklistSimple) checks.Add(PhoneGrade.Core.SecurityServices.ImeiInfoApiService.ImeiCheckType.BlacklistSimple);
        if (ImeiCheckBlacklistPremium) checks.Add(PhoneGrade.Core.SecurityServices.ImeiInfoApiService.ImeiCheckType.BlacklistPremium);
        if (ImeiCheckSamsungInfoKnox) checks.Add(PhoneGrade.Core.SecurityServices.ImeiInfoApiService.ImeiCheckType.SamsungInfoKnox);

        decimal monthlyCost = PhoneGrade.Core.SecurityServices.ImeiInfoApiService.CalculateEstimatedCost(EstimatedAppleDevices, EstimatedAndroidDevices, checks);
        decimal perDeviceCost = (EstimatedAppleDevices + EstimatedAndroidDevices) > 0 ? monthlyCost / (EstimatedAppleDevices + EstimatedAndroidDevices) : 0;

        EstimatedMonthlyCostDisplay = string.Format(LocalizationManager.GetString("Settings_ImeiInfoEstimatedMonthlyCost"), monthlyCost.ToString("F2"));
        EstimatedPerDeviceDisplay = string.Format(LocalizationManager.GetString("Settings_ImeiInfoEstimatedPerDevice"), perDeviceCost.ToString("F4"));
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

    // ============ What the label says ============
    //
    // Six settings that decide what is printed on a label, kept together because
    // they are read together: the paper and the barcodes take up the space the
    // words go in, so changing one changes what fits of the others.

    private string _labelStockPartNumber = LabelStock.Address.PartNumber;

    /// <summary>
    /// The DYMO part number of the roll in the printer.
    ///
    /// Read as a part number rather than as millimetres because that is how the roll
    /// is ordered and how it is recognised: 89x28 and 89x36 are both "address", and
    /// an operator holding the wrong one has no way to tell from the name.
    /// </summary>
    public string LabelStockPartNumber
    {
        get => _labelStockPartNumber;
        set
        {
            string wanted = LabelStock.FromPartNumber(value).PartNumber;
            _labelStockPartNumber = wanted;
            _settings.LabelStockPartNumber = wanted;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _labelStockPartNumber, wanted);
            ExportViewModel?.ReloadLabelSettings();
        }
    }

    /// <summary>What the barcode carries: the identifier, the pair, or one of each.</summary>
    public LabelBarcodeMode LabelBarcodeMode
    {
        get => _settings.LabelBarcodeMode;
        set
        {
            _settings.LabelBarcodeMode = value;
            _settings.Save();
            this.RaisePropertyChanged();
            ExportViewModel?.ReloadLabelSettings();
        }
    }

    /// <summary>Called by the export panel's pickers, which own the collections.</summary>
    public void SetLabelStock(LabelStock stock)
    {
        if (string.Equals(_labelStockPartNumber, stock.PartNumber, StringComparison.Ordinal)) return;
        _labelStockPartNumber = stock.PartNumber;
        _settings.LabelStockPartNumber = stock.PartNumber;
        _settings.Save();
        this.RaiseAndSetIfChanged(ref _labelStockPartNumber, stock.PartNumber);
        this.RaisePropertyChanged(nameof(LabelStock));
    }

    /// <summary>Called by the export panel's picker.</summary>
    public void SetLabelBarcodeMode(LabelBarcodeMode mode)
    {
        if (_settings.LabelBarcodeMode == mode) return;
        _settings.LabelBarcodeMode = mode;
        _settings.Save();
        this.RaisePropertyChanged(nameof(LabelBarcodeMode));
        ExportViewModel?.ReloadLabelSettings();
    }

    /// <summary>The stock, as the label writers take it.</summary>
    public LabelStock LabelStock => LabelStock.FromPartNumber(_labelStockPartNumber);

    /// <summary>
    /// The switches that decide what the label says, as the settings page shows them.
    /// </summary>
    /// <remarks>
    /// Gathered into one object so the label's wording is passed around as a single
    /// value. Every renderer takes it, which is the only way the preview can promise
    /// it is showing what the file will contain.
    /// </remarks>
    public LabelContent LabelContent => new(
        _settings.LabelShowBatteryCycles,
        _settings.LabelShowFaults,
        _settings.LabelShowLocks);

    /// <summary>Whether the charge count is on the label.</summary>
    public bool LabelShowBatteryCycles
    {
        get => _settings.LabelShowBatteryCycles;
        set { _settings.LabelShowBatteryCycles = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _labelShowBatteryCycles, value); LabelSettingsChanged(); }
    }

    /// <summary>Whether the faults are on the label.</summary>
    public bool LabelShowFaults
    {
        get => _settings.LabelShowFaults;
        set { _settings.LabelShowFaults = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _labelShowFaults, value); LabelSettingsChanged(); }
    }

    /// <summary>
    /// Whether the locks are on the label. A shop that turns this off should know
    /// that it removes the one line that stops a FRP locked phone being sold.
    /// </summary>
    public bool LabelShowLocks
    {
        get => _settings.LabelShowLocks;
        set { _settings.LabelShowLocks = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _labelShowLocks, value); LabelSettingsChanged(); }
    }

    private bool _labelShowBatteryCycles = true;
    private bool _labelShowFaults = true;
    private bool _labelShowLocks = true;

    /// <summary>Tells the preview and the writers that the label's wording changed.</summary>
    private void LabelSettingsChanged() => ExportViewModel?.ReloadLabelSettings();

    private bool _enableUsbEventMonitoring;
    public bool EnableUsbEventMonitoring
    {
        get => _enableUsbEventMonitoring;
        set 
        { 
            _settings.EnableUsbEventMonitoring = value; 
            _settings.Save(); 
            this.RaiseAndSetIfChanged(ref _enableUsbEventMonitoring, value); 
            if (value) _adbDirector?.Start(); else _adbDirector?.Stop();
        }
    }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        set => this.RaiseAndSetIfChanged(ref _busy, value);
    }

    /// <summary>True when a valid Pro license is active.</summary>
    public bool IsProLicenseActive => _trialGate.IsPro;

    /// <summary>Number of scans consumed on the free tier.</summary>
    public int TrialScanCount => _trialGate.ScanCount;

    /// <summary>True when the free tier limit is reached and no Pro license is active.</summary>
    public bool IsTrialLimitReached => !IsProLicenseActive && TrialScanCount >= TrialGate.FreeScanLimit;

    /// <summary>Human-readable licensing status, kept in one place with the pill and the introduction screen.</summary>
    public string LicensingStatusText => _licensingViewModel?.LicensingStatusText ?? "";

    /// <summary>Shared licensing view model: drives the introduction screen, the status pill and the settings row.</summary>
    public LicensingViewModel? Licensing => _licensingViewModel;

    private bool _isIntroVisible;
    /// <summary>True until the operator dismisses the first-run introduction screen.</summary>
    public bool IsIntroVisible
    {
        get => _isIntroVisible;
        private set => this.RaiseAndSetIfChanged(ref _isIntroVisible, value);
    }

    /// <summary>
    /// The pages the introduction screen is made of, in the order they are read.
    ///
    /// It used to be one long card holding the plan, the Pro price, the IMEI key
    /// offer, three buttons and a license box, which put the whole of it on one
    /// screen at a font too small to read comfortably. It is now four pages, and the
    /// language and the look are asked for on the first one rather than left for
    /// the operator to find afterwards.
    /// </summary>
    public IReadOnlyList<string> IntroPages { get; } =
        ["Language", "Plan", "Workflow", "Imei"];

    private string _introPage = "Language";

    /// <summary>The page of the introduction currently on screen.</summary>
    public string IntroPage
    {
        get => _introPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _introPage, value);
            this.RaisePropertyChanged(nameof(IsFirstIntroPage));
            this.RaisePropertyChanged(nameof(IsLastIntroPage));
        }
    }

    /// <summary>True on the first page, where the back button has nowhere to go.</summary>
    public bool IsFirstIntroPage => IntroPage == IntroPages[0];

    /// <summary>True on the last page, where the button finishes instead of advancing.</summary>
    public bool IsLastIntroPage => IntroPage == IntroPages[^1];

    /// <summary>The page after this one, wrapping to the first on the last.</summary>
    public string NextIntroPage =>
        IsLastIntroPage ? IntroPages[0] : IntroPages[IntroPages.IndexOf(IntroPage) + 1];

    /// <summary>The page before this one, stopping at the first.</summary>
    public string PreviousIntroPage =>
        IsFirstIntroPage ? IntroPages[0] : IntroPages[IntroPages.IndexOf(IntroPage) - 1];

    /// <summary>Goes to the next page, or closes the screen when it is the last one.</summary>
    public ReactiveCommand<Unit, Unit> IntroNextCommand { get; }

    /// <summary>Goes back a page. Does nothing on the first one.</summary>
    public ReactiveCommand<Unit, Unit> IntroBackCommand { get; }

    /// <summary>Jumps straight to a named page.</summary>
    public ReactiveCommand<string, Unit> IntroGoToPageCommand { get; }

    private bool _isIntroActivationVisible;
    /// <summary>True once "I already have a license" was pressed on the introduction screen.</summary>
    public bool IsIntroActivationVisible
    {
        get => _isIntroActivationVisible;
        set => this.RaiseAndSetIfChanged(ref _isIntroActivationVisible, value);
    }

    /// <summary>Closes the introduction screen for good.</summary>
    public ReactiveCommand<Unit, Unit> DismissIntroCommand { get; }

    /// <summary>Reveals the license key box on the introduction screen.</summary>
    public ReactiveCommand<Unit, Unit> ShowIntroActivationCommand { get; }

    private bool _isLicensePanelOpen;
    /// <summary>True while the license panel under the title bar status pill is open.</summary>
    public bool IsLicensePanelOpen
    {
        get => _isLicensePanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isLicensePanelOpen, value);
    }

    /// <summary>Shows or hides the license panel. The pill in the title bar is bound to it.</summary>
    public ReactiveCommand<Unit, Unit> ToggleLicensePanelCommand { get; }

    /// <summary>Opens the license panel and closes the settings drawer, so the two never overlap.</summary>
    public ReactiveCommand<Unit, Unit> ManageLicenseCommand { get; }

    private void DismissIntro()
    {
        _settings.IntroSeen = true;
        _settings.Save();
        IsIntroVisible = false;
        IsIntroActivationVisible = false;
    }

    private DeviceData _deviceData = new();
    public DeviceData DeviceData
    {
        get => _deviceData;
        set
        {
            this.RaiseAndSetIfChanged(ref _deviceData, value);
            this.RaisePropertyChanged(nameof(HasDevice));
            this.RaisePropertyChanged(nameof(SelectedGrade));
            this.RaisePropertyChanged(nameof(SelectedGradeDisplay));
            this.RaisePropertyChanged(nameof(SelectedInvoiceMethod));
            this.RaisePropertyChanged(nameof(SelectedInvoiceMethodDisplay));
        }
    }

    public string SelectedGrade
    {
        get => DeviceData.Quality;
        set
        {
            DeviceData.Quality = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(SelectedGradeDisplay));
            if (!string.IsNullOrWhiteSpace(SelectedDevice.Key))
            {
                DeviceSessionManager.UpdateSessionData(SelectedDevice.Key, DeviceData);
            }
        }
    }

    public string SelectedGradeDisplay => Services.GradeWording.Grade(DeviceData.Quality);

    public string SelectedInvoiceMethod
    {
        get => DeviceData.PayMethod;
        set
        {
            DeviceData.PayMethod = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(SelectedInvoiceMethodDisplay));
            if (!string.IsNullOrWhiteSpace(SelectedDevice.Key))
            {
                DeviceSessionManager.UpdateSessionData(SelectedDevice.Key, DeviceData);
            }
        }
    }

    public string SelectedInvoiceMethodDisplay => Services.GradeWording.InvoiceMethod(DeviceData.PayMethod);


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

    // Which topic of the settings page is on screen: one of General, Workflow,
    // Connection, License, Support, Advanced. A single string rather than a
    // boolean per topic, so a topic is one line in the view and nowhere else.
    private string _selectedSettingsSection = "General";
    public string SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        set => this.RaiseAndSetIfChanged(ref _selectedSettingsSection, value);
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
    public ReactiveCommand<string, Unit> SelectSettingsSectionCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLogsModalCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseLogsModalCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenTroubleshootModalCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseTroubleshootModalCommand { get; }
    public ReactiveCommand<Unit, Unit> BackToIdleCommand { get; }

    /// <summary>
    /// What changed in the version that was just installed, and nothing else.
    ///
    /// An update replaces the binaries and restarts, so there is no window open
    /// at the moment the new version lands to say what it is. The updater writes
    /// the notes to disk before applying the update and this takes them on the
    /// next launch, which is why the panel can be closed and dismissed instead of
    /// asked about: by the time it appears the update has already happened.
    /// </summary>
    private bool _isChangelogVisible;
    public bool IsChangelogVisible { get => _isChangelogVisible; set => this.RaiseAndSetIfChanged(ref _isChangelogVisible, value); }

    private string _changelogVersion = "";
    public string ChangelogVersion { get => _changelogVersion; set => this.RaiseAndSetIfChanged(ref _changelogVersion, value); }

    private string _changelogNotes = "";
    public string ChangelogNotes { get => _changelogNotes; set => this.RaiseAndSetIfChanged(ref _changelogNotes, value); }

    public ReactiveCommand<Unit, Unit> CloseChangelogCommand { get; }

    /// <summary>
    /// Opens the changelog panel if an update left notes behind.
    ///
    /// Reading them also clears them, so this runs once per update rather than on
    /// every launch: an operator who closed the panel and comes back tomorrow does
    /// not find yesterday's changes waiting again. A release packaged with no
    /// notes writes no file at all, which is the case that leaves the panel shut.
    /// </summary>
    private void ShowPendingChangelog()
    {
        var changelog = ReleaseChangelog.Take();
        if (changelog is null) return;

        ChangelogVersion = changelog.Version;
        ChangelogNotes = changelog.Notes;
        IsChangelogVisible = true;
    }

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
    public ReactiveCommand<InteractiveTestResult, Unit> DismissInteractiveTestCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreDismissedTestsCommand { get; }

    private bool _showAdbWarning;
    public bool ShowAdbWarning { get => _showAdbWarning; set => this.RaiseAndSetIfChanged(ref _showAdbWarning, value); }

    /// <summary>
    /// Set when the operator closed the how-to without following it. The next
    /// Android device to arrive on the cable clears it, so one dismissed phone
    /// does not silence the guide for every phone after it.
    /// </summary>
    private bool _adbGuideDismissed;

    /// <summary>Result of the most recent device list probe, kept so the manual
    /// refresh can build its status text without spawning the tools again.</summary>
    private DeviceService.ConnectionState _lastDiagState = DeviceService.ConnectionState.NotFound;

    public ReactiveCommand<Unit, Unit> RetryAdbDetectionCommand { get; }
    public ReactiveCommand<Unit, Unit> DismissAdbGuideCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> FinishInspectionCommand { get; }

    /// <summary>Opens the export panel. One screen for the label, the report and the numbers.</summary>
    public ReactiveCommand<Unit, Unit> OpenExportCommand { get; }

    // IMEI.info BYOK Commands
    public ReactiveCommand<Unit, Unit> SaveImeiInfoApiKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> TestImeiInfoApiKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenImeiDashboardCommand { get; }
    public ReactiveCommand<Unit, Unit> NavigateToImeiSettingsCommand { get; }

    public event Action<DeviceData>? DataEditorRequested;

    public MainWindowViewModel(LemonSqueezyClient? licenseClient = null)
    {
        // The parameter only exists so tests can hand over a client with a fake
        // transport; production passes nothing and gets the real endpoint.
        _licenseClient = licenseClient ?? new LemonSqueezyClient();
        _settings = AppSettings.Load();
        _theme = _settings.Theme;
        _language = _settings.Language == "en" ? "English" : "Nederlands";
        _autoActivate = _settings.AutoActivate;
        _autoDetectOnPlug = _settings.AutoDetectOnPlug;
        _runDiagnostics = _settings.RunDiagnostics;
        _enable85PercentChecker = _settings.Enable85PercentChecker;
        _isDebugMode = _settings.IsDebugMode;
        _useSecureOrigin = _settings.UseSecureOrigin;
        _usePublicTunnel = _settings.UsePublicTunnel;
        _openEditorBeforePrint = _settings.OpenEditorBeforePrint;
        _autoStartWebTest = _settings.AutoStartWebTest;
        _showSummaryScreenAfterTesting = _settings.ShowSummaryScreenAfterTesting;
        _requirePwaTest = _settings.RequirePwaTest;
        _autoFinishAfterTest = _settings.AutoFinishAfterTest;
        _imeiInfoApiKey = _settings.ImeiInfoApiKey ?? "";
        _selectedImeiChecks = _settings.SelectedImeiChecks ?? new List<string>();
        _estimatedAppleDevices = _settings.EstimatedAppleDevices > 0 ? _settings.EstimatedAppleDevices : 10;
        _estimatedAndroidDevices = _settings.EstimatedAndroidDevices > 0 ? _settings.EstimatedAndroidDevices : 10;
        _defaultQuality = _settings.DefaultQuality;
        _defaultPaymentMethod = _settings.DefaultPaymentMethod;
        _enableUsbEventMonitoring = _settings.EnableUsbEventMonitoring;
        _labelTemplatePath = _settings.TemplatePath ?? "";
        
        // Wire verbose logging flag from settings
        ToolRunner.EnableVerboseNetworkLogging = _settings.EnableVerboseNetworkLogging;
        TestRunnerServer.EnableVerboseNetworkLogging = _settings.EnableVerboseNetworkLogging;

        // Initialize cost calculator
        UpdateCostCalculator();

        _originResolver = new WebRunnerOriginResolver(
            (sessionUdid, port) => _adbTunnel.OpenAsync(sessionUdid, port),
            (port, onStatus) => _quickTunnel.StartAsync(port, onStatus));

        // A connector that dies takes the address behind an already printed QR code
        // with it, so the session is worked out again instead of leaving it pointing
        // at nothing. The tunnel can hold the live address with the cable switch on
        // (as the fallback) or off (as the route), so the tunnel switch alone decides
        // whether a resolve is due.
        _quickTunnel.AddressLost += () => Dispatcher.UIThread.Post(() =>
        {
            if (UsePublicTunnel) RefreshWebRunnerAddress();
        });

        // Licensing gate wired to the live settings instance so every Save() carries the token.
        _trialGate = new TrialGate(
            new TrialStateStore(
                () => _settings.TrialToken,
                token => { _settings.TrialToken = token; _settings.Save(); },
                TrialStateStore.DefaultBackupFilePath),
            _licenseClient);

        // Point DeviceService at this gate so every scan path is gated.
        DeviceService.ScanGate = () => _trialGate.EvaluateAsync();

        // Settings view model for the licensing card.
        _licensingViewModel = new LicensingViewModel(_trialGate, _licenseClient, RequestLicenseRefresh);

        // USB event monitoring and the how-to it can ask for. The director owns
        // the USB stream, the tutorial owns the wording, and this view model
        // owns the one flag that decides whether the how-to is on screen - so
        // a phone arriving over USB and the adb probe that finds the same phone
        // both end at the same place.
        _adbDirector = new AdbDeviceDirector();
        AdbTutorialViewModel = new AdbTutorialViewModel(_adbDirector);

        AdbTutorialViewModel.Requested += (_, _) =>
            Dispatcher.UIThread.Post(() => { _adbGuideDismissed = false; ShowAdbWarning = true; });
        AdbTutorialViewModel.Authorized += (_, _) =>
            Dispatcher.UIThread.Post(() => ShowAdbWarning = false);
        _adbDirector.AdbCleared += (_, _) =>
            Dispatcher.UIThread.Post(() => ShowAdbWarning = false);

        _adbDirector.Start();

        var canStart = this.WhenAnyValue(x => x.Busy, x => x.IsTrialLimitReached)
            .Select(t => !t.Item1 && !t.Item2);
        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(RefreshDeviceListAsync);
        StartCommand = ReactiveCommand.CreateFromTask(() => RunFlowAsync(), canStart);
        RetestCommand = ReactiveCommand.CreateFromTask(RetestCurrentDeviceAsync, canStart);
        RetryAdbDetectionCommand = ReactiveCommand.CreateFromTask(async () => {
            // Checking again is a fresh look at the cable, so a phone whose
            // guide was dismissed is worth asking about once more. The guide is
            // deliberately not closed first: it may only go away on an answer
            // that says the phone is fine, and closing it here is what used to
            // leave an operator off the how-to the moment the probe came back
            // with nothing to say about the phone at all.
            _adbGuideDismissed = false;
            await RefreshDeviceListAsync();
        });
        DismissAdbGuideCommand = ReactiveCommand.Create(() =>
        {
            _adbGuideDismissed = true;
            ShowAdbWarning = false;
        });
        SetQualityCommand = ReactiveCommand.Create<string>(q => _ = ContinueAfterQualityAsync(q));
        SetPaymentMethodCommand = ReactiveCommand.Create<string>(p => ContinueAfterPaymentAsync(p));
        DismissInteractiveTestCommand = ReactiveCommand.Create<InteractiveTestResult>(DismissInteractiveTest);
        RestoreDismissedTestsCommand = ReactiveCommand.Create(RestoreDismissedTests);
        OpenEditorCommand = ReactiveCommand.Create(() => DataEditorRequested?.Invoke(DeviceData));
        FinishInspectionCommand = ReactiveCommand.CreateFromTask(FinishInspectionAsync);

        ExportViewModel = new ExportViewModel(this);
        _labelTemplatePath = _settings.TemplatePath ?? "";
        OpenExportCommand = ReactiveCommand.Create(ExportViewModel.Open);

        // The panel's own flag is what the overlay binds to, and the panel raises it
        // on itself. Passing it through a property here without forwarding the change
        // meant the button set the flag and nothing on screen moved, which is why the
        // overlay was only ever visible in a screenshot taken before the window
        // existed.
        ExportViewModel.WhenAnyValue(panel => panel.IsOpen)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsExportOpen)));

        // IMEI.info BYOK Commands
        SaveImeiInfoApiKeyCommand = ReactiveCommand.CreateFromTask(SaveImeiInfoApiKeyAsync);
        TestImeiInfoApiKeyCommand = ReactiveCommand.CreateFromTask(TestImeiInfoApiKeyAsync);
        OpenImeiDashboardCommand = ReactiveCommand.Create(() => PhoneGrade.UI.Services.PricingLink.Open("https://dash.imei.info/"));

        // Introduction screen: shown until dismissed, and never again after that.
        IsIntroVisible = !_settings.IntroSeen;
        DismissIntroCommand = ReactiveCommand.Create(DismissIntro);
        // The last page finishes; the ones before it turn the page. One button for
        // both, because a Next that turns into a Finish halfway through is what
        // the operator expects and two buttons at the foot of the card is not.
        IntroNextCommand = ReactiveCommand.Create(() =>
        {
            if (IsLastIntroPage) DismissIntro();
            else IntroPage = NextIntroPage;
        });
        IntroBackCommand = ReactiveCommand.Create(() =>
        {
            if (!IsFirstIntroPage) IntroPage = PreviousIntroPage;
        });
        IntroGoToPageCommand = ReactiveCommand.Create<string>(page =>
        {
            if (IntroPages.Contains(page)) IntroPage = page;
        });
        ShowIntroActivationCommand = ReactiveCommand.Create(() => { IsIntroActivationVisible = true; });
        NavigateToImeiSettingsCommand = ReactiveCommand.Create(() =>
        {
            IsIntroVisible = false;
            IsSettingsDrawerOpen = true;
            SelectedSettingsSection = "ImeiApi";
        });

        // License panel: opened from the status pill, or from settings.
        ToggleLicensePanelCommand = ReactiveCommand.Create(() => { IsLicensePanelOpen = !IsLicensePanelOpen; });
        ManageLicenseCommand = ReactiveCommand.Create(() =>
        {
            IsLicensePanelOpen = true;
            IsSettingsDrawerOpen = false;
        });

        ToggleSettingsCommand = ReactiveCommand.Create(() => { IsSettingsDrawerOpen = !IsSettingsDrawerOpen; });
        SelectSettingsSectionCommand = ReactiveCommand.Create<string>(section => SelectedSettingsSection = section);
        OpenLogsModalCommand = ReactiveCommand.Create(() => { IsLogsModalOpen = true; IsSettingsDrawerOpen = false; });
        CloseLogsModalCommand = ReactiveCommand.Create(() => { IsLogsModalOpen = false; });
        OpenTroubleshootModalCommand = ReactiveCommand.Create(() => { IsTroubleshootModalOpen = true; IsSettingsDrawerOpen = false; });
        CloseTroubleshootModalCommand = ReactiveCommand.Create(() => { IsTroubleshootModalOpen = false; });
        BackToIdleCommand = ReactiveCommand.Create(() => { WorkflowState = AppWorkflowState.Idle; });
        CloseChangelogCommand = ReactiveCommand.Create(() => { IsChangelogVisible = false; });

        ShowPendingChangelog();


        UsbEventWatcher.UsbDeviceConnected += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AutoDetectOnPlug && !Busy)
                {
                    _ = RefreshDeviceListSilentAsync();
                }
            });
        };

        UsbEventWatcher.UsbDeviceDisconnected += (s, e) =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                // WMI reports every USB removal - a mouse, a headset, a phone
                // charger - not just the device under test, so the reset is
                // decided by the device count the same way the polling loop
                // decides it. Resetting on the raw event would tear down an
                // inspection because someone unplugged a peripheral.
                int count = await RefreshDeviceListSilentAsync();
                _presence.Report(count);
                if (count == 0 && _presence.IsUnplugged && WorkflowState != AppWorkflowState.Idle)
                {
                    ResetToIdle();
                }
            });
        };
        UsbEventWatcher.StartMonitoring();

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
                    InteractiveSessionStatus = string.Format(
                        LocalizationManager.GetString("Session_Connected"), e.SessionId);
                });
            };

            _webServer.MessageReceived += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (e.Message?.Type == "test_progress")
                    {
                        InteractiveSessionStatus = string.Format(
                            LocalizationManager.GetString("Session_TestRunning"),
                            e.Message.TestName, e.Message.Progress);
                    }
                    else if (e.Message?.Type == "test_complete")
                    {
                        InteractiveSessionStatus = string.Format(
                            LocalizationManager.GetString("Session_TestDone"),
                            e.Message.TestName, e.Message.Status);
                    }
                });
            };

            _webServer.ProgressChanged += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(SelectedDevice.Key) &&
                        !e.SessionId.Equals(SelectedDevice.Key, StringComparison.OrdinalIgnoreCase)) return;

                    ApplyPwaProgress(e.Snapshot);
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

            _webServer.MissingApiReported += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // The server records the same check on this DeviceData when
                    // it finds the session, so guard on absence rather than
                    // always appending: appending blind would list the gap twice,
                    // while guarding blind would drop it when no session matched.
                    string checkName = $"{GradePolicy.MissingApiPrefix} {e.MissingApi}";
                    if (!DeviceData.ComponentChecks.Any(c => c.Name == checkName))
                    {
                        DeviceData.ComponentChecks.Add(new ComponentStatus
                        {
                            Name = checkName,
                            Status = ComponentStatusType.Failed,
                            Details = string.Format(
                                LocalizationManager.GetString("Api_MissingDetails"), e.MissingApi)
                        });
                    }

                    DeviceData.InteractiveTests ??= new InteractiveTestSuiteResult 
                    { 
                        SessionId = e.SessionId, 
                        DeviceUdid = "Unknown", 
                        Tests = new List<InteractiveTestResult>() 
                    };

                    string testId = $"api_check_{e.MissingApi}";
                    if (DeviceData.InteractiveTests.Tests.Any(t => t.Id == testId))
                    {
                        return;
                    }

                    DeviceData.InteractiveTests.Tests.Add(new InteractiveTestResult
                    {
                        Id = testId,
                        Name = $"Browser API: {e.MissingApi}",
                        Status = TestStatus.Failed,
                        Notes = LocalizationManager.GetString("Api_MissingNotes"),
                        DurationMs = 0
                    });
                });
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
            InteractiveSessionStatus = string.Format(
                LocalizationManager.GetString("Session_WebRunnerError"), ex.Message);
        }
    }

    /// <summary>
    /// Binds a session to the phone and publishes where to open it.
    ///
    /// Where to open it cannot be answered synchronously: it depends on whether adb
    /// will build a tunnel, and building one is two adb calls with their own
    /// timeouts. Answering it by waiting here hung the whole application. This runs
    /// on the UI thread, because it is called from the SelectedDevice setter, and
    /// the awaited work posts its continuation back to the thread that is waiting
    /// for it. That thread can then never run it, so the wait never ended and the
    /// window stopped responding the moment an Android device was picked. The
    /// session now goes up straight away and the URL follows the tunnel's answer.
    /// </summary>
    public void UpdateWebRunnerSession(string? udid)
    {
        try
        {
            int port = _webServer?.BoundPort > 0 ? _webServer.BoundPort : 5055;
            var sessionUdid = !string.IsNullOrWhiteSpace(udid) ? udid : (DeviceData.Identifier != "NOID" ? DeviceData.Identifier : "DEMO");

            // Each newer request supersedes whatever is in flight, so a slow adb
            // answer cannot replace the URL of a device that has since been swapped.
            int generation = Interlocked.Increment(ref _webRunnerGeneration);

            _webServer?.SetActiveSession(sessionUdid);

            _ = ResolveWebRunnerAddressAsync(sessionUdid, port, generation);
        }
        catch (Exception ex)
        {
            InteractiveSessionStatus = string.Format(
                LocalizationManager.GetString("Session_QrError"), ex.Message);
        }
    }

    /// <summary>
    /// Works out where the phone should open the test page and publishes it.
    ///
    /// Nothing here waits for an answer on the thread it was called from. This runs
    /// on the UI thread, because it is called from the SelectedDevice setter, and
    /// waiting there for adb or the tunnel would hand the awaited continuation back
    /// to the very thread that is blocked, which never finishes. That is what froze
    /// the window the moment an Android device was picked. The session goes up
    /// straight away, the address follows whenever each route answers, and the
    /// publish is pushed onto the UI thread explicitly.
    /// </summary>
    private async Task ResolveWebRunnerAddressAsync(string sessionUdid, int port, int generation)
    {
        WebRunnerOrigin origin;

        try
        {
            origin = await _originResolver.ResolveAsync(
                sessionUdid,
                port,
                QrCodeService.NetworkAddress(port),
                QrCodeService.LoopbackAddress(port),
                UseSecureOrigin,
                UsePublicTunnel,
                status => ReportWebRunnerStatus(status, generation));
        }
        catch (Exception ex)
        {
            // Last resort: the chain catches its own failures, so getting here means
            // something broke outside of both routes. A QR code that silently cannot
            // run the camera and motion steps is worse than one that says so.
            SystemEventLogger.Warning(LogSource.UsbDetector, $"No route for the phone: {ex.Message}");
            origin = new WebRunnerOrigin(QrCodeService.NetworkAddress(port), false, null);
        }

        if (!IsCurrentWebRunnerSession(generation)) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
            PublishWebRunner(sessionUdid, origin, generation));
    }

    /// <summary>Shows what the routes are working on, while they are.</summary>
    private void ReportWebRunnerStatus(ConnectionNotice status, int generation)
    {
        if (!IsCurrentWebRunnerSession(generation)) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (IsCurrentWebRunnerSession(generation))
                InteractiveSessionStatus = ConnectionWording.Say(status);
        });
    }

    /// <summary>True while the given request is still the newest one.</summary>
    private bool IsCurrentWebRunnerSession(int generation) =>
        generation == Volatile.Read(ref _webRunnerGeneration);

    private void PublishWebRunner(string sessionUdid, WebRunnerOrigin origin, int generation)
    {
        if (!IsCurrentWebRunnerSession(generation)) return;

        WebRunnerUrl = QrCodeService.GenerateSessionUrl(origin.Address, sessionUdid, IsDebugMode);
        QrCodeBitmap = QrCodeService.GenerateQrCodeBitmap(WebRunnerUrl);

        // The warning replaces the polite opening line, but never the address: it is
        // already shown on its own in the window, so nothing is hidden by this.
        // Core hands over which routes failed and what a failing one said; the words
        // around those facts are picked here, from the dictionary in use.
        InteractiveSessionStatus = origin.Warning is null
            ? DescribeWebRunnerRoute(origin)
            : ConnectionWording.Warn(origin.Warning);

        // If AutoStartWebTest is enabled, hand the signal to the phone on its next
        // status poll. The address is published before the phone has opened the
        // page, so the command waits here until it asks.
        if (AutoStartWebTest && _webServer != null)
        {
            _webServer.QueueCommand(new { type = "auto_start_suite", sessionId = sessionUdid }, sessionUdid);
        }
    }

    /// <summary>Names the route the address went over, so the operator can tell.</summary>
    private string DescribeWebRunnerRoute(WebRunnerOrigin origin)
    {
        if (QrCodeService.IsLoopbackAddress(origin.Address))
            return string.Format(LocalizationManager.GetString("Route_Usb"), WebRunnerUrl);

        if (QrCodeService.IsSecureAddress(origin.Address))
            return string.Format(LocalizationManager.GetString("Route_Internet"), WebRunnerUrl);

        return string.Format(LocalizationManager.GetString("Route_Local"), WebRunnerUrl);
    }

    /// <summary>Rebuilds the QR code after the origin setting changed.</summary>
    private void RefreshWebRunnerAddress()
    {
        if (_webServer is null) return;

        string serial = SelectedDevice.Key;
        if (string.IsNullOrWhiteSpace(serial))
            serial = DeviceData.Identifier != "NOID" ? DeviceData.Identifier : "DEMO";

        UpdateWebRunnerSession(serial);
    }

    /// <summary>
    /// Closes the public address the tunnel opened. A connector left running would
    /// keep forwarding a URL to a server that is no longer there.
    /// </summary>
    public void Shutdown()
    {
        try
        {
            _quickTunnel.Dispose();
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector, $"Could not close the tunnel: {ex.Message}");
        }

        // Stop USB event monitoring
        try
        {
            AdbTutorialViewModel?.Dispose();
            _adbDirector?.Dispose();
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector, $"Could not stop USB monitoring: {ex.Message}");
        }

        // Let go of the static log event, which otherwise keeps the log view model alive.
        LogsViewModel.Dispose();
    }

    /// <summary>
    /// Re-raises everything that shows the licensing state. The gate mutates its
    /// counter during a scan, but the bindings only follow property changes, so
    /// without this the settings card and the banner keep showing the count the
    /// app started with.
    /// </summary>
    private void RefreshLicensingState()
    {
        this.RaisePropertyChanged(nameof(IsProLicenseActive));
        this.RaisePropertyChanged(nameof(TrialScanCount));
        this.RaisePropertyChanged(nameof(IsTrialLimitReached));
        this.RaisePropertyChanged(nameof(LicensingStatusText));
        _licensingViewModel?.Refresh();

        // A key that went valid on the introduction screen makes it pointless:
        // its whole job is explaining what the free tier is.
        if (_isIntroVisible && IsProLicenseActive) DismissIntro();
    }

    /// <summary>
    /// Called after an activation. The gate already answered while the key was
    /// being checked, so the screen is updated right here instead of after a
    /// round trip through the dispatcher: an operator who just pasted a key sees
    /// the tier change, and the introduction screen folds itself away, the
    /// moment the answer arrives.
    /// </summary>
    private void RequestLicenseRefresh() => RefreshLicensingState();

    /// <summary>
    /// <see cref="Shutdown"/> as a contract, so a caller that only has the interface
    /// can release the connector too. Closing the window already does it; a window
    /// that is built but never shown is never closed either, and there the connector
    /// would otherwise outlive the reason it was opened for.
    /// </summary>
    void IDisposable.Dispose() => Shutdown();

    /// <summary>
    /// Shows where the phone is, so the operator can walk away from it.
    ///
    /// The phone owns the run, but the operator is standing at the PC. Without
    /// this the desktop only said anything when the whole suite was done, which
    /// is no help to someone waiting on a phone on the other side of a counter.
    /// </summary>
    public void ApplyPwaProgress(PhoneGrade.UI.Web.PwaProgressSnapshot snapshot)
    {
        if (snapshot == null) return;

        int done = snapshot.CompletedCount;
        int total = snapshot.TotalTests > 0 ? snapshot.TotalTests : snapshot.Steps.Count;

        if (snapshot.Finished)
        {
            InteractiveSessionStatus = string.Format(
                LocalizationManager.GetString("Session_Finished"), done, total);
            return;
        }

        if (!snapshot.Started)
        {
            InteractiveSessionStatus = LocalizationManager.GetString("Session_Ready");
            return;
        }

        string where = string.IsNullOrWhiteSpace(snapshot.CurrentTestName)
            ? LocalizationManager.GetString("Session_NextStep")
            : snapshot.CurrentTestName;

        InteractiveSessionStatus = done == 0
            ? string.Format(LocalizationManager.GetString("Session_Started"), where)
            : string.Format(LocalizationManager.GetString("Session_InProgress"), where, done, total);
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
        int skipped = suite.Tests.Count(t => t.Status == TestStatus.Skipped);

        InteractiveTestSummary = skipped == 0
            ? string.Format(LocalizationManager.GetString("Session_TestsPlain"), passed, failed, suite.Platform)
            : string.Format(LocalizationManager.GetString("Session_TestsSkipped"), passed, failed, skipped, suite.Platform);

        if (failed > 0)
        {
            InteractiveSessionStatus = skipped > 0
                ? string.Format(LocalizationManager.GetString("Session_FailedSkipped"), failed, skipped)
                : string.Format(LocalizationManager.GetString("Session_Failed"), failed);
        }
        else if (skipped > 0)
        {
            InteractiveSessionStatus = string.Format(
                LocalizationManager.GetString("Session_SkippedOnly"), skipped);
        }
        else
        {
            InteractiveSessionStatus = LocalizationManager.GetString(
                suite.AllPassed ? "Session_AllPassed" : "Session_Done");
        }

        RefreshInteractiveTestLists();
    }

    /// <summary>
    /// Fills the two lists in the inspection report.
    ///
    /// They are refilled here and at the end of the flow, because the run can
    /// finish either way round: results that arrive after the report was built
    /// would otherwise sit unseen until the operator paid for nothing.
    /// </summary>
    private void RefreshInteractiveTestLists()
    {
        FailedInteractiveTests.Clear();
        SkippedInteractiveTests.Clear();
        DismissedInteractiveTests.Clear();

        foreach (var test in DeviceData.InteractiveTests?.Tests ?? Enumerable.Empty<InteractiveTestResult>())
        {
            // Counted, not dropped: the report says how much it is leaving out.
            if (test.Excluded)
            {
                DismissedInteractiveTests.Add(test);
            }
            else if (test.Status == TestStatus.Failed)
            {
                FailedInteractiveTests.Add(test);
            }
            else if (test.Status == TestStatus.Skipped)
            {
                SkippedInteractiveTests.Add(test);
            }
        }

        RaiseReportChanged();
    }

    /// <summary>
    /// Takes one row out of the inspection report.
    ///
    /// The decision is written onto the row, not kept in the list, because both
    /// lists are emptied and refilled every time the report refreshes: a
    /// dismissal held only by a list would be back on the next refresh. A new
    /// run rebuilds the rows from what the phone sent, so a fresh measurement
    /// shows itself again.
    /// </summary>
    private void DismissInteractiveTest(InteractiveTestResult? test)
    {
        if (test is null || test.Excluded) return;

        test.Excluded = true;
        FailedInteractiveTests.Remove(test);
        SkippedInteractiveTests.Remove(test);
        DismissedInteractiveTests.Add(test);

        RaiseReportChanged();
    }

    /// <summary>Puts every row back in the report, so a dismissal is never final.</summary>
    private void RestoreDismissedTests()
    {
        if (DismissedInteractiveTests.Count == 0) return;

        foreach (var test in DismissedInteractiveTests)
        {
            test.Excluded = false;
        }

        RefreshInteractiveTestLists();
    }

    /// <summary>
    /// Raises everything that moves when a row enters or leaves the report.
    ///
    /// The three are one piece of state: what is listed, what is hidden, and
    /// whether the green line is allowed to stand. Raising them together is
    /// what keeps a view from showing a figure for rows it no longer believes
    /// are hidden.
    /// </summary>
    private void RaiseReportChanged()
    {
        this.RaisePropertyChanged(nameof(NoInteractiveTestProblems));
        this.RaisePropertyChanged(nameof(HasDismissedInteractiveTests));
        this.RaisePropertyChanged(nameof(DismissedInteractiveTestCount));
    }

    /// <summary>Polls for device changes every 2s; starts the auto flow on first sight of a device.</summary>
    private void StartWatcher()
    {
        StopWatcher();
        // One refresh at a time. The interval does not care how long the last one took,
        // and SelectMany would happily start the next one on top of it.
        _watcher = SerialPoll.OneAtATime(
                Observable.Interval(TimeSpan.FromSeconds(2.5)) // Throttled to prevent lockdownd crashes
                    .ObserveOn(RxApp.TaskpoolScheduler),
                RefreshDeviceListSilentAsync)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(count =>
            {
                _presence.Report(count);

                // Hot Unplug Detection. An empty list on its own is not proof:
                // adb restarting or the phone re-enumerating on the bus empties
                // it for a few seconds. The tracker holds the disconnect back
                // until the absence has lasted longer than its grace period.
                if (count == 0)
                {
                    if (WorkflowState != AppWorkflowState.Idle && _presence.IsUnplugged)
                    {
                        ResetToIdle();
                    }
                    return;
                }

                // Only auto-start if AutoDetectOnPlug is explicitly enabled AND device hasn't started yet AND trial limit not reached
                if (!AutoDetectOnPlug || Busy || IsTrialLimitReached) { return; }

                string? udid = SelectedDevice.Key;
                if (udid is { Length: > 0 })
                {
                    // Check if this device was disconnected mid-test and can be resumed
                    if (DeviceSessionManager.TryGetPreservedSession(udid, out var preserved) && preserved?.Data != null)
                    {
                        DeviceData = preserved.Data;
                        Progress = 0; // Always restart from 0 on reconnect
                        ComponentChecks.Clear();
                        foreach (var c in DeviceData.ComponentChecks) ComponentChecks.Add(c);
                        WorkflowState = AppWorkflowState.Active;
                        Status = LocalizationManager.GetString("Status_Reconnected");
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
            Status = LocalizationManager.GetString("Status_Disconnected");
        }
        else
        {
            Status = LocalizationManager.GetString("Status_ConnectUsb");
        }

        WorkflowState = AppWorkflowState.Idle;
        Busy = false;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
    }

    /// <summary>
    /// Where a device refresh gets its answer. Production leaves this null and
    /// reads the cable; a test hands one over so the how-to can be proven
    /// without an adb binary or a phone that declines to trust this computer.
    /// </summary>
    public Func<Task<(Dictionary<string, string> Devices, DeviceService.ConnectionState State)>>? DeviceProbe { get; set; }

    /// <summary>
    /// Reads the device list and the adb state in one go, and opens or closes
    /// the USB debugging how-to to match. Called from both refresh paths:
    /// doing it only on the manual scan meant the guide never appeared on
    /// auto-detect and never cleared once the cable was pulled.
    /// </summary>
    private async Task<(Dictionary<string, string> Devices, DeviceService.ConnectionState State)> GetDevicesWithAdbStateAsync()
    {
        var (devices, diagState) = DeviceProbe is null
            ? await DeviceService.GetConnectedDevicesWithStateAsync()
            : await DeviceProbe();

        bool unauthorized = diagState == DeviceService.ConnectionState.Unauthorized;

        // The card goes up with whatever is known at that moment. Naming it is a
        // second, slower thing: the walk through the PnP tree takes a beat, and
        // several when the OS has not published the model yet, so the how-to is
        // worth showing before that answer arrives rather than after it.
        if (unauthorized && !_adbGuideDismissed && AdbTutorialViewModel.TryBeginDeviceNameLookup())
            _ = NameThePhoneAsync();

        await OnUiThreadAsync(() =>
        {
            // One rule, in one place, for every answer the probe can give. See
            // AdbGuidePolicy: only a phone that does not trust this computer
            // opens the how-to and only a phone adb can talk to closes it.
            switch (AdbGuidePolicy.Decide(diagState, _adbGuideDismissed))
            {
                case AdbGuideDecision.Show:
                    ShowAdbWarning = true;
                    break;
                case AdbGuideDecision.Hide:
                    ShowAdbWarning = false;
                    break;
            }
        });
        _lastDiagState = diagState;

        return (devices, diagState);
    }

    /// <summary>
    /// Asks the OS what is on the cable and points the how-to at it. Runs on its
    /// own: a phone already on the cable has no connection event to be named by,
    /// and one that just arrived is named by the OS a beat after the interface
    /// that identified it, so the card is opened first and narrowed down when the
    /// answer gets in.
    /// </summary>
    private async Task NameThePhoneAsync()
    {
        try
        {
            (string brand, string model) = await Task.Run(() => _adbDirector.IdentifyConnectedDevice());
            if (brand.Length > 0)
                await OnUiThreadAsync(() => AdbTutorialViewModel.SetDevice(brand, model));
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.UsbDetector, $"Naming the phone failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Puts UI work on the UI thread, and where the caller is already on it,
    /// does it here. Posting to a queue the caller is itself holding waits for
    /// nobody to empty it: a test that awaits a refresh would never come back,
    /// and in the app it is a needless hop before the same statement runs.
    /// </summary>
    private static async Task OnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>True when a device list refresh surfaced exactly one usable device.</summary>
    private async Task<int> RefreshDeviceListSilentAsync()
    {
        try
        {
            var (devices, _) = await GetDevicesWithAdbStateAsync();
            if (devices.Count == 0)
            {
                await OnUiThreadAsync(() =>
                {
                    Devices.Clear();
                    SelectedDevice = new KeyValuePair<string, string>("", "");
                });
                return 0;
            }

            await OnUiThreadAsync(() =>
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

            // The probe inside the silent refresh already produced this state;
            // re-running it here would spawn adb and libimobiledevice once more.
            var diagState = _lastDiagState;
            
            Status = diagState switch
            {
                DeviceService.ConnectionState.ToolsMissing =>
                    LocalizationManager.GetString("Status_ToolsMissing"),
                DeviceService.ConnectionState.Unauthorized =>
                    LocalizationManager.GetString("Status_Unauthorized"),
                DeviceService.ConnectionState.PermissionDenied =>
                    LocalizationManager.GetString("Status_PermissionDenied"),
                DeviceService.ConnectionState.DriverMissing =>
                    LocalizationManager.GetString("Status_DriverMissing"),
                DeviceService.ConnectionState.DaemonStopped =>
                    LocalizationManager.GetString(OperatingSystem.IsWindows()
                        ? "Status_DaemonStoppedWin"
                        : "Status_DaemonStoppedUnix"),
                DeviceService.ConnectionState.NotTrusted =>
                    LocalizationManager.GetString("Status_NotTrusted"),
                _ =>
                    LocalizationManager.GetString("Status_NoDeviceFound")
            };
            return;
        }

        Status = count switch
        {
            1 => LocalizationManager.GetString("Status_OneFound"),
            _ => string.Format(LocalizationManager.GetString("Status_ManyFound"), count),
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

    /// <summary>
    /// The licensing half of starting a scan: asks the gate, then refreshes the
    /// status bindings with whatever the gate decided. The gate spends a free
    /// scan (or validates Pro) inside EvaluateAsync, so the count on screen is
    /// stale from that moment on until this raises the changes. Returns false
    /// when the trial limit blocks the scan, and then no device is touched.
    /// </summary>
    public async Task<bool> PassScanGateAsync()
    {
        DeviceService.ScanInitResult verdict = await DeviceService.InitializeScanAsync().ConfigureAwait(false);
        RefreshLicensingState();
        return verdict == DeviceService.ScanInitResult.Proceed;
    }

    /// <summary>The whole pipeline for one device.</summary>
    private async Task RunFlowAsync()
    {
        string? udid = SelectedDevice.Key;
        if (udid is not { Length: > 0 })
        {
            Status = LocalizationManager.GetString("Status_NoDeviceSelected");
            return;
        }

        // Licensing gate: block at the limit before any device work starts.
        if (!await PassScanGateAsync().ConfigureAwait(false))
        {
            Status = LocalizationManager.GetString("Status_TrialLimit");
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
            Status = LocalizationManager.GetString("Status_Checking");
            var state = await DeviceService.GetConnectionStateAsync(udid);
            if (state == DeviceService.ConnectionState.Unauthorized)
            {
                Status = LocalizationManager.GetString("Status_Unauthorized");
                WorkflowState = AppWorkflowState.Idle; // Fallback to Idle
                return;
            }
            if (state == DeviceService.ConnectionState.NotTrusted)
            {
                Status = LocalizationManager.GetString("Status_NotTrusted");
                WorkflowState = AppWorkflowState.Idle; // Fallback to Idle
                return;
            }
            if (state == DeviceService.ConnectionState.PermissionDenied)
            {
                Status = LocalizationManager.GetString("Status_PermissionDenied");
                return;
            }
            if (state == DeviceService.ConnectionState.DriverMissing)
            {
                Status = LocalizationManager.GetString("Status_DriverMissing");
                return;
            }
            if (state == DeviceService.ConnectionState.DaemonStopped)
            {
                Status = LocalizationManager.GetString(OperatingSystem.IsWindows()
                    ? "Status_DaemonStoppedWin"
                    : "Status_DaemonStoppedUnix");
                return;
            }
            if (state != DeviceService.ConnectionState.Connected)
            {
                Status = LocalizationManager.GetString("Status_Unreachable");
                return;
            }
            Progress = 20;

            // 2. Activation bypass (optional)
            if (AutoActivate && state == DeviceService.ConnectionState.NotActivated ||
                AutoActivate && await NeedsActivationAsync(udid))
            {
                Status = LocalizationManager.GetString("Status_Activating");
                string result = await ActivationService.SkipActivationAsync(udid);
                Status = result;
            }
            Progress = 40;

            // 3. Read device data
            Status = LocalizationManager.GetString("Status_ReadingData");
            DeviceData = await DeviceService.GetDeviceDataAsync(udid);
            ComponentChecks.Clear();
            foreach (var check in DeviceData.ComponentChecks)
                ComponentChecks.Add(check);
            Progress = 60;

            // 4. Battery health: keep actual percentage in UI, label service adds [X] when printing if < 85%

            // 5. Diagnostics (panic logs & sensors): strictly asynchronous on background thread (non-blocking)
            if (RunDiagnostics)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var diagIssues = await DiagnosticService.DiagnoseAsync(udid);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var issue in diagIssues)
                                Issues.Add(issue);
                            HasIssues = Issues.Count > 0;
                        });
                    }
                    catch { }
                });
            }
            Progress = 75;

            // Wait for user to explicitly click 'Afronden'
            Status = LocalizationManager.GetString("Status_SpecsRead");
        }
        catch (Exception ex)
        {
            Status = string.Format(LocalizationManager.GetString("Status_Error"), ex.Message);
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
        string penalised = GradePolicy.ApplyMissingApiPenalty(quality, DeviceData.ComponentChecks);
        if (penalised != quality)
        {
            quality = penalised;
            SystemEventLogger.Warning(LogSource.Desktop, "Prevented Grade 'A' selection due to missing mandatory browser APIs.", DeviceData.Identifier);
            Status = LocalizationManager.GetString("Status_GradeDowngraded");
        }
        
        SelectedGrade = quality;
        IsQualityPopupVisible = false;

        if (DefaultPaymentMethod is { Length: > 0 })
        {
            await ContinueAfterPaymentAsync(DefaultPaymentMethod);
        }
        else
        {
            Progress = 90;
            Status = LocalizationManager.GetString("Status_ChoosePayment");
            IsPaymentPopupVisible = true;
        }
    }

    private Task ContinueAfterPaymentAsync(string method)
    {
        SelectedInvoiceMethod = method;
        IsQualityPopupVisible = false;
        IsPaymentPopupVisible = false;
        Progress = 100;
        Status = LocalizationManager.GetString("Status_TestsComplete");
        // Populate defect inspection report (only non-OEM/mismatch components and failed tests)
        DefectiveComponents.Clear();
        foreach (var c in DeviceData.ComponentChecks)
        {
            if (c.Status == ComponentStatusType.Mismatch || c.Status == ComponentStatusType.Untrusted)
            {
                DefectiveComponents.Add(c);
            }
        }

        RefreshInteractiveTestLists();

        WorkflowState = AppWorkflowState.Summary;

        string? udid = SelectedDevice.Key;
        if (!string.IsNullOrWhiteSpace(udid))
        {
            DeviceSessionManager.MarkCompleted(udid, DeviceData);
        }

        if (OpenEditorBeforePrint)
        {
            DataEditorRequested?.Invoke(DeviceData);
            return Task.CompletedTask;
        }

        if (!ShowSummaryScreenAfterTesting)
        {
            FinishInspectionAndShow();
        }

        return Task.CompletedTask;
    }

    /// <summary>Generate + open the label; single click path for the user.</summary>
    private async Task FinishInspectionAsync()
    {
        // Check if PWA test is required and not completed
        if (RequirePwaTest && DeviceData.InteractiveTests == null)
        {
            Status = LocalizationManager.GetString("Status_PwaRequired");
            return;
        }

        // Enforce Grading Penalty: Prevent auto 'A' grade if missing mandatory APIs
        string targetQuality = GradePolicy.ApplyMissingApiPenalty(DefaultQuality, DeviceData.ComponentChecks);
        if (targetQuality != DefaultQuality)
        {
            SystemEventLogger.Warning(LogSource.Desktop, "Downgraded automatic grade from A to B due to missing mandatory browser APIs.", DeviceData.Identifier);
        }

        // 6. Quality + payment: defaults from settings, else popup
        if (!string.IsNullOrEmpty(targetQuality))
            await ContinueAfterQualityAsync(targetQuality);
        else
        {
            IsQualityPopupVisible = true;
            Status = LocalizationManager.GetString("Status_ChooseQuality");
        }
    }

    /// <summary>
    /// Records the inspection and shows the report.
    ///
    /// It used to write the label and open whatever application the system had
    /// registered for a .dymo file, which meant the flow ended in either the DYMO
    /// editor or an error dialog depending on what was installed on the operator's
    /// machine. It now records the inspection and puts the operator in front of the
    /// export panel, which is the same screen the summary screen's button opens and
    /// which works the same whether or not DYMO software is present.
    /// </summary>
    private void FinishInspectionAndShow()
    {
        try
        {
            Status = LocalizationManager.GetString("Status_LabelGenerating");
            AuditLogService.ExportAuditLog(DeviceData);
            Progress = 100;
            WorkflowState = AppWorkflowState.Summary;

            if (!ShowSummaryScreenAfterTesting)
            {
                // With no report screen there is nowhere for the operator to read
                // what was found, so the export panel opens in its place rather
                // than the label being written to a file nobody is shown.
                ExportViewModel?.Open();
            }
        }
        catch (Exception ex)
        {
            Status = string.Format(LocalizationManager.GetString("Status_LabelFailed"), ex.Message);
        }
    }

    // IMEI.info BYOK Command Implementations
    private Task SaveImeiInfoApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ImeiInfoApiKey))
        {
            ImeiInfoApiKeyStatus = string.Format(LocalizationManager.GetString("Settings_ImeiInfoKeyInvalid") ?? "Invalid API Key: {0}", LocalizationManager.GetString("Settings_ImeiInfoKeyEmpty") ?? "Empty key");
            ImeiInfoApiKeyStatusColor = "#ef4444"; // Red
            return Task.CompletedTask;
        }

        _settings.ImeiInfoApiKey = ImeiInfoApiKey;
        _settings.Save();
        ImeiInfoApiKeyStatus = LocalizationManager.GetString("Settings_ImeiInfoApiKeySaved") ?? "API key saved";
        ImeiInfoApiKeyStatusColor = "#22c55e"; // Green
        return Task.CompletedTask;
    }

    private async Task TestImeiInfoApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ImeiInfoApiKey))
        {
            ImeiInfoApiKeyStatus = string.Format(LocalizationManager.GetString("Settings_ImeiInfoKeyInvalid") ?? "Invalid API Key: {0}", LocalizationManager.GetString("Settings_ImeiInfoKeyEmpty") ?? "Empty key");
            ImeiInfoApiKeyStatusColor = "#ef4444";
            return;
        }

        ImeiInfoApiKeyStatus = LocalizationManager.GetString("Settings_ImeiInfoKeyTesting") ?? "Testing API key...";
        ImeiInfoApiKeyStatusColor = "#f59e0b"; // Amber

        var (success, balance, error) = await PhoneGrade.Core.SecurityServices.ImeiInfoApiService.GetBalanceAsync(ImeiInfoApiKey);

        if (success)
        {
            ImeiInfoApiKeyStatus = string.Format(LocalizationManager.GetString("Settings_ImeiInfoKeyValid") ?? "API Key Valid. Balance: {0} USD", balance.ToString("F2"));
            ImeiInfoApiKeyStatusColor = "#22c55e"; // Green
        }
        else
        {
            ImeiInfoApiKeyStatus = string.Format(LocalizationManager.GetString("Settings_ImeiInfoKeyInvalid") ?? "Invalid API Key: {0}", error ?? LocalizationManager.GetString("Settings_ImeiInfoKeyUnknownError") ?? "Unknown error");
            ImeiInfoApiKeyStatusColor = "#ef4444"; // Red
        }
    }

    }
