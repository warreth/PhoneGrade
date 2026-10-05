using System.Text;
using PhoneGrade.Core;

namespace PhoneGrade.Tests;

/// <summary>
/// The recorded output of real handsets, taken with <see cref="DeviceCapture"/>
/// and checked in so the parsers are proved against what phones actually print
/// without needing a phone on the machine that runs the suite.
///
/// Every file is written the way <see cref="ToolRunner.RunAsync"/> returns a
/// command, which for adb means stdout only and for libimobiledevice means stdout
/// with stderr appended. Where a command refused, the exit code sits beside it in
/// a separate file, because "the handset has no such file" and "the handset would
/// not tell us" are different answers and a parser has to know which one it got.
/// </summary>
internal static class LiveFixture
{
    /// <summary>Folder holding the captures.</summary>
    public static string Folder => RepoPath.Get("PhoneGradeApp", "Tests", "Fixtures", "live");

    /// <summary>The iPhone capture taken from an iPhone 8 on iOS 16.7.10.</summary>
    public const string Iphone8 = "iphone8";

    /// <summary>The Android capture taken from a Honor X8b on Android 14.</summary>
    public const string HonorX8b = "honor-x8b";

    /// <summary>Reads one captured command. Throws when the capture is missing, on purpose.</summary>
    public static string Read(string device, string name)
    {
        string path = Path.Combine(Folder, $"{device}-{name}.txt");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No capture named '{name}' for '{device}'. " +
                $"Expected it at '{path}'. Set PHONEGRADE_CAPTURE=1 and run the capture test with a handset attached to take it.");
        }
        return File.ReadAllText(path);
    }

    /// <summary>Every capture taken for one device, keyed by the name it was stored under.</summary>
    public static Dictionary<string, string> ReadAll(string device)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(Folder)) return all;
        string prefix = device + "-";
        foreach (string file in Directory.GetFiles(Folder, prefix + "*.txt"))
        {
            all[Path.GetFileName(file)[prefix.Length..^".txt".Length]] = File.ReadAllText(file);
        }
        return all;
    }

    /// <summary>The exit code adb gave for a captured command, or -1 when it was not recorded.</summary>
    public static int ExitCode(string device, string name)
    {
        string text = Read(device, name + "@exit");
        return int.TryParse(text.Trim(), out int code) ? code : -1;
    }

    /// <summary>Whether the handset allowed the command at all. An adb shell that is
    /// refused exits non-zero with nothing on stdout.</summary>
    public static bool AdbCommandRan(string device, string name) => ExitCode(device, name) == 0;

    /// <summary>
    /// Rebuilds the production adb shell runner from a capture, including the
    /// refusal flag. The exit code recorded beside each answer is what decides it,
    /// so the reader sees the same two facts it sees on the bench.
    /// </summary>
    public static AndroidDeviceReader.GuardedShellRunner AdbShellFromCapture(string device)
    {
        var all = ReadAll(device);
        return command =>
        {
            string name = CaptureNameFor(command);
            string text = all.TryGetValue(name, out string? found) ? found : "";
            bool refused = !int.TryParse(all.GetValueOrDefault(name + "@exit")?.Trim(), out int exit) || exit != 0;
            return Task.FromResult((text, refused));
        };
    }

    /// <summary>The file name a shell command is stored under, matching DeviceCapture.</summary>
    public static string CaptureNameFor(string command)
    {
        var sb = new StringBuilder(command.Length);
        foreach (char c in command)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is '/' or '\\' or ':' or '.') sb.Append('-');
            else if (c is ' ' or '_') sb.Append('-');
        }
        return sb.ToString();
    }
}