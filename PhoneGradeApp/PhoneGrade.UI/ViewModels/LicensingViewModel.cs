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

    public LicensingViewModel(TrialGate gate, LemonSqueezyClient client, Action requestLicenseRefresh)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _requestLicenseRefresh = requestLicenseRefresh;

        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateAndActivateAsync);
        ValidateCommand.ThrownExceptions.Subscribe(_ => { });
        OpenPricingCommand = ReactiveCommand.Create(() => PricingLink.Open(PricingLink.Url));

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

    /// <summary>Short line for the status pill and the introduction screen.</summary>
    public string TierSummary => IsPro
        ? LocalizationManager.GetString("Licensing_TierPro")
        : string.Format(LocalizationManager.GetString("Licensing_TierFree"), ScansLeft);

    public ReactiveCommand<Unit, Unit> ValidateCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPricingCommand { get; }

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

            StatusMessage = result switch
            {
                LicenseValidationResult.Valid => LocalizationManager.GetString("Settings_ActivationSuccess"),
                LicenseValidationResult.Expired => LocalizationManager.GetString("Settings_ActivationExpired"),
                LicenseValidationResult.Deactivated => LocalizationManager.GetString("Settings_ActivationDeactivated"),
                LicenseValidationResult.Invalid => LocalizationManager.GetString("Settings_ActivationInvalid"),
                _ => LocalizationManager.GetString("Settings_ActivationError")
            };

            if (result == LicenseValidationResult.Valid)
            {
                LicenseKeyInput = "";
                Refresh();
                _requestLicenseRefresh();
            }
        }
        finally
        {
            IsActivating = false;
        }
    }

    public void Dispose() => _client.Dispose();
}
