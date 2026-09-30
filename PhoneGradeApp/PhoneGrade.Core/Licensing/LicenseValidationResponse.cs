namespace PhoneGrade.Core.Licensing;

/// <summary>
/// One Lemon Squeezy validate response: the raw fields the API returns
/// (<c>valid</c>, <c>error</c>, <c>license_key.status</c>) next to the
/// <see cref="Result"/> the app decides on. The raw fields are kept because the
/// error text is what the settings screen shows when activation fails, and the
/// status is what decides between Expired and Deactivated when
/// <c>valid</c> is false for both.
/// </summary>
public sealed record LicenseValidationResponse(bool Valid, string Error, string Status, LicenseValidationResult Result);
