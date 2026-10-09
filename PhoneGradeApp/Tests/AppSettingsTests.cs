using PhoneGrade.UI.Models;
using PhoneGrade.Core;
using Xunit;

namespace Tests;

// ============ Settings tests: fully isolated via AUTODYMO_SETTINGS_DIR ============

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public AppSettingsTests() => Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _dir);

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        new AppSettings { Theme = "Light", AutoDetectOnPlug = false, DefaultQuality = "A" }.Save();
        var loaded = AppSettings.Load();
        Assert.Equal("Light", loaded.Theme);
        Assert.False(loaded.AutoDetectOnPlug);
        Assert.Equal("A", loaded.DefaultQuality);
    }

    [Fact]
    public void SaveLoad_AllFields_RoundTrip()
    {
        var s = new AppSettings
        {
            Theme = "System",
            AutoActivate = false,
            AutoDetectOnPlug = false,
            RunDiagnostics = false,
            Enable85PercentChecker = false,
            OpenEditorBeforePrint = true,
            DefaultQuality = "C",
            DefaultPaymentMethod = "BTW",
            TemplatePath = "/tmp/custom.dymo",
            LabelVariant = LabelVariant.GradeBlock,
            LabelBatteryThreshold = 70,
            LabelCyclesThreshold = 800,
            LabelBarcodeEnabled = false,
            ExportFormats = [ExportFormat.ReportPdf, ExportFormat.Json],
            ExportFolderScheme = ExportFolderScheme.Month,
        };
        s.Save();
        var loaded = AppSettings.Load();
        Assert.Equal(s.Theme, loaded.Theme);
        Assert.Equal(s.AutoActivate, loaded.AutoActivate);
        Assert.Equal(s.AutoDetectOnPlug, loaded.AutoDetectOnPlug);
        Assert.Equal(s.RunDiagnostics, loaded.RunDiagnostics);
        Assert.Equal(s.Enable85PercentChecker, loaded.Enable85PercentChecker);
        Assert.Equal(s.OpenEditorBeforePrint, loaded.OpenEditorBeforePrint);
        Assert.Equal(s.DefaultQuality, loaded.DefaultQuality);
        Assert.Equal(s.DefaultPaymentMethod, loaded.DefaultPaymentMethod);
        Assert.Equal(s.TemplatePath, loaded.TemplatePath);
        Assert.Equal(s.LabelVariant, loaded.LabelVariant);
        Assert.Equal(s.LabelBatteryThreshold, loaded.LabelBatteryThreshold);
        Assert.Equal(s.LabelCyclesThreshold, loaded.LabelCyclesThreshold);
        Assert.Equal(s.LabelBarcodeEnabled, loaded.LabelBarcodeEnabled);
        Assert.Equal(s.ExportFormats, loaded.ExportFormats);
        Assert.Equal(s.ExportFolderScheme, loaded.ExportFolderScheme);
    }

    [Fact]
    public void TheExportFormatsAreStoredAsNames_SoTheFileCanBeRead()
    {
        // A settings file is a file a person may open, and a list of numbers is a
        // list nobody can check. A file that carries numbers from an older build
        // still loads, because silently changing what a shop exports over an app
        // update is worse than any wording.
        new AppSettings { ExportFormats = [ExportFormat.DymoLabel, ExportFormat.ReportPdf] }.Save();
        string text = File.ReadAllText(Path.Combine(_dir, "settings.json"));

        Assert.Contains("DymoLabel", text, StringComparison.Ordinal);
        Assert.Contains("ReportPdf", text, StringComparison.Ordinal);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"ExportFormats":["ReportPdf","Json"]}""");
        Assert.Equal(
            new[] { ExportFormat.ReportPdf, ExportFormat.Json },
            AppSettings.Load().ExportFormats);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ this is not json ]");
        Assert.Equal("Dark", AppSettings.Load().Theme);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = AppSettings.Load();
        Assert.False(loaded.AutoActivate);
        Assert.True(loaded.Enable85PercentChecker);
        Assert.False(loaded.AutoDetectOnPlug);
        Assert.Equal("Dark", loaded.Theme);

        // The label and the report are what a fresh install writes, and the
        // thresholds are the ones the label has always used.
        Assert.Equal(
            new[] { ExportFormat.DymoLabel, ExportFormat.ReportPdf },
            loaded.ExportFormats);
        Assert.Equal(LabelVariant.Clean, loaded.LabelVariant);
        Assert.Equal(ExportFolderScheme.Week, loaded.ExportFolderScheme);
        Assert.Equal(LabelFields.LowBatteryPercent, loaded.LabelBatteryThreshold);
        Assert.Equal(500, loaded.LabelCyclesThreshold);
    }

    [Fact]
    public void Save_WritableDir_CreatesFile()
    {
        new AppSettings().Save();
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
