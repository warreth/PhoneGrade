using System;
using System.Reactive;
using System.Threading.Tasks;
using PhoneGrade.Core.Licensing;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// Everything the app shows about licensing, shared by the introduction screen,
/// the status pill in the top bar and the settings row. One view model means the
/// counter, the tier and the key input can never disagree between those three
/// places: they all read the same gate and raise the same changes.
/// </summary>
public class LicensingViewModel : ReactiveObject, IDisposable
{
    /// <summary>From this many free scans left the status pill turns amber: the run is close to ending.</summary>
    public const int AlmostOutScans = 8;

    private readonly TrialGate _gate;
    private readonly LemonSqueezyClient _client;
    private readonly Action _requestLicenseRefresh;
    private string _licenseKeyInput = "";
    private string _statusMessage = "";
    private bool _isActivating;
    private bool _isReleasing;
    private bool _isReleaseArmed;

    /// <summary>
    /// The last four characters of this machine's fingerprint, or empty when the
    /// machine cannot be identified. Read once at construction because it cannot
    /// change while the app runs, and exposed so the panel can mark the current
    /// machine in the seat list without reaching into the gate.
    /// </summary>
    private static string ShortValue => MachineFingerprint.Current.ShortValue;

    public LicensingViewModel(TrialGate gate, LemonSqueezyClient client, Action requestLicenseRefresh)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _requestLicenseRefresh = requestLicenseRefresh;

        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateAndActivateAsync);
        ValidateCommand.ThrownExceptions.Subscribe(_ => { });
        OpenPricingCommand = ReactiveCommand.Create(() => PricingLink.Open(PricingLink.Url));

        // Releasing a seat takes a machine off Pro and cannot be undone from the
        // panel, so the first click only arms the button and the second one does it.
        ReleaseSeatCommand = ReactiveCommand.CreateFromTask(ReleaseSeatAsync);
        ReleaseSeatCommand.ThrownExceptions.Subscribe(_ => { });
        CancelReleaseCommand = ReactiveCommand.Create(() => { IsReleaseArmed = false; return Unit.Default; });

        // Initial status reflects whatever the gate currently holds.
        Refresh();
    }

    /// <summary>Typed license key. Cleared after a successful activation so the key is not left on screen.</summary>
    public string LicenseKeyInput
    {
        get => _licenseKeyInput;
        set => this.RaiseAndSetIfChanged(ref _licenseKeyInput, value);
    }

    /// <summary>Result of the last activation attempt, empty before the first one. Shown under the key input.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>True while a key is being validated, so the button can disable itself.</summary>
    public bool IsActivating
    {
        get => _isActivating;
        private set => this.RaiseAndSetIfChanged(ref _isActivating, value);
    }

    /// <summary>True while a seat is being handed back, so the button can disable itself.</summary>
    public bool IsReleasing
    {
        get => _isReleasing;
        private set => this.RaiseAndSetIfChanged(ref _isReleasing, value);
    }

    /// <summary>
    /// True once the operator has pressed Release seat once and before they press
    /// it again. The button asks for confirmation by changing what it says, which
    /// keeps a destructive action one deliberate click away from taking effect
    /// without introducing a modal dialog the panel cannot own.
    /// </summary>
    public bool IsReleaseArmed
    {
        get => _isReleaseArmed;
        private set => this.RaiseAndSetIfChanged(ref _isReleaseArmed, value);
    }

    /// <summary>
    /// True when this computer holds a seat, which is the only state in which
    /// releasing one means anything. The button is hidden rather than disabled when
    /// it is false, because a disabled button on the free tier invites the question
    /// of what it would do.
    /// </summary>
    public bool CanReleaseSeat => _gate.HasSeat;

    /// <summary>Machines holding a seat on the key, as the vendor reports them.</summary>
    public int MachineCount => _gate.MachineCount;

    /// <summary>How many machines the key's plan allows. Zero when the vendor did not say.</summary>
    public int MachineLimit => _gate.MachineLimit;

    /// <summary>
    /// True only while the vendor has told us both halves of the seat fraction, so
    /// the panel never draws "0 of 0" for a key the API answered nothing about.
    /// </summary>
    public bool HasSeatNumbers => MachineLimit > 0;

    /// <summary>
    /// One line per seat this app knows about, with the current machine marked.
    ///
    /// The vendor hands back a single instance per call rather than the key's
    /// whole seat list, so this is normally one line. It says "This computer
    /// (cdef)" rather than a UUID because the operator is confirming which bench
    /// they are sitting at, not reading a serial number.
    /// </summary>
    public IReadOnlyList<string> SeatLines =>
        _gate.MachineNames.Count == 0
            ? []
            : [string.Format(LocalizationManager.GetString("Licensing_PanelSeatThis"), ShortValue)];

    /// <summary>True when the machine has no readable identity and cannot hold a seat at all.</summary>
    public bool CannotIdentifyMachine => !_gate.CanIdentifyMachine;

    public bool IsPro => _gate.IsPro;

    public int UsedScans => Math.Min(_gate.ScanCount, TrialGate.FreeScanLimit);

    public int ScansLeft => Math.Max(TrialGate.FreeScanLimit - _gate.ScanCount, 0);

    public bool IsLimitReached => !_gate.IsPro && _gate.ScanCount >= TrialGate.FreeScanLimit;

    /// <summary>True while a free tier user still has scans: the state the warning pill is for.</summary>
    public bool IsAlmostOut => !_gate.IsPro && !IsLimitReached && UsedScans >= AlmostOutScans;

    /// <summary>True when the tier, not the key box, is the thing to show. Pro users have no key box to fill.</summary>
    public bool IsFreeTier => !_gate.IsPro;

    /// <summary>
    /// True only while the free tier still has scans to spare, which is the one
    /// state where the status pill should stay quiet. Kept separate from
    /// <see cref="IsFreeTier"/> because that one is also true when the run is
    /// close to ending or already blocked, and the pill colours are exclusive.
    /// </summary>
    public bool IsFreeSteady => IsFreeTier && !IsAlmostOut && !IsLimitReached;

    /// <summary>Full tier line: "Free Tier (3/10 Scans Used)" or "Pro Tier (Active)".</summary>
    public string LicensingStatusText =>
        IsPro
            ? LocalizationManager.GetString("Settings_ProTierStatus")
            : string.Format(LocalizationManager.GetString("Settings_FreeTierStatus"), UsedScans);

    /// <summary>
    /// The seat line: "2 of 5 machines in use". Both numbers come from Lemon
    /// Squeezy, never from a table in this file, so a tier change is a change in
    /// their dashboard. Empty when the vendor did not report a limit.
    /// </summary>
    public string SeatUsageText =>
        HasSeatNumbers
            ? string.Format(LocalizationManager.GetString("Licensing_PanelSeatsUsed"), MachineCount, MachineLimit)
            : "";

    /// <summary>Label for the Release seat button: it asks first, then confirms.</summary>
    public string ReleaseSeatText =>
        IsReleaseArmed
            ? LocalizationManager.GetString("Licensing_PanelReleaseConfirm")
            : LocalizationManager.GetString("Licensing_PanelRelease");

    /// <summary>Short line for the status pill and the introduction screen.</summary>
    public string TierSummary => IsPro
        ? LocalizationManager.GetString("Licensing_TierPro")
        : string.Format(LocalizationManager.GetString("Licensing_TierFree"), ScansLeft);

    public ReactiveCommand<Unit, Unit> ValidateCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPricingCommand { get; }

    /// <summary>Hands this computer's seat back so another machine can take it.</summary>
    public ReactiveCommand<Unit, Unit> ReleaseSeatCommand { get; }

    /// <summary>Backs out of a half-pressed Release seat.</summary>
    public ReactiveCommand<Unit, Unit> CancelReleaseCommand { get; }

    /// <summary>
    /// Re-reads the gate and raises every derived property. The gate changes
    /// during a scan and during activation, and nothing else announces itself.
    /// </summary>
    public void Refresh()
    {
        this.RaisePropertyChanged(nameof(IsPro));
        this.RaisePropertyChanged(nameof(IsFreeTier));
        this.RaisePropertyChanged(nameof(IsFreeSteady));
        this.RaisePropertyChanged(nameof(IsLimitReached));
        this.RaisePropertyChanged(nameof(IsAlmostOut));
        this.RaisePropertyChanged(nameof(UsedScans));
        this.RaisePropertyChanged(nameof(ScansLeft));
        this.RaisePropertyChanged(nameof(LicensingStatusText));
        this.RaisePropertyChanged(nameof(TierSummary));
        this.RaisePropertyChanged(nameof(MachineCount));
        this.RaisePropertyChanged(nameof(MachineLimit));
        this.RaisePropertyChanged(nameof(HasSeatNumbers));
        this.RaisePropertyChanged(nameof(SeatUsageText));
        this.RaisePropertyChanged(nameof(SeatLines));
        this.RaisePropertyChanged(nameof(CanReleaseSeat));
        this.RaisePropertyChanged(nameof(CannotIdentifyMachine));
    }

    public void UpdateStatusText() => Refresh();

    private async Task ValidateAndActivateAsync()
    {
        StatusMessage = "";
        string key = (LicenseKeyInput ?? "").Trim();

        if (key.Length == 0)
        {
            StatusMessage = LocalizationManager.GetString("Settings_ActivationInvalid");
            return;
        }

        IsActivating = true;
        try
        {
            LicenseValidationResult result = await _gate.ActivateLicenseAsync(key).ConfigureAwait(false);

            // ActivationLimitReached is its own line rather than a flavour of "invalid":
                // the key is fine, this computer is simply not allowed a seat, and
                // saying otherwise would send the operator looking for a typo.
                StatusMessage = result switch
            {
                LicenseValidationResult.Valid => LocalizationManager.GetString("Settings_ActivationSuccess"),
                LicenseValidationResult.Expired => LocalizationManager.GetString("Settings_ActivationExpired"),
                LicenseValidationResult.Deactivated => LocalizationManager.GetString("Settings_ActivationDeactivated"),
                LicenseValidationResult.ActivationLimitReached => LocalizationManager.GetString("Settings_ActivationLimitReached"),
                LicenseValidationResult.Invalid => LocalizationManager.GetString("Settings_ActivationInvalid"),
                _ => LocalizationManager.GetString("Settings_ActivationError")
            };

            if (result == LicenseValidationResult.ActivationLimitReached)
            {
                // The counters from the refusal are worth showing: they are what the
                // operator has to reason about before releasing a seat elsewhere.
                Refresh();
                _requestLicenseRefresh();
                return;
            }

            if (result == LicenseValidationResult.Valid)
            {
                LicenseKeyInput = "";
                IsReleaseArmed = false;
                Refresh();
                _requestLicenseRefresh();
            }
        }
        finally
        {
            IsActivating = false;
        }
    }

    /// <summary>
    /// Hands this computer's seat back. The first press only arms the button, so
    /// an accidental click on a destructive action does not put a paying shop back
    /// on the free tier with no way to undo it from the panel.
    /// </summary>
    private async Task ReleaseSeatAsync()
    {
        if (!_gate.HasSeat)
        {
            IsReleaseArmed = false;
            return;
        }

        if (!IsReleaseArmed)
        {
            IsReleaseArmed = true;
            return;
        }

        IsReleaseArmed = false;
        IsReleasing = true;
        try
        {
            LicenseDeactivationResponse response = await _gate.DeactivateLicenseAsync();

            // The seat is released locally either way, so the panel reports what
            // happened here and not what the vendor answered: a vendor that already
            // forgot the seat still put this computer back on the free tier.
            StatusMessage = response.Deactivated
                ? LocalizationManager.GetString("Licensing_PanelReleaseDone")
                : LocalizationManager.GetString("Licensing_PanelReleaseDoneAlready");

            Refresh();
            _requestLicenseRefresh();
        }
        finally
        {
            IsReleasing = false;
        }
    }

    public void Dispose() => _client.Dispose();
}
