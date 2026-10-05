namespace PhoneGrade.Core.Licensing;

/// <summary>
/// One Lemon Squeezy validate response: the raw fields the API returns
/// (<c>valid</c>, <c>error</c>, <c>license_key.status</c>, <c>meta.product_id</c>,
/// <c>license_key.activation_limit</c>, <c>license_key.activation_usage</c> and the
/// <c>instance</c> when one was asked about) next to the <see cref="Result"/> the
/// app decides on.
///
/// The raw fields are kept because the error text is what the settings screen shows
/// when activation fails, and the status is what decides between Expired and
/// Deactivated when <c>valid</c> is false for both. <see cref="ProductId"/> is the
/// product the key was sold for, which is what tells a PhoneGrade key apart from a
/// key that is perfectly valid but belongs to somebody else's product.
///
/// The two activation counters are the vendor's numbers and are shown as they
/// arrive. The app holds no table of what a tier is allowed to have, so a tier
/// ladder becomes a change in the Lemon Squeezy dashboard rather than a release.
///
/// <paramref name="InstanceId"/> is only filled in when the request named an
/// instance. Validating a bare key answers "is this key good", which is not the
/// same question as "is this machine one of the seats on this key", and the gate
/// asks the second one from the moment a seat exists.
/// </summary>
public sealed record LicenseValidationResponse(
    bool Valid,
    string Error,
    string Status,
    LicenseValidationResult Result,
    long ProductId = 0,
    int ActivationLimit = 0,
    int ActivationUsage = 0,
    string InstanceId = "",
    string InstanceName = "");

/// <summary>
/// One Lemon Squeezy activate response.
///
/// Activate is the call that turns a key into a seat on a machine, and it answers
/// with the instance it created rather than with a bare yes. The instance id is
/// the handle for everything that follows: validating on the next startup names it,
/// and deactivating hands it back.
///
/// <see cref="Result"/> collapses the two ways activation can fail into one enum
/// because both of them mean "the app is not Pro right now", but they are told
/// apart by name on screen, so <see cref="LicenseValidationResult.ActivationLimitReached"/>
/// is a state of its own rather than a flavour of
/// <see cref="LicenseValidationResult.Invalid"/>.
/// </summary>
public sealed record LicenseActivationResponse(
    bool Activated,
    string Error,
    string InstanceId,
    string InstanceName,
    LicenseValidationResult Result,
    int ActivationLimit = 0,
    int ActivationUsage = 0,
    long ProductId = 0);

/// <summary>
/// One Lemon Squeezy deactivate response.
///
/// Deactivation is the seat being handed back so another machine can take it, which
/// is what stops a shop that replaces a bench from slowly running out of seats.
/// <see cref="Result"/> is <see cref="LicenseValidationResult.Valid"/> when the
/// seat was released and <see cref="LicenseValidationResult.Invalid"/> when
/// nothing was: an instance the vendor no longer knows about needs no release, but
/// the app still has to forget it, and the gate does that either way.
/// </summary>
public sealed record LicenseDeactivationResponse(
    bool Deactivated,
    string Error,
    LicenseValidationResult Result);