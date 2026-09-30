using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core.Licensing;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// View model for the "Licensing & Subscription" card inside the settings drawer.
/// </summary>
public class SettingsViewModel : ReactiveObject, IDisposable
{
    private readonly TrialGate _gate;
    private readonly LemonSqueezyClient _client;
    private readonly Action _requestLicenseRefresh;
    private string _licenseKeyInput = "";
    private string _statusMessage = "";

    public SettingsViewModel(TrialGate gate, LemonSqueezyClient client, Action requestLicenseRefresh)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _requestLicenseRefresh = requestLicenseRefresh;

        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateAndActivateAsync);
        ValidateCommand.ThrownExceptions.Subscribe(_ => { });

        // Initial status reflects whatever the gate currently holds.
        UpdateStatusText();
    }

    public string LicenseKeyInput
    {
        get => _licenseKeyInput;
        set => this.RaiseAndSetIfChanged(ref _licenseKeyInput, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public string LicensingStatusText =>
        _gate.IsPro
            ? LocalizationManager.GetString("Settings_ProTierStatus")
            : string.Format(LocalizationManager.GetString("Settings_FreeTierStatus"), _gate.ScanCount);

    public ReactiveCommand<Unit, Unit> ValidateCommand { get; }

    private async Task ValidateAndActivateAsync()
    {
        StatusMessage = "";
        string key = (LicenseKeyInput ?? "").Trim();

        if (key.Length == 0)
        {
            StatusMessage = LocalizationManager.GetString("Settings_ActivationInvalid");
            return;
        }

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
            this.RaisePropertyChanged(nameof(LicensingStatusText));
            _requestLicenseRefresh();
        }
    }

    public void UpdateStatusText() => this.RaisePropertyChanged(nameof(LicensingStatusText));

    public void Dispose() => _client.Dispose();
}