using System;
using System.IO;

namespace Tests;

/// <summary>
/// A scratch settings folder for one test class, with the process-wide
/// AUTODYMO_SETTINGS_DIR pointing at it while the test runs.
///
/// The settings file is read at call time all over the app, so a test that
/// wants its own theme, language or licence state points the variable at a
/// folder of its own instead of touching the real profile. This is that
/// folder: created with the JSON the test wants, restored and deleted on
/// dispose. Twenty-odd classes used to carry the same six lines by hand.
/// </summary>
public sealed class SettingsScratch : IDisposable
{
    private readonly string? _original;

    /// <param name="settingsJson">
    /// What settings.json holds for this test. The default is the dark theme
    /// most tests use; pass a different document when the test is about
    /// something else in the file.
    /// </param>
    public SettingsScratch(string settingsJson = """{"Theme":"Dark"}""")
    {
        _original = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

        Directory = Path.Combine(Path.GetTempPath(), $"phonegrade-settings-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Directory);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", Directory);

        if (settingsJson is not null)
            File.WriteAllText(Path.Combine(Directory, "settings.json"), settingsJson);
    }

    /// <summary>The folder the app now treats as its settings directory.</summary>
    public string Directory { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _original);

        try
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
        catch
        {
            // Temp cleanup never fails a test.
        }
    }
}