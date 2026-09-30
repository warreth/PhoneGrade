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
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private volatile TrialState _state;
    private volatile CachedValidation? _validation;

    public TrialGate(TrialStateStore store, LemonSqueezyClient? client = null, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _client = client ?? new LemonSqueezyClient();
        _clock = clock ?? TimeProvider.System;
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

            if (!string.IsNullOrWhiteSpace(state.LicenseKey))
            {
                LicenseValidationResult validation = await ValidateCachedAsync(state.LicenseKey, cancellationToken).ConfigureAwait(false);
                if (validation == LicenseValidationResult.Valid)
                {
                    IsPro = true;
                    return ScanAuthorization.AllowedPro;
                }
            }

            IsPro = false;
            if (state.ScanCount >= FreeScanLimit)
                return ScanAuthorization.LimitReached;

            _state = new TrialState
            {
                ScanCount = state.ScanCount + 1,
                LicenseKey = state.LicenseKey
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
    /// The settings screen's Validate and Activate action. Validation here is
    /// always fresh (the user explicitly asked), and only a Valid key is
    /// persisted: an expired, deactivated or wrong key changes nothing on disk
    /// and leaves any already stored key in place.
    /// </summary>
    public async Task<LicenseValidationResult> ActivateLicenseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        string candidate = licenseKey?.Trim() ?? "";
        if (candidate.Length == 0)
            return LicenseValidationResult.Invalid;

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LicenseValidationResponse response =
                await _client.ValidateDetailedAsync(candidate, cancellationToken).ConfigureAwait(false);

            if (response.Result == LicenseValidationResult.Valid)
            {
                _state = new TrialState { ScanCount = _state.ScanCount, LicenseKey = candidate };
                _store.Save(_state);
                _validation = new CachedValidation(candidate, LicenseValidationResult.Valid, _clock.GetUtcNow());
                IsPro = true;
            }
            else if (string.Equals(_state.LicenseKey, candidate, StringComparison.Ordinal))
            {
                // The stored key itself no longer validates: drop the Pro flag.
                IsPro = false;
            }
            // A failed attempt with a different key leaves the stored key and
            // whatever status it had untouched.

            return response.Result;
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

            LicenseValidationResult validation = await ValidateCachedAsync(_state.LicenseKey, cancellationToken).ConfigureAwait(false);
            IsPro = validation == LicenseValidationResult.Valid;
            return IsPro;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Cache-aware validation. Callers hold <see cref="_mutex"/>.</summary>
    private async Task<LicenseValidationResult> ValidateCachedAsync(string key, CancellationToken cancellationToken)
    {
        CachedValidation? cached = _validation;
        DateTimeOffset now = _clock.GetUtcNow();
        if (cached is { } entry &&
            string.Equals(entry.Key, key, StringComparison.Ordinal) &&
            now - entry.At < (entry.Result == LicenseValidationResult.Valid ? PositiveCacheTtl : NegativeCacheTtl))
        {
            return entry.Result;
        }

        LicenseValidationResult result = await _client.ValidateAsync(key, cancellationToken).ConfigureAwait(false);
        _validation = new CachedValidation(key, result, now);
        return result;
    }

    private sealed record CachedValidation(string Key, LicenseValidationResult Result, DateTimeOffset At);
}
