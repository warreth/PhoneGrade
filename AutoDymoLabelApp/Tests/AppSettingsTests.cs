using AutoDymoLabelApp.UI.Models;
using AutoDymoLabel.Core;
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
        Assert.True(loaded.AutoActivate);
        Assert.True(loaded.Enable85PercentChecker);
        Assert.True(loaded.AutoDetectOnPlug);
        Assert.Equal("Dark", loaded.Theme);
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
