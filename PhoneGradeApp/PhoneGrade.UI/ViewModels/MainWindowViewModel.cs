using System.Collections.ObjectModel;
using System.IO;
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
using PhoneGrade.UI.ShopProfiles;
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

    // Held so Shutdown can take the static USB events back off this instance.
    // Anonymous handlers cannot be unsubscribed, and a disposed view model that
    // still answers a plug event brings a closed window back to life.
    private readonly EventHandler _usbConnected;
    private readonly EventHandler _usbDisconnected;

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

    /// <summary>
    /// The label settings and their live preview, for the settings page.
    /// </summary>
    /// <remarks>
    /// Separate from the export panel because it answers a different question: the
    /// panel is "what does this phone's label look like", the settings are "what
    /// does every label look like". They read the same stored values and both draw
    /// through the same plate.
    /// </remarks>
    public LabelSettingsViewModel? LabelSettings { get; private set; }

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

    private string _language = SupportedLanguages.NameOf(SupportedLanguages.DefaultCode);
    public string Language
    {
        get => _language;
        set
        {
            // The dropdown speaks in names and the settings file in codes, so the
            // name has to be resolved rather than compared against a fixed pair.
            // An unknown name resolves to Dutch, which is the language the app
            // starts in anyway, so a stale settings file cannot leave the picker
            // showing one language and the window drawing another.
            string code = Services.SupportedLanguages.CodeOf(value);
            string name = Services.SupportedLanguages.NameOf(code);

            _settings.Language = code;
            _settings.Save();
            Services.LocalizationManager.SetLanguage(code);
            this.RaiseAndSetIfChanged(ref _language, name);
            // The how-to holds a formatted heading and a list of keys rather
            // than text, so neither re-reads itself when the dictionary swaps.
            AdbTutorialViewModel?.RefreshLocalization();
            // The grade and invoice method are formatted from the dictionary rather
            // than stored, so the two summary rows have to be asked to re-read.
            this.RaisePropertyChanged(nameof(SelectedGradeDisplay));
            this.RaisePropertyChanged(nameof(SelectedInvoiceMethodDisplay));
            // The import preview is made of words the diff already spoke, so it
            // is built again rather than patched.
            RebuildShopProfilePreview();
        }
    }
    
    /// <summary>Every shipped language, in its own script. Drives the picker in the
    /// settings drawer and on the introduction screen.</summary>
    public string[] LanguageOptions { get; } = Services.SupportedLanguages.Names;

    private bool _autoActivate;
    public bool AutoActivate
    {
        get => _autoActivate;
        set { _settings.AutoActivate = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _autoActivate, value); }
    }

    private bool _includePrereleases;

    /// <summary>
    /// Whether this shop wants to be offered beta builds.
    /// </summary>
    /// <remarks>
    /// The row underneath says what a beta is, because a shop working a counter should
    /// not have to guess. It is off on a fresh install and on every existing one, since
    /// a missing key reads as false.
    ///
    /// Turning it on is the whole of the beta channel from this side. The build side is
    /// a prerelease tag, which the release workflow puts on a separate channel, so the
    /// only thing this does is say whether to look at that channel at all.
    /// </remarks>
    public bool IncludePrereleases
    {
        get => _includePrereleases;
        set
        {
            _settings.IncludePrereleases = value;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _includePrereleases, value);

            // Look for the beta straight away rather than at the next start, so the
            // effect of the tick is not something the operator has to restart to find
            // out. It goes through the same path a normal update takes, including the
            // catch-all that swallows an offline machine.
            if (value) _ = AutoUpdater.CheckAndApplyAsync(_settings);
        }
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
            // ReactiveUI 20 returns the newly set value from RaiseAndSetIfChanged,
            // not a changed flag, so the guard asks the field directly. The old
            // `if (!RaiseAndSetIfChanged(...)) return;` skipped the body whenever
            // the new value was false, which is how a switch meant to turn the
            // cable route off turned nothing off.
            if (_useSecureOrigin == value) return;
            this.RaiseAndSetIfChanged(ref _useSecureOrigin, value);
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
            // Same guard as UseSecureOrigin, for the same ReactiveUI 20 reason.
            if (_usePublicTunnel == value) return;
            this.RaiseAndSetIfChanged(ref _usePublicTunnel, value);
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

        EstimatedMonthlyCostDisplay = string.Format(LocalizationManager.GetString("Settings_ImeiInfoEstimatedMonthlyCost"), monthlyCost.ToString("F2", LocalizationManager.Culture));
        EstimatedPerDeviceDisplay = string.Format(LocalizationManager.GetString("Settings_ImeiInfoEstimatedPerDevice"), perDeviceCost.ToString("F4", LocalizationManager.Culture));
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
            LabelSettingsChanged();
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
            this.RaisePropertyChanged(nameof(EffectiveLabelBarcodeMode));
            LabelSettingsChanged();
        }
    }

    /// <summary>
    /// Which symbology the label's barcode is drawn in.
    /// </summary>
    /// <remarks>
    /// Its own setting rather than a part of the mode, because the two answer
    /// different questions: the mode says what the code carries and the symbology says
    /// how it is written down. A shop that wants one code carrying everything needs
    /// both of them chosen, and a shop whose scanner only reads Code39 needs the
    /// other pairing.
    /// </remarks>
    public LabelCodeSymbology LabelSymbology
    {
        get => _settings.LabelSymbology;
        set
        {
            _settings.LabelSymbology = value;
            _settings.Save();
            this.RaisePropertyChanged();
            LabelSettingsChanged();
        }
    }

    /// <summary>Called by the label settings picker, which owns the collection.</summary>
    public void SetLabelStock(LabelStock stock)
    {
        if (string.Equals(_labelStockPartNumber, stock.PartNumber, StringComparison.Ordinal)) return;
        _labelStockPartNumber = stock.PartNumber;
        _settings.LabelStockPartNumber = stock.PartNumber;
        _settings.Save();
        this.RaiseAndSetIfChanged(ref _labelStockPartNumber, stock.PartNumber);
        this.RaisePropertyChanged(nameof(LabelStock));
        LabelSettingsChanged();
    }

    /// <summary>Called by the label settings picker.</summary>
    public void SetLabelSymbology(LabelCodeSymbology symbology) => LabelSymbology = symbology;

    /// <summary>Called by the label settings picker.</summary>
    public void SetLabelBarcodeMode(LabelBarcodeMode mode)
    {
        if (_settings.LabelBarcodeMode == mode) return;
        _settings.LabelBarcodeMode = mode;
        _settings.Save();
        this.RaisePropertyChanged(nameof(LabelBarcodeMode));
        this.RaisePropertyChanged(nameof(EffectiveLabelBarcodeMode));
        LabelSettingsChanged();
    }

    /// <summary>The stock, as the label writers take it.</summary>
    public LabelStock LabelStock => LabelStock.FromPartNumber(_labelStockPartNumber);

    /// <summary>
    /// The switches that decide what the label says, as the writers take them.
    /// </summary>
    /// <remarks>
    /// Gathered into one object so the label's wording is passed around as a single
    /// value. Every renderer takes it, which is the only way the preview can promise
    /// it is showing what the file will contain.
    ///
    /// The faults and the locks are always on. They used to be switches, and a switch
    /// that takes the locks off a label takes off the one line that stops a FRP locked
    /// phone being sold; the panel wording said so and it was still the wrong thing to
    /// offer. A clean phone carries neither line because there is nothing to say, not
    /// because somebody turned the line off.
    /// </remarks>
    public LabelContent LabelContent => new(
        BatteryCycles: _settings.LabelShowBatteryCycles,
        Faults: true,
        Locks: true);

    /// <summary>Whether the charge count is on the label at all.</summary>
    public bool LabelShowBatteryCycles
    {
        get => _settings.LabelShowBatteryCycles;
        set { _settings.LabelShowBatteryCycles = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _labelShowBatteryCycles, value); LabelSettingsChanged(); }
    }

    private bool _labelShowBatteryCycles = true;

    /// <summary>
    /// How the label is arranged: the single specification line, the structured
    /// arrangement, or the grade as a block of its own.
    /// </summary>
    public LabelVariant LabelVariant
    {
        get => _settings.LabelVariant;
        set { _settings.LabelVariant = value; _settings.Save(); this.RaisePropertyChanged(); LabelSettingsChanged(); }
    }

    /// <summary>
    /// Whether the label carries a barcode at all.
    /// </summary>
    /// <remarks>
    /// Off is for the shop whose scanner cannot read one; the barcode itself is one
    /// Code39 code with the serial number, which is what every till reads. What the
    /// code carries and which symbology it is drawn in stay in the label settings
    /// behind this switch, because a shop that scans nothing has no use for either.
    /// </remarks>
    public bool LabelBarcodeEnabled
    {
        get => _settings.LabelBarcodeEnabled;
        set
        {
            _settings.LabelBarcodeEnabled = value;
            _settings.Save();
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(LabelBarcodeMode));
            LabelSettingsChanged();
        }
    }

    /// <summary>The barcode mode the writers actually use: none when the switch is off.</summary>
    public LabelBarcodeMode EffectiveLabelBarcodeMode =>
        _settings.LabelBarcodeEnabled ? _settings.LabelBarcodeMode : LabelBarcodeMode.None;

    /// <summary>
    /// The battery percentage under which the marker goes on the label, and under
    /// which the report counts the battery as a deviation.
    /// </summary>
    public int LabelBatteryThreshold
    {
        get => _settings.LabelBatteryThreshold;
        set
        {
            int wanted = Math.Clamp(value, 50, 95);
            if (_settings.LabelBatteryThreshold == wanted) return;
            _settings.LabelBatteryThreshold = wanted;
            _settings.Save();
            this.RaisePropertyChanged();
            LabelSettingsChanged();
        }
    }

    /// <summary>Below this charge count the number is left off the label.</summary>
    public int LabelCyclesThreshold
    {
        get => _settings.LabelCyclesThreshold;
        set
        {
            int wanted = Math.Clamp(value, 0, 5000);
            if (_settings.LabelCyclesThreshold == wanted) return;
            _settings.LabelCyclesThreshold = wanted;
            _settings.Save();
            this.RaisePropertyChanged();
            LabelSettingsChanged();
        }
    }

    /// <summary>The files a finished inspection writes without being asked.</summary>
    public List<ExportFormat> ExportFormats
    {
        get => _settings.ExportFormats;
        set
        {
            _settings.ExportFormats = value;
            _settings.Save();
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Whether a format is in the set a finish writes.</summary>
    public bool WritesFormat(ExportFormat format) => _settings.ExportFormats.Contains(format);

    /// <summary>Adds or removes one format from the set a finish writes.</summary>
    public void SetWritesFormat(ExportFormat format, bool wanted)
    {
        var formats = new List<ExportFormat>(_settings.ExportFormats);
        if (wanted && !formats.Contains(format)) formats.Add(format);
        if (!wanted) formats.Remove(format);

        ExportFormats = formats;
    }

    /// <summary>How the exports are grouped in the folder the operator sees.</summary>
    public ExportFolderScheme ExportFolderScheme
    {
        get => _settings.ExportFolderScheme;
        set { _settings.ExportFolderScheme = value; _settings.Save(); this.RaisePropertyChanged(); }
    }

    /// <summary>Tells the preview and the writers that the label's wording changed.</summary>
    private void LabelSettingsChanged()
    {
        ExportViewModel?.ReloadLabelSettings();
        LabelSettings?.Refresh();
    }

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

    // ============ Shop profile: carrying settings to another computer ============
    //
    // A profile is the shareable half of the settings, written to a file the
    // operator carries to the next computer. Import shows what would change
    // before anything is written, so a wrong file is caught at the bench rather
    // than after the label settings have already moved.

    /// <summary>
    /// A profile that was read but not applied yet. Kept so the preview can be
    /// built again (after a language switch) and so Apply knows what to write.
    /// </summary>
    private ShopProfile? _pendingShopProfile;

    private string _shopProfileStatus = "";

    /// <summary>The sentence under the export and import buttons.</summary>
    public string ShopProfileStatus
    {
        get => _shopProfileStatus;
        set => this.RaiseAndSetIfChanged(ref _shopProfileStatus, value);
    }

    private string _shopProfileStatusColor = "Transparent";

    /// <summary>Green for done, amber for nothing to do, red for a failure.</summary>
    public string ShopProfileStatusColor
    {
        get => _shopProfileStatusColor;
        set => this.RaiseAndSetIfChanged(ref _shopProfileStatusColor, value);
    }

    private IReadOnlyList<ShopProfileChange> _shopProfileChanges = [];

    /// <summary>The rows of the import preview, in the order the settings appear in the app.</summary>
    public IReadOnlyList<ShopProfileChange> ShopProfileChanges
    {
        get => _shopProfileChanges;
        private set
        {
            this.RaiseAndSetIfChanged(ref _shopProfileChanges, value);
            this.RaisePropertyChanged(nameof(HasShopProfilePreview));
            this.RaisePropertyChanged(nameof(ShopProfilePreviewSummary));
        }
    }

    /// <summary>True when a picked profile differs from this computer's settings.</summary>
    public bool HasShopProfilePreview => _pendingShopProfile is not null && _shopProfileChanges.Count > 0;

    /// <summary>How many settings the preview is about to overwrite.</summary>
    public string ShopProfilePreviewSummary => string.Format(
        LocalizationManager.GetString("Settings_ShopProfilePreviewSummary"), _shopProfileChanges.Count);

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
        set => this.RaiseAndSetIfChanged(ref _isIntroVisible, value);
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
        ["Language", "Plan", "Workflow", "Licence", "Imei"];

    private string _introPage = "Language";

    /// <summary>The page of the introduction currently on screen.</summary>
    public string IntroPage
    {
        get => _introPage;
        set
        {
            this.RaiseAndSetIfChanged(ref _introPage, value);
            this.RaisePropertyChanged(nameof(IsFirstIntroPage));
            this.RaisePropertyChanged(nameof(IsLastIntroPage));
            this.RaisePropertyChanged(nameof(IsOnLicencePage));
            this.RaisePropertyChanged(nameof(ShowIntroNext));
            this.RaisePropertyChanged(nameof(IntroStepNumber));
            this.RaisePropertyChanged(nameof(IntroStepDisplay));
        }
    }

    /// <summary>How many pages the introduction has, for the progress line.</summary>
    public int IntroPageCount => IntroPages.Count;

    /// <summary>Which page is on screen, counting from one.</summary>
    public int IntroStepNumber => IntroPages.IndexOf(IntroPage) + 1;

    /// <summary>
    /// The step counter beside the progress bar. Numbers only, so it needs no
    /// translation and cannot disagree with a language switch halfway through.
    /// </summary>
    public string IntroStepDisplay => $"{IntroStepNumber} / {IntroPageCount}";

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

    /// <summary>
    /// Accepts the terms and continues to the last page. Not the way out: the
    /// introduction ends on the last page, where Finish stands.
    /// </summary>
    public ReactiveCommand<Unit, Unit> AcceptIntroTermsCommand { get; }

    /// <summary>Opens the introduction again from the settings page.</summary>
    public ReactiveCommand<Unit, Unit> ReplayIntroCommand { get; }

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

    /// <summary>
    /// Ends the introduction, and refuses to record it as seen until the terms have
    /// been accepted.
    ///
    /// Every route out of the introduction arrives here, so this is the one place the
    /// check has to be. Without it the last page's Finish would be a way around the
    /// agreement, and a gate that can be walked past is worse than no gate because it
    /// reads as one.
    /// </summary>
    private void DismissIntro()
    {
        if (!CanFinishIntro) return;

        _settings.IntroSeen = true;
        _settings.Save();
        IsIntroVisible = false;
        IsIntroActivationVisible = false;
    }

    /// <summary>The page carrying the agreement, named so the markup cannot drift.</summary>
    public const string LicencePage = "Licence";

    /// <summary>
    /// Whether the operator has accepted the terms that apply to them.
    ///
    /// Deliberately not written to the settings file. An operator who ticked this on
    /// one machine has not agreed on another, and a gate that remembered across
    /// machines could be walked past with a single tick on the first one. It is asked
    /// once per machine, which is also the only point at which the answer is worth
    /// anything.
    /// </summary>
    private bool _isLicenceAccepted;
    public bool IsLicenceAccepted
    {
        get => _isLicenceAccepted;
        set
        {
            // The field is asked, not the return value: ReactiveUI 20 returns the
            // new value from RaiseAndSetIfChanged, so `if (RaiseAndSetIfChanged(...))`
            // ran the dependent raise only when the box was ticked and skipped it
            // when it was cleared.
            if (_isLicenceAccepted == value) return;
            this.RaiseAndSetIfChanged(ref _isLicenceAccepted, value);
            this.RaisePropertyChanged(nameof(CanFinishIntro));
        }
    }

    /// <summary>
    /// Whether the introduction may be closed.
    ///
    /// Checked in <see cref="DismissIntro"/> rather than only on the buttons,
    /// because finishing and the terms page's continue are both ways out and
    /// neither may skip the one page that is not optional.
    /// </summary>
    public bool CanFinishIntro => IsLicenceAccepted;

    public bool IsOnLicencePage => IntroPage == LicencePage;

    /// <summary>
    /// Next is offered everywhere except the agreement page and the last page, where
    /// the button says Finish instead.
    /// </summary>
    public bool ShowIntroNext => !IsOnLicencePage && !IsLastIntroPage;

    /// <summary>
    /// Which document applies. The free plan is covered by the licence the source is
    /// published under. A paying customer is not, which is why the commercial end user
    /// licence agreement is the document that has to be named to them.
    /// </summary>
    public string LicenceDocumentUrl =>
        Services.LicenceTerms.DocumentFor(Licensing.IsPro);

    /// <summary>
    /// What the checkbox says. It names the document rather than only saying agree,
    /// so an operator who did not read it can still say afterwards which document it
    /// was.
    /// </summary>
    public string LicenceAgreementLabel => LocalizationManager.GetString(
        Licensing.IsPro
            ? "Intro_LicenceAcceptCommercial"
            : "Intro_LicenceAcceptFree");

    public ReactiveCommand<Unit, Unit> OpenLicenceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTrademarkCommand { get; }

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
            LabelSettingsChanged();
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
    /// What changed in the version that is running.
    ///
    /// An update replaces the binaries and restarts, so there is no window open at
    /// the moment the new version lands to say what it is. The notes are read from the
    /// release on GitHub, which is where the release workflow writes them, so this
    /// answers a question an operator can ask on any launch rather than only on the
    /// launch after an update installed itself.
    /// </summary>
    private bool _isChangelogVisible;
    public bool IsChangelogVisible { get => _isChangelogVisible; set => this.RaiseAndSetIfChanged(ref _isChangelogVisible, value); }

    private string _changelogVersion = "";
    public string ChangelogVersion { get => _changelogVersion; set => this.RaiseAndSetIfChanged(ref _changelogVersion, value); }

    private string _changelogNotes = "";
    public string ChangelogNotes { get => _changelogNotes; set => this.RaiseAndSetIfChanged(ref _changelogNotes, value); }

    /// <summary>True while the release is being read, so the panel can say so rather than sit empty.</summary>
    private bool _isChangelogLoading;
    public bool IsChangelogLoading { get => _isChangelogLoading; set => this.RaiseAndSetIfChanged(ref _isChangelogLoading, value); }

    /// <summary>
    /// Set when there is nothing to show and no way to fetch it: a machine with no
    /// network that has never opened this panel, or a release with an empty body.
    /// </summary>
    private bool _changelogUnavailable;
    public bool ChangelogUnavailable { get => _changelogUnavailable; set => this.RaiseAndSetIfChanged(ref _changelogUnavailable, value); }

    public ReactiveCommand<Unit, Unit> CloseChangelogCommand { get; }

    /// <summary>Opens the panel on demand, rather than only when an update put it there.</summary>
    public ReactiveCommand<Unit, Unit> OpenChangelogCommand { get; }

    /// <summary>Opens the release page, which carries the same notes with their formatting.</summary>
    public ReactiveCommand<Unit, Unit> OpenChangelogReleaseCommand { get; }

    /// <summary>
    /// Shows the notes for the running version, and admits it when there are none.
    ///
    /// Whatever is cached goes on screen first, so the panel is never empty while it
    /// waits, and then the release is read in the background. That order is the point
    /// of asking GitHub rather than only carrying what the updater left behind: a
    /// portable copy never has an update to carry notes, and an operator who closed
    /// the panel last week can open it again.
    /// </summary>
    private async System.Threading.Tasks.Task ShowChangelog()
    {
        var cached = ReleaseChangelog.Read();
        if (cached is not null)
        {
            ChangelogVersion = cached.Version;
            ChangelogNotes = cached.Notes;
            ChangelogUnavailable = false;
        }

        string version = AutoUpdater.RunningVersion();
        if (string.IsNullOrWhiteSpace(version)) return;

        ChangelogVersion = version.TrimStart('v');
        IsChangelogLoading = true;

        var fetched = await ReleaseChangelog.FetchAsync(version);
        IsChangelogLoading = false;

        if (fetched is null)
        {
            // Nothing from the network and nothing cached is the only case where the
            // panel has to say it has nothing.
            ChangelogUnavailable = string.IsNullOrWhiteSpace(ChangelogNotes);
            return;
        }

        ChangelogNotes = fetched.Notes;
        ChangelogUnavailable = false;
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
    public string[] PaymentOptions { get; } = ["", "Marge", "BTW", PaymentMethods.NeverAsk];

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

    // Shop profile commands
    public ReactiveCommand<Unit, Unit> ExportShopProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportShopProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyShopProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelShopProfileImportCommand { get; }

    public event Action<DeviceData>? DataEditorRequested;

    /// <summary>
    /// Raised when the operator asks to export a profile. The window answers it
    /// because only a control can open a file picker; the view model does the
    /// writing once the window hands it a path.
    /// </summary>
    public event Action? ShopProfileExportRequested;

    /// <summary>The same split for import: the window picks a path, the view model reads it.</summary>
    public event Action? ShopProfileImportRequested;

    public MainWindowViewModel(LemonSqueezyClient? licenseClient = null)
    {
        // The parameter only exists so tests can hand over a client with a fake
        // transport; production passes nothing and gets the real endpoint.
        _licenseClient = licenseClient ?? new LemonSqueezyClient();
        _settings = AppSettings.Load();
        _theme = _settings.Theme;
        // The stored code is resolved through the table, so a language from a
        // build that carried more of them still lands on the picker as its own
        // name instead of falling back to a row nobody picked.
        _language = Services.SupportedLanguages.NameOf(_settings.Language);
        _autoActivate = _settings.AutoActivate;
        _includePrereleases = _settings.IncludePrereleases;
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
        LabelSettings = new LabelSettingsViewModel(this);
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

        // Opening the documents rather than reproducing them. A licence is long, and
        // the sentence that matters is much easier to find on a page an operator can
        // scroll and search than inside a modal.
        OpenLicenceCommand = ReactiveCommand.Create(() =>
            Services.PricingLink.Open(Services.LicenceTerms.DocumentFor(Licensing.IsPro)));
        OpenTrademarkCommand = ReactiveCommand.Create(() =>
            Services.PricingLink.Open(Services.LicenceTerms.Trademark));
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
        // "Accept and continue" continues. The agreement is a step like the
        // others, and the last page is where the introduction ends: the old
        // button closed the whole screen, which left the optional-checks page
        // reachable only through the topic tabs that no longer exist.
        AcceptIntroTermsCommand = ReactiveCommand.Create(() =>
        {
            if (!CanFinishIntro) return;
            IntroPage = NextIntroPage;
        });
        // Settings has a button for this: an operator showing a colleague how the
        // app works should not have to reset the machine to see the screen again.
        // The terms were accepted to get this far, so the agreement page is shown
        // with its box ticked and the foot can close from anywhere.
        ReplayIntroCommand = ReactiveCommand.Create(() =>
        {
            IsSettingsDrawerOpen = false;
            IntroPage = IntroPages[0];
            IsLicenceAccepted = true;
            IsIntroActivationVisible = false;
            IsIntroVisible = true;
        });
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

        // Shop profile: the pickers live in the window, the file work lives here.
        ExportShopProfileCommand = ReactiveCommand.Create(() => ShopProfileExportRequested?.Invoke());
        ImportShopProfileCommand = ReactiveCommand.Create(() => ShopProfileImportRequested?.Invoke());
        ApplyShopProfileCommand = ReactiveCommand.Create(ApplyShopProfile);
        CancelShopProfileImportCommand = ReactiveCommand.Create(CancelShopProfileImport);

        OpenLogsModalCommand = ReactiveCommand.Create(() => { IsLogsModalOpen = true; IsSettingsDrawerOpen = false; });
        CloseLogsModalCommand = ReactiveCommand.Create(() => { IsLogsModalOpen = false; });
        OpenTroubleshootModalCommand = ReactiveCommand.Create(() => { IsTroubleshootModalOpen = true; IsSettingsDrawerOpen = false; });
        CloseTroubleshootModalCommand = ReactiveCommand.Create(() => { IsTroubleshootModalOpen = false; });
        BackToIdleCommand = ReactiveCommand.Create(() => { WorkflowState = AppWorkflowState.Idle; });
        CloseChangelogCommand = ReactiveCommand.Create(() => { IsChangelogVisible = false; });
        // On demand rather than only after an update, and read straight from the
        // release rather than from whatever the updater happened to leave behind.
        OpenChangelogCommand = ReactiveCommand.Create(() =>
        {
            IsChangelogVisible = true;
            ChangelogUnavailable = false;
            _ = ShowChangelog();
        });
        OpenChangelogReleaseCommand = ReactiveCommand.Create(() =>
            Services.PricingLink.Open(ReleaseChangelog.ReleasePageUrl(ChangelogVersion)));

        if (_settings.MarkVersionSeen(AutoUpdater.RunningVersion()))
        {
            // Only on the launch of a build that has not introduced itself yet. Every
            // other launch has nothing to interrupt the operator with, and the panel
            // is one click away in the settings.
            IsChangelogVisible = true;
            _ = ShowChangelog();
        }


        _usbConnected = (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (AutoDetectOnPlug && !Busy)
                {
                    _ = RefreshDeviceListSilentAsync();
                }
            });
        };

        _usbDisconnected = (s, e) =>
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

        UsbEventWatcher.UsbDeviceConnected += _usbConnected;
        UsbEventWatcher.UsbDeviceDisconnected += _usbDisconnected;
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

        // Stop USB event monitoring. The subscriptions come off by their held
        // delegates: an anonymous handler cannot be taken back off, and a view
        // model that still answers a plug event is a closed window brought back
        // to life by the next mouse someone plugs in.
        try
        {
            UsbEventWatcher.UsbDeviceConnected -= _usbConnected;
            UsbEventWatcher.UsbDeviceDisconnected -= _usbDisconnected;
            UsbEventWatcher.StopMonitoring();
            AdbTutorialViewModel?.Dispose();
            _adbDirector?.Dispose();
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector, $"Could not stop USB monitoring: {ex.Message}");
        }

        // The poll loop and a running flow both write to bound state; nothing
        // may raise a property change once the window has gone.
        try
        {
            StopWatcher();
            _flowCts?.Cancel();
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector, $"Could not stop the device watcher: {ex.Message}");
        }

        // The phone's page is served from the window's own server. Stopping the
        // host hands the port back and closes what is still connected, instead
        // of leaving the next window to find 5055 taken. The stop runs on the
        // pool and is not waited for: this runs on the window's own thread, and
        // a Kestrel stop that posts a continuation back to a waiting UI thread
        // never finishes. UiThreadDoesNotWaitTests is the guard that keeps it
        // that way.
        if (_webServer is { } server)
        {
            _webServer = null;
            _ = Task.Run(async () => await server.DisposeAsync());
        }

        // Let go of the static log event, which otherwise keeps the log view model alive.
        LogsViewModel.Dispose();
        _licensingViewModel?.Dispose();
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
        // The refresh touches bound state, so this method has to finish on the
        // UI thread whoever called it. Called from a pool thread (a watcher
        // callback, a stray continuation) it hops over first; called from the
        // UI thread it stays there. Without the hop, IsTrialLimitReached moves
        // the start button's CanExecute on that pool thread, and Avalonia
        // answers a command change from the wrong thread by ending the process.
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(PassScanGateAsync);

        DeviceService.ScanInitResult verdict = await DeviceService.InitializeScanAsync();
        RefreshLicensingState();
        return verdict == DeviceService.ScanInitResult.Proceed;
    }

    /// <summary>The whole pipeline for one device.</summary>
    private async Task RunFlowAsync()
    {
        // Every line of the flow touches bound state, so a caller off the UI
        // thread is moved over before anything runs. The gate below guards
        // itself too, because it is public and has other callers.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(RunFlowAsync);
            return;
        }

        string? udid = SelectedDevice.Key;
        if (udid is not { Length: > 0 })
        {
            Status = LocalizationManager.GetString("Status_NoDeviceSelected");
            return;
        }

        // Licensing gate: block at the limit before any device work starts.
        if (!await PassScanGateAsync())
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

        if (DefaultPaymentMethod == PaymentMethods.NeverAsk)
        {
            // A shop that does not record an invoice method is not stopped for one.
            // The device keeps no method, the label leaves the field off and the
            // report says nothing about it, which is what "not asked" means.
            await ContinueAfterPaymentAsync("");
        }
        else if (DefaultPaymentMethod is { Length: > 0 })
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
            ImeiInfoApiKeyStatus = string.Format(LocalizationManager.GetString("Settings_ImeiInfoKeyValid") ?? "API Key Valid. Balance: {0} USD", balance.ToString("F2", LocalizationManager.Culture));
            ImeiInfoApiKeyStatusColor = "#22c55e"; // Green
        }
        else
        {
            ImeiInfoApiKeyStatus = string.Format(LocalizationManager.GetString("Settings_ImeiInfoKeyInvalid") ?? "Invalid API Key: {0}", error ?? LocalizationManager.GetString("Settings_ImeiInfoKeyUnknownError") ?? "Unknown error");
            ImeiInfoApiKeyStatusColor = "#ef4444"; // Red
        }
    }

    // ============ Shop profile: export and import ============

    /// <summary>
    /// Writes the shareable settings of this computer to <paramref name="path"/>.
    /// A failure lands on the status line rather than in a dialog: the only
    /// thing the operator can do about it is pick another folder.
    /// </summary>
    public void ExportShopProfileTo(string path)
    {
        try
        {
            ShopProfile profile = ShopProfileMapper.FromSettings(_settings, AutoUpdater.RunningVersion());
            File.WriteAllText(path, ShopProfileCodec.Serialize(profile));
            ShopProfileStatus = string.Format(
                LocalizationManager.GetString("Settings_ShopProfileExportDone"), Path.GetFileName(path));
            ShopProfileStatusColor = "#22c55e"; // Green
        }
        catch (Exception ex)
        {
            ShopProfileStatus = string.Format(
                LocalizationManager.GetString("Settings_ShopProfileExportFailed"), ex.Message);
            ShopProfileStatusColor = "#ef4444"; // Red
        }
    }

    /// <summary>
    /// Reads a profile and shows what it would change, without changing
    /// anything. An unreadable or unrecognised file clears any earlier preview
    /// and says why on the status line.
    /// </summary>
    public void PreviewShopProfileImport(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            ClearShopProfilePreview();
            ShopProfileStatus = string.Format(
                LocalizationManager.GetString("Settings_ShopProfileReadFailed"), ex.Message);
            ShopProfileStatusColor = "#ef4444"; // Red
            return;
        }

        if (!ShopProfileCodec.TryParse(json, out ShopProfile? profile, out string? errorKey) || profile is null)
        {
            ClearShopProfilePreview();
            ShopProfileStatus = LocalizationManager.GetString(errorKey!);
            ShopProfileStatusColor = "#ef4444"; // Red
            return;
        }

        _pendingShopProfile = profile;
        ShopProfileChanges = ShopProfileDiff.Compare(_settings, profile);

        if (_shopProfileChanges.Count == 0)
        {
            ClearShopProfilePreview();
            ShopProfileStatus = LocalizationManager.GetString("Settings_ShopProfileNoChanges");
            ShopProfileStatusColor = "#f59e0b"; // Amber
            return;
        }

        // The preview card speaks for itself, so the line under the buttons
        // stays empty until something is applied or goes wrong.
        ShopProfileStatus = "";
        ShopProfileStatusColor = "Transparent";
    }

    /// <summary>
    /// Applies the pending profile. Every value goes through the same public
    /// property the settings screen binds to, so the side effects of a change
    /// happen exactly as if the operator had made it by hand: the theme is
    /// applied, the language dictionary swaps, the USB watcher starts or stops,
    /// the label preview reloads.
    /// </summary>
    public void ApplyShopProfile()
    {
        if (_pendingShopProfile is not { } profile) return;

        if (profile.Theme is { } theme) Theme = theme;
        if (profile.Language is { } language) Language = SupportedLanguages.NameOf(language);

        if (profile.AutoActivate is { } autoActivate) AutoActivate = autoActivate;
        if (profile.AutoDetectOnPlug is { } autoDetect) AutoDetectOnPlug = autoDetect;
        if (profile.AutoStartWebTest is { } autoStart) AutoStartWebTest = autoStart;
        if (profile.ShowSummaryScreenAfterTesting is { } showSummary) ShowSummaryScreenAfterTesting = showSummary;
        if (profile.RequirePwaTest is { } requirePwa) RequirePwaTest = requirePwa;
        if (profile.AutoFinishAfterTest is { } autoFinish) AutoFinishAfterTest = autoFinish;
        if (profile.EnableUsbEventMonitoring is { } usbMonitoring) EnableUsbEventMonitoring = usbMonitoring;

        if (profile.ImeiInfoApiKey is { } apiKey) ImeiInfoApiKey = apiKey;
        if (profile.SelectedImeiChecks is { } checks) SelectedImeiChecks = new List<string>(checks);
        if (profile.EstimatedAppleDevices is { } appleDevices) EstimatedAppleDevices = Math.Max(0, appleDevices);
        if (profile.EstimatedAndroidDevices is { } androidDevices) EstimatedAndroidDevices = Math.Max(0, androidDevices);

        if (profile.DefaultQuality is { } quality) DefaultQuality = quality;
        if (profile.DefaultPaymentMethod is { } payment) DefaultPaymentMethod = payment;

        if (profile.LabelStockPartNumber is { } stock) LabelStockPartNumber = stock;
        if (profile.LabelBarcodeMode is { } barcodeMode) LabelBarcodeMode = barcodeMode;
        if (profile.LabelSymbology is { } symbology) LabelSymbology = symbology;
        if (profile.LabelBarcodeEnabled is { } barcodeEnabled) LabelBarcodeEnabled = barcodeEnabled;
        if (profile.LabelVariant is { } variant) LabelVariant = variant;
        if (profile.LabelShowBatteryCycles is { } cycles) LabelShowBatteryCycles = cycles;
        if (profile.LabelBatteryThreshold is { } batteryThreshold) LabelBatteryThreshold = batteryThreshold;
        if (profile.LabelCyclesThreshold is { } cyclesThreshold) LabelCyclesThreshold = cyclesThreshold;
        if (profile.ExportFormats is { } exportFormats) ExportFormats = new List<ExportFormat>(exportFormats);
        if (profile.ExportFolderScheme is { } folderScheme) ExportFolderScheme = folderScheme;

        if (profile.UseSecureOrigin is { } secureOrigin) UseSecureOrigin = secureOrigin;
        if (profile.UsePublicTunnel is { } publicTunnel) UsePublicTunnel = publicTunnel;

        if (profile.RunDiagnostics is { } diagnostics) RunDiagnostics = diagnostics;
        if (profile.Enable85PercentChecker is { } checker) Enable85PercentChecker = checker;
        if (profile.OpenEditorBeforePrint is { } openEditor) OpenEditorBeforePrint = openEditor;
        if (profile.IncludePrereleases is { } prereleases) IncludePrereleases = prereleases;

        ClearShopProfilePreview();
        ShopProfileStatus = LocalizationManager.GetString("Settings_ShopProfileApplied");
        ShopProfileStatusColor = "#22c55e"; // Green
    }

    /// <summary>Drops a pending import and its preview without touching the settings.</summary>
    public void CancelShopProfileImport()
    {
        ClearShopProfilePreview();
        ShopProfileStatus = "";
        ShopProfileStatusColor = "Transparent";
    }

    /// <summary>
    /// A file picker that could not open. The window calls this so a platform
    /// failure still ends on the same status line as everything else.
    /// </summary>
    public void ReportShopProfilePickerFailed(string detail)
    {
        ShopProfileStatus = string.Format(
            LocalizationManager.GetString("Settings_ShopProfilePickerFailed"), detail);
        ShopProfileStatusColor = "#ef4444"; // Red
    }

    private void ClearShopProfilePreview()
    {
        _pendingShopProfile = null;
        ShopProfileChanges = [];
    }

    /// <summary>
    /// Re-says the preview after a language switch. The label keys would follow
    /// on their own through the converter, but the values (on and off, a
    /// language name, a stock name) were formed by the diff, so the list is
    /// built again rather than patched.
    /// </summary>
    private void RebuildShopProfilePreview()
    {
        if (_pendingShopProfile is not { } profile) return;
        ShopProfileChanges = ShopProfileDiff.Compare(_settings, profile);
    }

    }
