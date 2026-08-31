using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoDymoLabelApp.UI.Models;

/// <summary>App settings persisted to %LOCALAPPDATA%/AutoDymoLabel/settings.json.</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Dark";            // "Dark", "Light" or "System"
    public bool AutoActivate { get; set; } = true;
    public bool AutoDetectOnPlug { get; set; } = true;    // start the flow the moment a device appears
    public bool RunDiagnostics { get; set; } = true;
    public bool Enable85PercentChecker { get; set; } = true;
    public bool OpenEditorBeforePrint { get; set; } = false;
    public string DefaultQuality { get; set; } = "";       // "", "A", "B", "C" — empty asks
    public string DefaultPaymentMethod { get; set; } = "";  // "", "Marge", "BTW"
    public string? TemplatePath { get; set; }             // custom my.dymo override

    [JsonIgnore]
    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDymoLabel");
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
