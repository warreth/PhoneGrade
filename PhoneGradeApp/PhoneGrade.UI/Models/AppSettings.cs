using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoneGrade.UI.Models;

/// <summary>App settings persisted to %LOCALAPPDATA%/PhoneGrade/settings.json.</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Dark";            // "Dark", "Light" or "System"
    public string Language { get; set; } = "nl";           // "en" (English) or "nl" (Nederlands)
    public bool AutoActivate { get; set; } = false;
    public bool AutoDetectOnPlug { get; set; } = false;   // User must explicitly click to start inspecting
    public bool AutoStartWebTest { get; set; } = false;   // User must manually scan and run tests
    public bool ShowSummaryScreenAfterTesting { get; set; } = true;
    public bool RequirePwaTest { get; set; } = true;       // operator must complete PWA test before finishing
    public bool AutoFinishAfterTest { get; set; } = false; // automatically jump to summary screen after tests complete
    public string ImeiApiKey { get; set; } = "";           // SickW / IMEIPro API key for real-time FMI check
    public string ImeiApiProvider { get; set; } = "sickw"; // default API provider
    public bool RunDiagnostics { get; set; } = true;
    public bool Enable85PercentChecker { get; set; } = true;
    public bool OpenEditorBeforePrint { get; set; } = false;
    public string DefaultQuality { get; set; } = "";       // "", "A", "B", "C": empty asks
    public string DefaultPaymentMethod { get; set; } = "";  // "", "Marge", "BTW"
    public string? TemplatePath { get; set; }             // custom my.dymo override
    public bool EnableVerboseNetworkLogging { get; set; } = false; // Trace HTTP requests, CLI stdout/stderr, JSON payloads
    public bool IsDebugMode { get; set; } = false; // Global debug toggle for mobile PWA overlay and verbose tracing

    [JsonIgnore]
    private static string SettingsDir =>
        Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneGrade");
    [JsonIgnore]
    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { /* corrupt settings → defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only dir / permission → keep running with in-memory settings */ }
    }
}
