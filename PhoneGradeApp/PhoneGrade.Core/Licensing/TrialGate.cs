namespace PhoneGrade.Core.Licensing;

/// <summary>
/// Decides whether a scan may run, and consumes free scans when it does.
///
/// Rules:
/// - A stored license key is validated against Lemon Squeezy first. While the
///   result is Valid the scan is free of charge and the count is not touched.
/// - Without a valid license the count is checked against the free limit: below
///   it the scan is allowed and the incremented state is persisted to both
///   storage locations before the scan starts, at or above it the scan is
///   blocked.
///
/// Validation results are cached in memory so a burst of scans (start, retest,
/// auto-start) does not turn into a burst of API calls: a valid result is
/// trusted for <see cref="PositiveCacheTtl"/>, any other result for the shorter
/// <see cref="NegativeCacheTtl"/> so a key that was just fixed, or a network
/// that just came back, is rechecked within a minute.
///
/// The state reference is swapped, never mutated, so the UI thread can read
/// <see cref="ScanCount"/> and <see cref="IsPro"/> while a scan is being
/// authorized without observing a half-updated value.
/// </summary>
public sealed class TrialGate
{
    /// <summary>Number of scans the free tier includes.</summary>
    public const int FreeScanLimit = 10;

    /// <summary>How long a Valid result is trusted without calling the API.</summary>
    public static readonly TimeSpan PositiveCacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>How long a failed or unreachable validation is trusted before retrying.</summary>
    public static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(1);

    private readonly TrialStateStore _store;
    private readonly LemonSqueezyClient _client;
    private readonly TimeProvider _clock;
    private readonly MachineFingerprint _fingerprint;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private volatile TrialState _state;
    private volatile CachedValidation? _validation;
    private volatile string[] _machineNames = [];

    public TrialGate(
        TrialStateStore store,
        LemonSqueezyClient? client = null,
        TimeProvider? clock = null,
        MachineFingerprint? fingerprint = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _client = client ?? new LemonSqueezyClient();
        _clock = clock ?? TimeProvider.System;
        // The fingerprint is a parameter so a test can hand over a machine that
        // cannot be identified. Production passes nothing and reads the real one.
        _fingerprint = fingerprint ?? MachineFingerprint.Current;
        // Loading reconciles the two storage locations, so the high-water mark
        // and the self repair have already run by the time anyone asks to scan.
        _state = store.Load();
    }

    /// <summary>Scans consumed on the free tier so far.</summary>
    public int ScanCount => _state.ScanCount;

    /// <summary>The stored license key, empty when never activated.</summary>
    public string LicenseKey => _state.LicenseKey;

    /// <summary>Last known validity of <see cref="LicenseKey"/>. Refreshed by evaluations and activations.</summary>
    public bool IsPro { get; private set; }

    /// <summary>
    /// Machines currently holding a seat on <see cref="LicenseKey"/>, as the
    /// vendor reports them. Zero when the last answer did not say, which is the
    /// free tier rather than a plan that allows nothing.
    /// </summary>
    public int MachineCount { get; private set; }

    /// <summary>
    /// How many machines the key's plan allows. Lemon Squeezy owns this number and
    /// it arrives with every answer; nothing in the app compares it to a value
    /// baked into the binary, so a tier ladder is a dashboard change.
    /// </summary>
    public int MachineLimit { get; private set; }

    /// <summary>
    /// The seats this app has actually seen, named. Lemon Squeezy answers a
    /// validate or an activate with one instance rather than the key's whole seat
    /// list, so this is one name in practice: the seat that was claimed here, or
    /// the one that came back when the key was checked. It is what the panel marks
    /// as this computer, and it is deliberately not a list of all seats, because
    /// nothing the API returns can be one.
    /// </summary>
    public IReadOnlyList<string> MachineNames => Array.AsReadOnly(_machineNames);

    /// <summary>True when this computer holds a seat on the stored key.</summary>
    public bool HasSeat => _state.InstanceId.Length > 0;

    /// <summary>
    /// The Lemon Squeezy instance id stored for this computer, empty when it holds
    /// no seat. Read by the panel to explain which seat the Release button gives
    /// back, and written on disk as soon as activate succeeds.
    /// </summary>
    public string InstanceId => _state.InstanceId;

    /// <summary>
    /// This machine's identity, empty when the operating system would not say. The
    /// panel reads it to mark which seat is this computer. Named Value rather than
    /// MachineFingerprint so it cannot shadow the type of the same name.
    /// </summary>
    public string MachineFingerprintValue => _fingerprint.Value;

    /// <summary>True while this machine can be identified and could hold a seat.</summary>
    public bool CanIdentifyMachine => _fingerprint.IsAvailable;

    /// <summary>
    /// The scan initializer's decision: validates when a key is present, allows
    /// and counts a free scan while the limit is not reached, blocks at the
    /// limit. Counting happens before the scan runs and is written to both
    /// storage locations immediately, so a crash mid-scan still spent the scan.
    /// </summary>
    public async Task<ScanAuthorization> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrialState state = _state;

            // Pro is a seat on this machine, not merely a key that is valid somewhere. A
            // key without a stored instance is one the operator deliberately
            // released, or one this machine never claimed, and neither may scan for
            // free: otherwise the Release seat button would only hide the seat
            // while leaving unlimited scanning behind. A key with no seat therefore
            // costs no API call either, because there is no instance to ask about.
            if (state.InstanceId.Length > 0 &&
                await ValidateCachedAsync(state.LicenseKey, cancellationToken).ConfigureAwait(false)
                    == LicenseValidationResult.Valid)
            {
                IsPro = true;
                return ScanAuthorization.AllowedPro;
            }

            IsPro = false;
            if (state.ScanCount >= FreeScanLimit)
                return ScanAuthorization.LimitReached;

            // Every field is carried over, not just the two the count touches.
            // Writing a fresh state with only ScanCount and LicenseKey would drop
            // the instance id on the first free scan after a seat was lost, and the
            // Release seat button would then have nothing to release and a
            // re-activation would take a second seat.
            _state = new TrialState
            {
                ScanCount = state.ScanCount + 1,
                LicenseKey = state.LicenseKey,
                InstanceId = state.InstanceId,
                MachineFingerprint = state.MachineFingerprint
            };
            _store.Save(_state);
            return ScanAuthorization.AllowedTrial;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// The settings screen's Validate and Activate action.
    ///
    /// Three calls at most, and the order is the whole point:
    ///
    /// 1. Validate with the stored instance. If this computer already holds a seat
    ///    on the stored key, that is the answer and no activate call is made. A user
    ///    who re-pastes a key they already activated here gets their tier back
    ///    instead of a second seat burned.
    /// 2. Validate the bare key. A key that is not ours, expired or deactivated
    ///    stops here, writes nothing, and leaves any stored key and its seat alone.
    /// 3. Activate, and persist the instance id the vendor handed back before this
    ///    method returns. A crash between the vendor's call and the write would
    ///    leave a seat taken with nothing on disk to release it from, which is
    ///    exactly the leak this button exists to prevent.
    ///
    /// A machine that cannot be identified never gets past step 2: activating with
    /// an empty instance name would spend a seat that cannot be validated, released
    /// or explained afterwards. It stays on the free tier, which needs no identity.
    /// </summary>
    public async Task<LicenseValidationResult> ActivateLicenseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        string candidate = licenseKey?.Trim() ?? "";
        if (candidate.Length == 0)
            return LicenseValidationResult.Invalid;

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_state.LicenseKey, candidate, StringComparison.Ordinal) &&
                _state.InstanceId.Length > 0)
            {
                LicenseValidationResponse held = await _client
                    .ValidateDetailedAsync(candidate, _state.InstanceId, cancellationToken)
                    .ConfigureAwait(false);

                if (held.Result == LicenseValidationResult.Valid)
                {
                    RememberSeats(held);
                    IsPro = true;
                    return LicenseValidationResult.Valid;
                }
                // The seat is gone (released from another machine, key reset). Fall
                // through and try to claim one again rather than leaving the app
                // convinced it is Pro.
            }

            LicenseValidationResponse response =
                await _client.ValidateDetailedAsync(candidate, cancellationToken).ConfigureAwait(false);

            if (response.Result != LicenseValidationResult.Valid)
            {
                if (string.Equals(_state.LicenseKey, candidate, StringComparison.Ordinal))
                    IsPro = false; // the stored key itself no longer validates
                // A failed attempt with a different key leaves the stored key,
                // its seat and its status untouched.
                return response.Result;
            }

            // The key is ours, healthy and active. Whatever seat the vendor hands
            // out now belongs to this candidate, so it is written first and the
            // stored key follows it. Storing the key before the seat would leave a
            // window where a crash loses the instance id, and the next launch would
            // claim a second seat for the same machine.
            LicenseValidationResult claimed = await ClaimSeatAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (claimed == LicenseValidationResult.Valid && !string.Equals(_state.LicenseKey, candidate, StringComparison.Ordinal))
                ReplaceStoredKey(candidate);
            return claimed;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Claims one seat on <paramref name="licenseKey"/> for this machine and writes
    /// it down before returning. Shared by the Validate and Activate button and by
    /// the startup path, because both end in the same two steps and both have to
    /// persist the instance id first.
    ///
    /// Nothing is written when the vendor refuses: at the seat limit, on a key for
    /// another product, or on a machine that cannot be identified at all.
    /// Callers hold <see cref="_mutex"/>.
    /// </summary>
    private async Task<LicenseValidationResult> ClaimSeatAsync(
        string licenseKey,
        CancellationToken cancellationToken)
    {
        if (!_fingerprint.IsAvailable)
        {
            // A real key on a machine with no identity. The free tier still works,
            // and nothing is written: activating with an empty instance name would
            // take a seat that can never be validated or released again, and would
            // let the limit be bypassed by breaking the fingerprint read.
            return LicenseValidationResult.Invalid;
        }

        LicenseActivationResponse activation =
            await _client.ActivateAsync(licenseKey, _fingerprint.Value, cancellationToken).ConfigureAwait(false);

        if (activation.Result != LicenseValidationResult.Valid || activation.InstanceId.Length == 0)
        {
            // A refused activation can still have taken a seat: one that succeeded
            // on the server but failed the product check is exactly that. It is
            // handed back rather than left dangling on the key, because nothing
            // else in the app will ever hold its instance id.
            if (activation.Activated && activation.InstanceId.Length > 0)
            {
                await _client.DeactivateAsync(licenseKey, activation.InstanceId, cancellationToken)
                    .ConfigureAwait(false);
            }

            RememberSeats(activation.ActivationLimit, activation.ActivationUsage, "");
            return activation.Result;
        }

        // The seat is written before the key is trusted as Pro anywhere, and before
        // this method returns, so a crash cannot leave a seat taken with no handle
        // on disk to release it from.
        _state = new TrialState
        {
            ScanCount = _state.ScanCount,
            LicenseKey = licenseKey,
            InstanceId = activation.InstanceId,
            MachineFingerprint = _fingerprint.Value
        };
        _store.Save(_state);

        string seatName = activation.InstanceName.Length > 0
            ? activation.InstanceName
            : _fingerprint.Value;

        _validation = new CachedValidation(
            licenseKey,
            LicenseValidationResult.Valid,
            _clock.GetUtcNow(),
            "active",
            activation.ActivationLimit,
            activation.ActivationUsage,
            activation.InstanceId,
            seatName,
            activation.InstanceId);
        RememberSeats(activation.ActivationLimit, activation.ActivationUsage, seatName);
        IsPro = true;
        return LicenseValidationResult.Valid;
    }

    /// <summary>
    /// Swaps the stored key for one the vendor has just confirmed, keeping the seat
    /// that was just claimed and the scan count. Used when an operator pastes a
    /// different valid key over one that is already stored.
    ///
    /// The seat is already on disk when this runs, because a seat belongs to the
    /// machine and a key does not: two keys on one machine is one seat. The cache
    /// entry is rebuilt from what the seat wrote, so the next scan does not ask
    /// the vendor again about a key it has just confirmed.
    /// </summary>
    private void ReplaceStoredKey(string licenseKey)
    {
        _state = new TrialState
        {
            ScanCount = _state.ScanCount,
            LicenseKey = licenseKey,
            InstanceId = _state.InstanceId,
            MachineFingerprint = _state.MachineFingerprint
        };
        _store.Save(_state);

        _validation = _validation is { } cached
            ? cached with { Key = licenseKey }
            : new CachedValidation(licenseKey, LicenseValidationResult.Valid, _clock.GetUtcNow());
    }

    /// <summary>
    /// Hands this computer's seat back so another machine can take it, and drops
    /// to the free tier.
    ///
    /// A shop that replaces a bench, swaps a drive or reinstalls Windows has to be
    /// able to free the old seat without emailing anyone; without this the seats
    /// leak one machine at a time until a key is exhausted. The stored key is kept,
    /// so the operator can activate again on the new machine with the same key.
    ///
    /// The instance id is cleared even when the vendor reports the seat was already
    /// gone, because the local half of the seat is the part the app controls: a
    /// seat the vendor has forgotten still has to stop being validated here.
    /// </summary>
    public async Task<LicenseDeactivationResponse> DeactivateLicenseAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.LicenseKey.Length == 0 || _state.InstanceId.Length == 0)
                return new LicenseDeactivationResponse(false, "", LicenseValidationResult.Invalid);

            LicenseDeactivationResponse response = await _client
                .DeactivateAsync(_state.LicenseKey, _state.InstanceId, cancellationToken)
                .ConfigureAwait(false);

            _state = new TrialState
            {
                ScanCount = _state.ScanCount,
                LicenseKey = _state.LicenseKey,
                InstanceId = "",
                MachineFingerprint = _state.MachineFingerprint
            };
            _store.Save(_state);
            _validation = null;
            _machineNames = [];
            MachineCount = 0;
            IsPro = false;

            return response;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Revalidates the stored key, used for the background check at startup.</summary>
    public async Task<bool> RefreshLicenseStatusAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(_state.LicenseKey))
            {
                IsPro = false;
                return false;
            }

            // A stored key with a seat: ask about that seat and nothing else.
            if (_state.InstanceId.Length > 0)
            {
                LicenseValidationResponse held = await ValidateDetailedCachedAsync(
                    _state.LicenseKey, _state.InstanceId, cancellationToken).ConfigureAwait(false);

                IsPro = held.Result == LicenseValidationResult.Valid;
                if (!IsPro)
                {
                    // The seat is not usable any more, so stop reporting seats. The
                    // stored instance id stays: it is what the operator releases
                    // with the Release seat button, and dropping it here would turn
                    // a re-typed key into a fresh activate that burns a second seat.
                    MachineCount = 0;
                    MachineLimit = held.ActivationLimit;
                    _machineNames = [];
                }
                return IsPro;
            }

            // A stored key with no seat. No activate happens here: this path only
            // reports. The key was validated, but a machine with no seat is on the
            // free tier until the operator presses Validate and Activate, which is
            // where a seat is claimed.
            //
            // Claiming one automatically would look like a courtesy and is not. The
            // operator pressed nothing, and a seat that appears because the app
            // started is a seat a shop cannot see going. For a key at its limit that
            // refusal would surface here as a bare failure with no sentence, which
            // is exactly what the panel's own message is for.
            LicenseValidationResponse bare = await ValidateDetailedCachedAsync(
                _state.LicenseKey, null, cancellationToken).ConfigureAwait(false);

            IsPro = false;
            MachineLimit = bare.ActivationLimit;
            MachineCount = 0;
            _machineNames = [];
            return false;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Cache-aware validation for the scan path, which only needs the verdict.
    /// Names the stored instance when there is one, so a scan on a computer whose
    /// seat was taken away is refused rather than quietly allowed on the strength
    /// of the key alone.
    /// </summary>
    private async Task<LicenseValidationResult> ValidateCachedAsync(string key, CancellationToken cancellationToken)
    {
        LicenseValidationResponse response = await ValidateDetailedCachedAsync(
            key, _state.InstanceId, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }

    /// <summary>
    /// Cache-aware validation that keeps the seat counters, because the startup
    /// path is what fills the panel. The cache entry holds the counters as well as
    /// the verdict, so a cached answer can still be shown without another call.
    /// Callers hold <see cref="_mutex"/>.
    /// </summary>
    private async Task<LicenseValidationResponse> ValidateDetailedCachedAsync(
        string key,
        string? instanceId,
        CancellationToken cancellationToken)
    {
        CachedValidation? cached = _validation;
        DateTimeOffset now = _clock.GetUtcNow();
        bool fresh = cached is { } entry &&
            string.Equals(entry.Key, key, StringComparison.Ordinal) &&
            string.Equals(entry.AskedForInstanceId, instanceId ?? "", StringComparison.Ordinal) &&
            now - entry.At < (entry.Result == LicenseValidationResult.Valid ? PositiveCacheTtl : NegativeCacheTtl);

        if (fresh && cached is not null)
        {
            RememberSeats(cached!.ActivationLimit, cached.ActivationUsage, cached.InstanceName);
            return new LicenseValidationResponse(
                cached.Result == LicenseValidationResult.Valid,
                "",
                cached.Status,
                cached.Result,
                LemonSqueezyClient.PhoneGradeProductId,
                cached.ActivationLimit,
                cached.ActivationUsage,
                cached.ReturnedInstanceId,
                cached.InstanceName);
        }

        LicenseValidationResponse response =
            await _client.ValidateDetailedAsync(key, instanceId, cancellationToken).ConfigureAwait(false);

        // AskedForInstanceId is what the request named, InstanceId is what came
        // back. Keeping them apart matters: a validate against a seat the vendor
        // has forgotten answers with a valid key and a different (or absent)
        // instance, and comparing the wrong one of the two would make the cache miss
        // on every scan and turn a burst of scans into a burst of API calls.
        _validation = new CachedValidation(
            key,
            response.Result,
            now,
            response.Status,
            response.ActivationLimit,
            response.ActivationUsage,
            instanceId ?? "",
            response.InstanceName,
            response.InstanceId);
        RememberSeats(response);
        return response;
    }

    /// <summary>Takes the seat counters off a validate or activate answer.</summary>
    private void RememberSeats(LicenseValidationResponse response) =>
        RememberSeats(
            response.ActivationLimit,
            response.ActivationUsage,
            response.InstanceName);

    /// <summary>
    /// Records what the vendor says about the seats. The instance name is the
    /// fingerprint that was sent, so the panel can mark which entry is this
    /// computer without asking the operator to recognise a UUID.
    /// </summary>
    private void RememberSeats(int limit, int usage, string instanceName)
    {
        MachineLimit = limit;
        MachineCount = usage;
        _machineNames = instanceName.Length > 0 ? [instanceName] : [];
    }

    private sealed record CachedValidation(
        string Key,
        LicenseValidationResult Result,
        DateTimeOffset At,
        string Status = "",
        int ActivationLimit = 0,
        int ActivationUsage = 0,
        string AskedForInstanceId = "",
        string InstanceName = "",
        string ReturnedInstanceId = "");
}
