using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// Persists the encrypted trial payload in two locations and reconciles them on
/// every load.
///
/// Location 1 (primary): the Base64 token embedded in the app's settings.json
/// under the TrialToken field, next to the ordinary settings the operator can
/// edit. Location 2 (backup): sys_cache.dat in LocalApplicationData, outside the
/// settings file so deleting or hand-editing one location does not reset the
/// scan count.
///
/// Reconciliation follows the high-water mark: the highest ScanCount of the two
/// locations wins, because the only direction the legitimate count ever moves is
/// up. Whichever side is missing, corrupt or behind is rewritten from the merged
/// result during the same load, so a deleted backup file or a gutted
/// settings.json repairs itself at startup instead of on the next scan.
///
/// Every read and write failure is swallowed: a locked file or a read-only
/// directory must never stop the app from starting or from scanning, and one
/// failing location must never prevent the other from being written.
/// </summary>
public sealed class TrialStateStore
{
    /// <summary>File name of the backup location inside LocalApplicationData\PhoneGrade.</summary>
    public const string BackupFileName = "sys_cache.dat";

    /// <summary>The field the token travels under inside settings.json.</summary>
    public const string TokenProperty = "TrialToken";

    private const string SettingsFileName = "settings.json";

    private readonly Func<string> _readToken;
    private readonly Action<string> _writeToken;
    private readonly string _backupFilePath;

    /// <summary>
    /// <paramref name="readToken"/> returns the TrialToken currently stored in
    /// settings.json (empty when absent), <paramref name="writeToken"/> persists
    /// it there, and <paramref name="backupFilePath"/> is the full path of
    /// sys_cache.dat. The delegates let the UI bind location 1 to its live
    /// AppSettings instance so a later settings save cannot carry a stale token.
    /// </summary>
    public TrialStateStore(Func<string> readToken, Action<string> writeToken, string backupFilePath)
    {
        _readToken = readToken ?? throw new ArgumentNullException(nameof(readToken));
        _writeToken = writeToken ?? throw new ArgumentNullException(nameof(writeToken));
        _backupFilePath = backupFilePath ?? throw new ArgumentNullException(nameof(backupFilePath));
    }

    public string BackupFilePath => _backupFilePath;

    /// <summary>Directory of settings.json: LocalApplicationData\PhoneGrade, or the test override.</summary>
    public static string DefaultSettingsDirectory =>
        Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR") is { Length: > 0 } overrideDirectory
            ? overrideDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneGrade");

    /// <summary>
    /// The backup location: sys_cache.dat next to settings.json, which in
    /// production is directly in LocalApplicationData\PhoneGrade. Following the
    /// settings directory (including its AUTODYMO_SETTINGS_DIR override) keeps
    /// both locations together wherever the settings file is pointed, so tests
    /// that redirect settings never read or overwrite a real profile.
    /// </summary>
    public static string DefaultBackupFilePath =>
        Path.Combine(DefaultSettingsDirectory, BackupFileName);

    /// <summary>
    /// Store wired to the real files: TrialToken inside settings.json (read as
    /// JSON so the operator's other settings survive a token write) and
    /// sys_cache.dat next to it. Paths are parameters so tests never touch the
    /// real profile.
    /// </summary>
    public static TrialStateStore CreateDefault(string? settingsFilePath = null, string? backupFilePath = null)
    {
        string settingsPath = settingsFilePath ?? Path.Combine(DefaultSettingsDirectory, SettingsFileName);
        return new TrialStateStore(
            () => ReadTokenFromSettingsFile(settingsPath),
            token => WriteTokenToSettingsFile(settingsPath, token),
            backupFilePath ?? DefaultBackupFilePath);
    }

    /// <summary>Encrypts <paramref name="state"/> and writes it to both locations.</summary>
    public void Save(TrialState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string token = TrialStateCipher.Encrypt(state);
        TryWriteToken(token);
        TryWriteBackup(state);
    }

    /// <summary>
    /// Reads both locations, decrypts both, takes the highest ScanCount (and a
    /// non-empty license key when the counts tie), then rewrites whichever side
    /// does not already hold the merged result. A fresh install simply creates
    /// both files with a zero count.
    /// </summary>
    public TrialState Load()
    {
        TrialState? fromSettings = ReadSettingsSide();
        TrialState? fromBackup = ReadBackupSide();

        TrialState merged = (fromSettings, fromBackup) switch
        {
            (null, null) => new TrialState(),
            (var settings, null) => settings!,
            (null, var backup) => backup!,
            (var settings, var backup) => Merge(settings, backup)
        };

        if (!Matches(fromSettings, merged))
            TryWriteToken(TrialStateCipher.Encrypt(merged));
        if (!Matches(fromBackup, merged))
            TryWriteBackup(merged);

        return merged;
    }

    private static TrialState Merge(TrialState settings, TrialState backup) => new()
    {
        // High-water mark: the count only ever grows, so the lower reading is
        // the stale one no matter which file survived a delete or an edit.
        ScanCount = Math.Max(settings.ScanCount, backup.ScanCount),
        LicenseKey = !string.IsNullOrEmpty(settings.LicenseKey) ? settings.LicenseKey : backup.LicenseKey
    };

    private static bool Matches(TrialState? stored, TrialState merged) =>
        stored is not null &&
        stored.ScanCount == merged.ScanCount &&
        string.Equals(stored.LicenseKey, merged.LicenseKey, StringComparison.Ordinal);

    private TrialState? ReadSettingsSide()
    {
        try
        {
            return TrialStateCipher.TryDecryptState(_readToken());
        }
        catch (Exception)
        {
            return null; // unreadable settings behave like deleted settings
        }
    }

    private TrialState? ReadBackupSide()
    {
        try
        {
            if (!File.Exists(_backupFilePath)) return null;
            return TrialStateCipher.TryDecryptState(File.ReadAllText(_backupFilePath));
        }
        catch (Exception)
        {
            return null; // missing, locked or garbage backup behaves like deleted
        }
    }

    private bool TryWriteToken(string token)
    {
        try
        {
            _writeToken(token);
            return true;
        }
        catch (Exception)
        {
            return false; // settings location unavailable; the backup still gets its turn
        }
    }

    private bool TryWriteBackup(TrialState state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_backupFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_backupFilePath, TrialStateCipher.Encrypt(state));
            return true;
        }
        catch (Exception)
        {
            return false; // IOException, UnauthorizedAccessException, path too long, ...
        }
    }

    private static string ReadTokenFromSettingsFile(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath)) return "";
        JsonNode? document = JsonNode.Parse(File.ReadAllText(settingsFilePath));
        return document?[TokenProperty]?.GetValue<string>() ?? "";
    }

    private static void WriteTokenToSettingsFile(string settingsFilePath, string token)
    {
        JsonObject settings = ReadSettingsObjectOrRebuild(settingsFilePath);
        settings[TokenProperty] = token;
        string? directory = Path.GetDirectoryName(settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(settingsFilePath,
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Loads the settings document the token is written into, preserving the
    /// operator's other settings. Only a file that is genuinely unparseable is
    /// rebuilt (it already loads as all defaults everywhere else); a file that
    /// merely cannot be read right now (locked, access denied) propagates, so
    /// TryWriteToken skips the write instead of replacing real settings.
    /// </summary>
    private static JsonObject ReadSettingsObjectOrRebuild(string settingsFilePath)
    {
        string json;
        try
        {
            json = File.ReadAllText(settingsFilePath);
        }
        catch (FileNotFoundException)
        {
            return new JsonObject(); // first write
        }
        catch (DirectoryNotFoundException)
        {
            return new JsonObject(); // first write into a missing folder
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject(); // corrupt settings file: rebuild around the token
        }
    }
}
