namespace PhoneGrade.Core.Licensing;

/// <summary>
/// The four states the app acts on after talking to the Lemon Squeezy validate
/// endpoint. Everything the API can report (network failure, malformed body,
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

    /// <summary>Unknown key, malformed response, or the API could not be reached.</summary>
    Invalid
}
