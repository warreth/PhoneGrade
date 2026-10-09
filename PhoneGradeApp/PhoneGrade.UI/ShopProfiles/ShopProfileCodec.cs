using System;
using System.Text.Json;

namespace PhoneGrade.UI.ShopProfiles;

/// <summary>
/// Reads and writes the profile file.
///
/// The parser is deliberately forgiving about everything except the schema
/// version: comments and trailing commas are allowed so a shop that keeps the
/// file in a repository or a text editor does not have to fight the format, and
/// properties this build does not know are ignored so a file from a later build
/// with more settings still imports the settings this build does carry.
///
/// What it is not forgiving about is <see cref="ShopProfile.SchemaVersion"/>.
/// A file without one is not a PhoneGrade profile at all, and a file with a
/// higher one was written by a build that knows settings this one has never
/// heard of. Importing that silently would apply half a profile and leave the
/// operator believing the other computer now matches, so it is refused with a
/// message that says to update first.
///
/// Failures are keys, not sentences: the view model says them in the operator's
/// language, and tests can assert on the key without a dictionary.
/// </summary>
public static class ShopProfileCodec
{
    /// <summary>The file is unreadable as a profile: bad JSON, no version, no object.</summary>
    public const string InvalidFileKey = "Settings_ShopProfileErrorInvalid";

    /// <summary>The file was written by a newer build than this one.</summary>
    public const string NewerVersionKey = "Settings_ShopProfileErrorNewer";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(ShopProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(profile, Options);
    }

    /// <summary>
    /// Parses a profile file. On failure <paramref name="profile"/> is null and
    /// <paramref name="errorKey"/> is one of the two key constants above.
    /// </summary>
    public static bool TryParse(string json, out ShopProfile? profile, out string? errorKey)
    {
        profile = null;
        errorKey = null;

        try
        {
            profile = JsonSerializer.Deserialize<ShopProfile>(json, Options);
        }
        catch (JsonException)
        {
            errorKey = InvalidFileKey;
            return false;
        }

        if (profile is null || profile.SchemaVersion is not { } version || version < 1)
        {
            errorKey = InvalidFileKey;
            profile = null;
            return false;
        }

        if (version > ShopProfile.CurrentSchemaVersion)
        {
            errorKey = NewerVersionKey;
            profile = null;
            return false;
        }

        return true;
    }
}
