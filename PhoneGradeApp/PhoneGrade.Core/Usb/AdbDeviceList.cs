using System;
using System.Threading.Tasks;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// What adb currently reports about the phones plugged in.
///
/// <see cref="Ran"/> is false when adb could not be run at all, which is a
/// different answer from "ran and listed nothing": the first says the tool is
/// missing, the second says no phone has switched debugging on yet. Both leave
/// the guide open, but for different reasons, so they are kept apart.
/// </summary>
public readonly record struct AdbDeviceListState(bool Ran, bool AnyAuthorized, bool AnyUnauthorized);

/// <summary>
/// Reads the adb device list. Seamed so the director can be exercised without
/// a phone, an adb binary, or a person to unlock one.
/// </summary>
public interface IAdbDeviceList
{
    Task<AdbDeviceListState> ReadAsync();
}

/// <summary>The real adb, as it answers today.</summary>
public sealed class AdbDeviceList : IAdbDeviceList
{
    public async Task<AdbDeviceListState> ReadAsync()
    {
        var (stdout, _, exitCode) = await ToolRunner.ExecuteAsync("adb", "devices");
        return Parse(stdout, exitCode).State;
    }

    /// <summary>
    /// Reads raw `adb devices` output. One parser for both readers: the
    /// director opens the how-to from this list and the device probe writes
    /// the status line from it, and two parsers over one list is how the screen
    /// ended up describing a phone it could not see.
    ///
    /// `authorizing` sits beside `unauthorized` on purpose. It is the state a
    /// phone is in while it shows the RSA prompt, which is the one moment the
    /// how-to has something to offer, and it used to fall past both branches to
    /// an empty bench.
    /// </summary>
    public static (AdbDeviceListState State, string[] Trusted) Parse(string stdout, int exitCode)
    {
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return (new AdbDeviceListState(Ran: false, AnyAuthorized: false, AnyUnauthorized: false), []);

        var trusted = new List<string>();
        bool anyUnauthorized = false;

        foreach (string raw in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (parts[1].Equals("device", StringComparison.OrdinalIgnoreCase))
            {
                trusted.Add(parts[0]);
            }
            else if (parts[1].Equals("unauthorized", StringComparison.OrdinalIgnoreCase)
                     || parts[1].Equals("authorizing", StringComparison.OrdinalIgnoreCase))
            {
                anyUnauthorized = true;
            }
        }

        return (new AdbDeviceListState(Ran: true, trusted.Count > 0, anyUnauthorized), trusted.ToArray());
    }
}
