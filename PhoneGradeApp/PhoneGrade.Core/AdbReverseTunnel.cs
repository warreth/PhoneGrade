using System.Text.RegularExpressions;

namespace PhoneGrade.Core;

/// <summary>
/// Points the test server at the phone through USB instead of the network.
///
/// A browser only hands out camera, microphone, motion and orientation access on
/// a secure origin. A plain http:// address on the LAN is not one, so over the
/// network those four APIs simply do not exist and the steps that use them have
/// nothing to run on. Chromium does treat http://localhost as secure, and
/// "adb reverse" gives the handset a localhost that arrives at this machine's
/// port, which turns the page into a secure origin without changing anything
/// about the server.
///
/// The tunnel needs the cable that is already required to read the device, so it
/// is the default. The LAN address stays available for iOS, which has no
/// equivalent of adb reverse.
/// </summary>
public sealed partial class AdbReverseTunnel
{
    /// <summary>Runs adb with the given arguments and timeout, returning its output.</summary>
    public delegate Task<(string Stdout, string Stderr, int ExitCode)> AdbRunner(string arguments, int timeoutMs);

    private readonly AdbRunner _adb;

    public AdbReverseTunnel(AdbRunner adb)
    {
        _adb = adb;
    }

    /// <summary>The production tunnel: every call goes through the bundled adb.</summary>
    public static AdbReverseTunnel CreateDefault() =>
        new((args, timeout) => ToolRunner.ExecuteAsync("adb", args, timeout));

    /// <summary>
    /// The host the phone should use in the URL. Anything but localhost keeps the
    /// page on a plain address, which is the whole point of the switch.
    /// </summary>
    public static string LoopbackHost => "localhost";

    /// <summary>
    /// True when the device id belongs to something adb can build a tunnel for.
    ///
    /// The rule is by shape, not by a device list. An Apple udid is hex only, in
    /// either the 40 character form or the 8-16 dash form, and a serial that
    /// matches one of those cannot be an adb serial. Everything else can: a Pixel
    /// reports an alphanumeric serial that may start with a letter, as a Samsung
    /// does, so a leading-digit test would wrongly rule those out. "DEMO" is the
    /// placeholder the app uses when no device has been picked yet.
    /// </summary>
    [GeneratedRegex(@"^([0-9a-fA-F]{40}|[0-9a-fA-F]{8}-[0-9a-fA-F]{16})$")]
    private static partial Regex AppleUdidRegex();

    public static bool SupportsReverse(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        if (sessionId.Equals("DEMO", StringComparison.OrdinalIgnoreCase)) return false;

        return !AppleUdidRegex().IsMatch(sessionId);
    }

    /// <summary>
    /// Binds a port on the phone to the same port on this machine. Returns false
    /// when adb is missing, the device is not authorised, or the build has no
    /// reverse support, so the caller can fall back to the network address.
    /// </summary>
    public async Task<bool> OpenAsync(string serial, int port)
    {
        if (string.IsNullOrWhiteSpace(serial) || port <= 0) return false;

        // The old mapping is dropped first: adb refuses to rebind a port it still
        // holds, and a stale mapping from a previous run would silently send the
        // phone to whatever was listening then.
        await RemoveAsync(serial, port);

        var (_, stderr, exitCode) = await _adb($"-s {serial} reverse tcp:{port} tcp:{port}", 10_000);

        if (exitCode != 0)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector,
                $"adb reverse tcp:{port} failed (exit {exitCode}): {stderr}");
            return false;
        }

        SystemEventLogger.Info(LogSource.UsbDetector,
            $"adb reverse tcp:{port} opened for {serial}, the phone sees the server as localhost");
        return true;
    }

    /// <summary>Removes the mapping. Safe to call when there is none.</summary>
    public async Task RemoveAsync(string serial, int port)
    {
        if (string.IsNullOrWhiteSpace(serial) || port <= 0) return;

        try
        {
            await _adb($"-s {serial} reverse --remove tcp:{port}", 10_000);
        }
        catch
        {
            // Nothing to undo is not a problem worth surfacing.
        }
    }

    /// <summary>True when the phone can be reached over the tunnel right now.</summary>
    public async Task<bool> IsOpenAsync(string serial, int port)
    {
        if (string.IsNullOrWhiteSpace(serial) || port <= 0) return false;

        try
        {
            var (stdout, _, exitCode) = await _adb($"-s {serial} reverse --list", 10_000);
            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return false;

            // adb prints one mapping per line as "serial tcp:5055 tcp:5055".
            return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Contains($"tcp:{port} tcp:{port}", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }
}
