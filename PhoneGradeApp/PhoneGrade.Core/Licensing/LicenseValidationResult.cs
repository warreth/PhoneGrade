namespace PhoneGrade.Core.Licensing;

/// <summary>
/// The states the app acts on after talking to the Lemon Squeezy licensing API. Everything the API can report (network failure, malformed body,
/// unknown key) collapses into <see cref="Invalid"/> so callers never have to
/// interpret transport errors: no key, no Pro tier.
/// </summary>
public enum LicenseValidationResult
{
    /// <summary>The key exists, is active and is not expired.</summary>
    Valid,

    /// <summary>The key exists but its expiry date has passed.</summary>
    Expired,

    /// <summary>The key was deactivated (or disabled) by the vendor.</summary>
    Deactivated,

    /// <summary>
    /// The key is real and healthy, but every seat it allows is already taken by
    /// another machine. Distinct from <see cref="Invalid"/> because the operator
    /// has to be told "this computer is the one over the limit", not "this key is
    /// wrong": the fix is to release a seat or activate elsewhere, and showing
    /// "invalid key" would send them looking in the wrong place.
    /// </summary>
    ActivationLimitReached,

    /// <summary>Unknown key, malformed response, or the API could not be reached.</summary>
    Invalid
}
