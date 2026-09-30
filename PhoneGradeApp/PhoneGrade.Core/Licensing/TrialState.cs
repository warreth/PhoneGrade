using System.Text.Json;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// The payload that travels inside the encrypted token: how many free scans have
/// been used and which license key, if any, the operator entered. Serialized as
/// plain JSON before encryption so the cipher stays a self-contained step that
/// can be tested on its own.
/// </summary>
public sealed class TrialState
{
    /// <summary>Number of scans consumed on the free tier (0 up to the ten scan limit).</summary>
    public int ScanCount { get; set; }

    /// <summary>The Lemon Squeezy license key as entered, empty when the operator never activated.</summary>
    public string LicenseKey { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Parses a decrypted payload. Returns null instead of throwing so a payload
    /// that decrypts but does not decode (wrong shape, truncated) is treated the
    /// same as a missing one and the other storage location takes over.
    /// </summary>
    public static TrialState? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            TrialState? state = JsonSerializer.Deserialize<TrialState>(json);
            // A negative count cannot come from this app; treat it as corruption.
            if (state is null || state.ScanCount < 0) return null;
            state.LicenseKey ??= "";
            return state;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
