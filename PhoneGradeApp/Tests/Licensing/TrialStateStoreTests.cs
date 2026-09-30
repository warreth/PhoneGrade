using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhoneGrade.Core.Licensing;
using PhoneGrade.UI.Models;
using Xunit;

namespace Tests;

/// <summary>
/// Covers the dual location store: high-water mark reconciliation, the repair of
/// a deleted or edited location from the other one, and graceful behaviour when
/// a write is impossible. Every test runs against temp files, never the real
/// profile.
/// </summary>
public class TrialStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"trialstore-{Guid.NewGuid():N}");
    private readonly string _settingsPath;
    private readonly string _backupPath;

    public TrialStateStoreTests()
    {
        _settingsPath = Path.Combine(_dir, "settings.json");
        _backupPath = Path.Combine(_dir, "PhoneGrade", "sys_cache.dat");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch { /* temp cleanup never fails a test */ }
    }

    // ---- Save -------------------------------------------------------------------

    [Fact]
    public void Save_WritesEncryptedTokenToBothLocations_AndKeepsOtherSettings()
    {
        SeedSettings(new TrialState { ScanCount = 1, LicenseKey = "OLD" }); // pre-existing settings file
        var store = CreateStore();
        var state = new TrialState { ScanCount = 4, LicenseKey = "KEY-4" };

        store.Save(state);

        JsonNode? settings = JsonNode.Parse(File.ReadAllText(_settingsPath));
        Assert.Equal("Dark", settings?["Theme"]?.GetValue<string>()); // other settings survive
        Assert.Equal(4, TrialStateCipher.TryDecryptState(settings?["TrialToken"]?.GetValue<string>())!.ScanCount);
        Assert.Equal("KEY-4", TrialStateCipher.TryDecryptState(File.ReadAllText(_backupPath))!.LicenseKey);
    }

    [Fact]
    public void Save_WhenBackupDirectoryCannotBeCreated_WritesSettingsAndDoesNotThrow()
    {
        // A plain file where a directory has to go: Directory.CreateDirectory throws.
        string blocked = Path.Combine(_dir, "blocked-by-file");
        File.WriteAllText(blocked, "not a directory");
        var store = new TrialStateStore(
            () => ReadSettingsToken() ?? "",
            token => WriteSettingsToken(token),
            Path.Combine(blocked, "sys_cache.dat"));

        store.Save(new TrialState { ScanCount = 6 }); // must not throw

        Assert.Equal(6, ReadSettingsState()!.ScanCount);
        Assert.False(File.Exists(Path.Combine(blocked, "sys_cache.dat")));
    }

    [Fact]
    public void Save_WhenSettingsLocationIsUnwritable_WritesBackupAndDoesNotThrow()
    {
        // The settings delegate itself fails, as a read-only profile would.
        var store = new TrialStateStore(
            () => throw new UnauthorizedAccessException("blocked"),
            token => throw new UnauthorizedAccessException("blocked"),
            _backupPath);

        store.Save(new TrialState { ScanCount = 6, LicenseKey = "KEY" }); // must not throw

        Assert.Equal(6, ReadBackupState()!.ScanCount);
    }

    // ---- Load: fresh install and high-water mark --------------------------------

    [Fact]
    public void Load_FreshInstall_CreatesBothLocationsAtZero()
    {
        var store = CreateStore();

        TrialState state = store.Load();

        Assert.Equal(0, state.ScanCount);
        Assert.Equal("", state.LicenseKey);
        Assert.Equal(0, ReadSettingsState()!.ScanCount);   // primary created
        Assert.Equal(0, ReadBackupState()!.ScanCount);     // backup created
    }

    [Fact]
    public void Load_BackupHigher_KeepsBackup_AndRepairsSettings()
    {
        SeedSettings(new TrialState { ScanCount = 3 });
        SeedBackup(new TrialState { ScanCount = 8 });

        TrialState state = CreateStore().Load();

        Assert.Equal(8, state.ScanCount);   // high-water mark wins
        Assert.Equal(8, ReadSettingsState()!.ScanCount); // repaired immediately
    }

    [Fact]
    public void Load_SettingsHigher_KeepsSettings_AndRepairsBackup()
    {
        SeedSettings(new TrialState { ScanCount = 8 });
        SeedBackup(new TrialState { ScanCount = 3 });

        TrialState state = CreateStore().Load();

        Assert.Equal(8, state.ScanCount);
        Assert.Equal(8, ReadBackupState()!.ScanCount); // backup repaired immediately
    }

    // ---- Load: deletion repair --------------------------------------------------

    [Fact]
    public void Load_SettingsFileDeleted_RestoresFromBackupIntoSettings()
    {
        SeedBackup(new TrialState { ScanCount = 7, LicenseKey = "KEY-7" });

        TrialState state = CreateStore().Load();

        Assert.Equal(7, state.ScanCount);
        Assert.Equal("KEY-7", state.LicenseKey);
        TrialState? repairedSettings = ReadSettingsState();
        Assert.NotNull(repairedSettings);
        Assert.Equal(7, repairedSettings!.ScanCount);
        Assert.Equal("KEY-7", repairedSettings.LicenseKey);
    }

    [Fact]
    public void Load_BackupFileDeleted_RestoresFromSettingsIntoBackup()
    {
        SeedSettings(new TrialState { ScanCount = 7, LicenseKey = "KEY-7" });

        TrialState state = CreateStore().Load();

        Assert.Equal(7, state.ScanCount);
        TrialState? repairedBackup = ReadBackupState();
        Assert.NotNull(repairedBackup);
        Assert.Equal(7, repairedBackup!.ScanCount);
        Assert.Equal("KEY-7", repairedBackup.LicenseKey);
    }

    // ---- Load: corruption repair ------------------------------------------------

    [Fact]
    public void Load_SettingsTokenEdited_UsesBackupAndRewritesSettings()
    {
        SeedSettings(rawToken: "!!!not-a-valid-token!!!");
        SeedBackup(new TrialState { ScanCount = 6, LicenseKey = "KEY-6" });

        TrialState state = CreateStore().Load();

        Assert.Equal(6, state.ScanCount);
        Assert.Equal("KEY-6", ReadSettingsState()!.LicenseKey); // token rewritten
    }

    [Fact]
    public void Load_BackupEdited_UsesSettingsAndRewritesBackup()
    {
        SeedSettings(new TrialState { ScanCount = 6, LicenseKey = "KEY-6" });
        SeedBackupRaw("garbage that is not base64");

        TrialState state = CreateStore().Load();

        Assert.Equal(6, state.ScanCount);
        Assert.Equal("KEY-6", ReadBackupState()!.LicenseKey); // backup rewritten
    }

    [Fact]
    public void Load_BothLocationsCorrupt_ReturnsFreshStateAndRepairsBoth()
    {
        SeedSettings(rawToken: "corrupt-settings");
        SeedBackupRaw("corrupt-backup");

        TrialState state = CreateStore().Load();

        Assert.Equal(0, state.ScanCount);
        Assert.Equal(0, ReadSettingsState()!.ScanCount);
        Assert.Equal(0, ReadBackupState()!.ScanCount);
    }

    [Fact]
    public void Load_SettingsHoldsImpossibleNegativeCount_IsTreatedAsCorrupt()
    {
        // Craftable only with the cipher key, but still: a negative count must
        // never win reconciliation.
        SeedSettings(rawToken: TrialStateCipher.Encrypt("""{"ScanCount":-5,"LicenseKey":""}"""));
        SeedBackup(new TrialState { ScanCount = 4 });

        TrialState state = CreateStore().Load();

        Assert.Equal(4, state.ScanCount);
    }

    [Fact]
    public void Load_TiedCounts_PrefersNonEmptyLicenseKey()
    {
        SeedSettings(new TrialState { ScanCount = 5, LicenseKey = "" });
        SeedBackup(new TrialState { ScanCount = 5, LicenseKey = "FROM-BACKUP" });

        Assert.Equal("FROM-BACKUP", CreateStore().Load().LicenseKey);
    }

    // ---- Integration with the real settings.json --------------------------------

    [Fact]
    public void DefaultBackupPath_FollowsTheSettingsDirectoryOverride()
    {
        // Without this, a test run (or a redirected profile) would write
        // sys_cache.dat into the real LocalApplicationData instead.
        string previousOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR") ?? "";
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _dir);
        try
        {
            Assert.Equal(Path.Combine(_dir, TrialStateStore.BackupFileName), TrialStateStore.DefaultBackupFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR",
                previousOverride.Length > 0 ? previousOverride : null);
        }
    }

    [Fact]
    public void TokenSurvivesOrdinaryAppSettingsSaves()
    {
        // Production wiring: location 1 is the live AppSettings instance, so a
        // later theme change must carry the token along instead of clobbering it.
        string previousOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR") ?? "";
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _dir);
        try
        {
            var state = new TrialState { ScanCount = 9, LicenseKey = "KEY-9" };
            TrialStateStore store = CreateStoreThroughAppSettings();

            store.Save(state);

            // An unrelated settings change (which calls Save on the whole object).
            AppSettings settings = AppSettings.Load();
            settings.Theme = "Light";
            settings.Save();

            Assert.Equal("Light", AppSettings.Load().Theme);
            TrialState? reloaded = CreateStoreThroughAppSettings().Load();
            Assert.Equal(9, reloaded!.ScanCount);
            Assert.Equal("KEY-9", reloaded.LicenseKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR",
                previousOverride.Length > 0 ? previousOverride : null);
        }
    }

    [Fact]
    public void AppSettings_TrialToken_DefaultsToEmptyAndRoundTrips()
    {
        string previousOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR") ?? "";
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _dir);
        try
        {
            Assert.Equal("", new AppSettings().TrialToken);

            var settings = new AppSettings { TrialToken = "abc123" };
            settings.Save();

            Assert.Equal("abc123", AppSettings.Load().TrialToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR",
                previousOverride.Length > 0 ? previousOverride : null);
        }
    }

    // ---- helpers ----------------------------------------------------------------

    private TrialStateStore CreateStore() => TrialStateStore.CreateDefault(_settingsPath, _backupPath);

    private TrialStateStore CreateStoreThroughAppSettings() => new(
        readToken: () => AppSettings.Load().TrialToken,
        writeToken: token =>
        {
            AppSettings settings = AppSettings.Load();
            settings.TrialToken = token;
            settings.Save();
        },
        backupFilePath: _backupPath);

    private void SeedSettings(TrialState? state = null, string? rawToken = null)
    {
        var document = new JsonObject { ["Theme"] = "Dark" };
        if (rawToken is not null)
            document["TrialToken"] = rawToken;
        else if (state is not null)
            document["TrialToken"] = TrialStateCipher.Encrypt(state);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void SeedBackup(TrialState state) =>
        SeedBackupRaw(TrialStateCipher.Encrypt(state));

    /// <summary>Writes the backup file verbatim, used to plant corrupt content.</summary>
    private void SeedBackupRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_backupPath)!);
        File.WriteAllText(_backupPath, content);
    }

    private string? ReadSettingsToken()
    {
        if (!File.Exists(_settingsPath)) return null;
        return JsonNode.Parse(File.ReadAllText(_settingsPath))?["TrialToken"]?.GetValue<string>();
    }

    private void WriteSettingsToken(string token)
    {
        var document = File.Exists(_settingsPath)
            ? JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        document["TrialToken"] = token;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private TrialState? ReadSettingsState()
    {
        string? token = ReadSettingsToken();
        return token is null ? null : TrialStateCipher.TryDecryptState(token);
    }

    private TrialState? ReadBackupState() =>
        File.Exists(_backupPath) ? TrialStateCipher.TryDecryptState(File.ReadAllText(_backupPath)) : null;
}
