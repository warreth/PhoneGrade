using System.Text;
using PhoneGrade.Core;
using PhoneGrade.Core.Usb;

namespace PhoneGrade.Tests;

/// <summary>
/// Runs the very command lines the application runs against a handset that is
/// plugged in right now, and keeps what came back. The captured text is what
/// <see cref="ToolRunner.RunAsync"/> hands to the parsers in production, so a
/// fixture taken here is the same string the parser sees on the bench.
///
/// The command lists live here rather than in the capture test so that the
/// capture test and the fixtures describe one thing: what the application asks
/// a phone. A command added to the reader shows up as a missing fixture.
/// </summary>
internal static class DeviceCapture
{
    /// <summary>Every file name a capture of one tool call is stored under.</summary>
    private static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is '/' or '\\' or ':' or '.') sb.Append('-');
            else if (c is ' ' or '_') sb.Append('-');
        }
        return sb.ToString();
    }

    /// <summary>The lockdown domains <c>DeviceService</c> queries, in the order it queries them.</summary>
    public static readonly string[] IosDomains =
    {
        "",
        "com.apple.mobile.gestalt",
        "com.apple.mobile.diagnostics",
        "com.apple.disk_usage",
        "com.apple.purplebuddy",
        "com.apple.fmip",
    };

    /// <summary>The ioreg nodes <c>DeviceService</c> walks for display, cameras, biometrics and battery.</summary>
    public static readonly string[] IosIORegNodes =
    {
        "IOMobileFramebuffer",
        "AppleCLCD2",
        "AppleCLCD",
        "AppleH10CamIn",
        "AppleH11CamIn",
        "AppleH13CamIn",
        "AppleH15CamIn",
        "AppleH16CamIn",
        "AppleH17CamIn",
        "AppleCameraInterface",
        "AppleISPCaptureInput",
        "AppleBiometricSensor",
        "AppleSmartBattery",
    };

    /// <summary>The shell commands <c>AndroidDeviceReader</c> runs, in order.</summary>
    public static readonly string[] AdbShellCommands =
    {
        "getprop",
        "df -k /data",
        "dumpsys battery",
        "cat /sys/class/power_supply/battery/charge_full",
        "cat /sys/class/power_supply/battery/charge_full_design",
        "service call iphonesubinfo 1",
        "service call iphonesubinfo 2",
        "cat /sys/class/net/wlan0/address 2>/dev/null",
        "settings get secure bluetooth_address 2>/dev/null",
        "getprop gsm.sim.state",
        "getprop ro.carrier",
        "settings get secure secure_frp_mode",
    };

    /// <summary>Reads every lockdown domain for one iPhone.</summary>
    public static async Task<Dictionary<string, string>> CaptureIosDomainsAsync(string udid)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string domain in IosDomains)
        {
            // The bulk reader asks every domain for XML, so the capture does too.
            // Text output would exercise a different parser than the one in use.
            string args = domain.Length == 0 ? $"-u {udid} -x" : $"-u {udid} -q {domain} -x";
            var (output, _) = await ToolRunner.RunAsync("ideviceinfo", args);
            captured[domain.Length == 0 ? "default" : SafeName(domain)] = output;
        }
        return captured;
    }

    /// <summary>Reads every ioreg node for one iPhone.</summary>
    public static async Task<Dictionary<string, string>> CaptureIosIORegAsync(string udid)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string node in IosIORegNodes)
        {
            var (output, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry {node}", 5000);
            captured[SafeName(node)] = output;
        }
        return captured;
    }

    /// <summary>Reads every adb shell command for one Android handset, stdout as the reader sees it.</summary>
    public static async Task<Dictionary<string, string>> CaptureAdbAsync(string serial)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string command in AdbShellCommands)
        {
            var (stdout, stderr, exitCode) = await ToolRunner.ExecuteAsync("adb", $"-s {serial} shell {command}");
            // The reader takes stdout and throws the rest away, so that is what is kept.
            // The exit code decides whether the empty answer means "absent" or "refused",
            // and is kept next to it for that reason alone.
            captured[SafeName(command)] = string.IsNullOrEmpty(stdout) && exitCode != 0
                ? string.Empty
                : stdout;
            captured[SafeName(command) + "@exit"] = exitCode.ToString();
            captured[SafeName(command) + "@stderr"] = stderr;
        }
        return captured;
    }

    /// <summary>Writes a capture to disk, one file per command.</summary>
    public static void Write(string folder, string prefix, IReadOnlyDictionary<string, string> captured)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, text) in captured)
        {
            File.WriteAllText(Path.Combine(folder, $"{prefix}-{name}.txt"), text, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Puts the toolset the application ships next to its own build output on the
    /// path of this process, so <see cref="ToolRunner.Resolve"/> finds it.
    ///
    /// The test project only copies the connector next to itself, not the whole
    /// libimobiledevice set, so without this the capture has no <c>ideviceinfo</c> to
    /// run and would quietly record nothing. Returns the directory it settled on, or
    /// null when the set is nowhere to be found.
    /// </summary>
    public static string? EnsureToolsOnPath()
    {
        foreach (string config in new[] { "Debug", "Release" })
        {
            string dir = RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "bin", config, "net8.0", "idevice-tools");
            if (!Directory.Exists(dir)) continue;
            if (!File.Exists(Path.Combine(dir, OperatingSystem.IsWindows() ? "idevice_id.exe" : "idevice_id")))
                continue;

            string current = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (current.Split(Path.PathSeparator).Contains(dir, StringComparer.OrdinalIgnoreCase)) return dir;

            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + current);
            return dir;
        }
        return null;
    }
}

/// <summary>
/// Finds the handsets that are plugged in at the moment. Used by the tests that
/// run the production reader against real hardware instead of a recording of it.
/// </summary>
internal static class ConnectedDevice
{
    /// <summary>The UDID of the first trusted iPhone, or null when none is attached.</summary>
    public static async Task<string?> FindIphoneAsync()
    {
        var (output, exitCode) = await ToolRunner.RunAsync("idevice_id", "-l");
        if (exitCode != 0) return null;
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .FirstOrDefault(s => s.Length > 0);
    }

    /// <summary>The serial of the first authorized Android handset, or null when none is attached.</summary>
    public static async Task<string?> FindAndroidAsync()
    {
        var (output, exitCode) = await ToolRunner.RunAsync("adb", "devices");
        if (exitCode != 0) return null;
        var (state, devices) = AdbDeviceList.Parse(output, exitCode);
        if (!state.Ran || !state.AnyAuthorized) return null;
        return devices.FirstOrDefault(s => s.Length > 0);
    }
}