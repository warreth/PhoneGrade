using System.Diagnostics;

namespace AutoDymoLabel.Core;

/// <summary>
/// Generates DYMO labels by filling the my.dymo template, and opens them in the DYMO Label app.
/// </summary>
public static class LabelService
{
    /// <summary>Template search order: configured path, app-dir Assets, cwd. Set by UI settings.</summary>
    public static string? ConfiguredTemplatePath { get; set; }

    public static string OutputPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoDymoLabel", "gen_label.dymo");

    /// <summary>Finds the label template shipped with the app, or the configured override.</summary>
    public static string FindTemplate()
    {
        if (ConfiguredTemplatePath is { Length: > 0 } && File.Exists(ConfiguredTemplatePath))
            return ConfiguredTemplatePath;

        string exeDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(exeDir, "Assets", "my.dymo"),
            Path.Combine(exeDir, "my.dymo"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "my.dymo"),
        ];
        foreach (string c in candidates) if (File.Exists(c)) return c;
        throw new FileNotFoundException(
            $"Label template my.dymo not found. Looked in: {string.Join(", ", candidates)}");
    }

    /// <summary>Generates the .dymo label file from the template. Returns the output path.</summary>
    public static string GenerateLabel(DeviceData data)
    {
        string template = FindTemplate();
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);

        string battery = data.BatteryHealth.Contains('%') || data.BatteryHealth.Contains("NOBATT")
            ? data.BatteryHealth
            : $"{data.BatteryHealth}%";

        string content = File.ReadAllText(template)
            .Replace("IDENTIFIER", data.Identifier)
            .Replace("MODEL", data.Model)
            .Replace("PCOLOR", data.Color)
            .Replace("BATTERY", battery)
            .Replace("QUALITY", data.Quality)
            .Replace("PAYM", data.PayMethod)
            .Replace("STORAGE", data.Storage);

        File.WriteAllText(OutputPath, content);
        return OutputPath;
    }

    /// <summary>Opens a file with the OS default handler (DYMO Label on .dymo).</summary>
    public static string OpenLabelFile(string? path = null)
    {
        string file = path ?? OutputPath;
        if (!File.Exists(file)) return $"ERROR: label file not found at {file}";
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = true,
            });
            return "Label opened in DYMO Label.";
        }
        catch (Exception ex)
        {
            return $"ERROR opening label: {ex.Message}";
        }
    }
}
