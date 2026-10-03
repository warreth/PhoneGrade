using System.Collections.Generic;
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
    
    // IMEI.info BYOK API Configuration
    public string ImeiInfoApiKey { get; set; } = "";       // API key from imei.info dashboard
    public List<string> SelectedImeiChecks { get; set; } = new(); // Enabled check types (service codes)
    public int EstimatedAppleDevices { get; set; } = 10;   // For cost calculator
    public int EstimatedAndroidDevices { get; set; } = 10; // For cost calculator

    public bool RunDiagnostics { get; set; } = true;
    public bool Enable85PercentChecker { get; set; } = true;
    public bool OpenEditorBeforePrint { get; set; } = false;
    public string DefaultQuality { get; set; } = "";       // "", "A", "B", "C": empty asks
    public string DefaultPaymentMethod { get; set; } = "";  // "", "Marge", "BTW"
    public string? TemplatePath { get; set; }             // custom my.dymo override
    public bool EnableVerboseNetworkLogging { get; set; } = false; // Trace HTTP requests, CLI stdout/stderr, JSON payloads
    public bool IsDebugMode { get; set; } = false; // Global debug toggle for mobile PWA overlay and verbose tracing
    public bool EnableUsbEventMonitoring { get; set; } = true; // Use native USB event monitoring instead of polling

    /// <summary>
    /// Encrypted trial payload (scan count plus license key) as Base64, written
    /// by the licensing store. It lives here so that every ordinary settings
    /// save carries the current token along instead of overwriting it with a
    /// stale copy, and so a copied settings.json alone never reveals the count:
    /// the token only decrypts on the machine and account it was written for.
    /// </summary>
    public string TrialToken { get; set; } = "";

    /// <summary>
    /// Serves the PWA to the phone as http://localhost through an adb reverse
    /// tunnel instead of the LAN address. A browser only grants camera,
    /// microphone, motion and orientation access on a secure origin, and a plain
    /// http:// LAN address is not one, so over the network those four steps have
    /// nothing to run on. The tunnel needs the cable that is required anyway, so
    /// this is the default. Turn it off to drop straight back to the LAN address.
    /// </summary>
    public bool UseSecureOrigin { get; set; } = true;

    /// <summary>
    /// Opens a public https address for a phone that cannot use the adb tunnel,
    /// which is every iPhone. Off keeps every address on the local network.
    /// </summary>
    public bool UsePublicTunnel { get; set; } = true;

    /// <summary>
    /// Whether the introduction screen has been dismissed. False on a fresh
    /// install, which is the only place it belongs: an operator who has already
    /// seen what the free tier is and where the paid plan lives should never be
    /// stopped by it again.
    /// </summary>
    public bool IntroSeen { get; set; } = false;

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
