using System.Reactive;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Core.Licensing;
using Avalonia.Threading;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

public partial class MainWindowViewModel
{

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

    /// <summary>Shows or hides the license panel. The pill in the title bar is bound to it.</summary>
    public ReactiveCommand<Unit, Unit> ToggleLicensePanelCommand { get; }

    /// <summary>Opens the license panel and closes the settings drawer, so the two never overlap.</summary>
    public ReactiveCommand<Unit, Unit> ManageLicenseCommand { get; }

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
    /// Asks the vendor once, at startup, what the stored key is worth.
    /// </summary>
    /// <remarks>
    /// The gate was only consulted when a scan started, so the pill and the
    /// settings card said "Free tier" on every launch of a machine that had a
    /// valid seat, and the operator found out it was still Pro only by starting
    /// a scan. One check answers that before anything is touched. A failure
    /// leaves the stored state as it is and the first scan asks again, and a
    /// machine with no key costs no call at all.
    /// </remarks>
    private async Task RefreshLicenseOnStartupAsync()
    {
        try
        {
            await _trialGate.RefreshLicenseStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.Desktop,
                "The licence check at startup failed; the stored state stays.", ex.Message);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(RefreshLicensingState);
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

}
