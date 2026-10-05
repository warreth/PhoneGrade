using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// A stable, opaque identity for the computer PhoneGrade runs on.
///
/// Lemon Squeezy counts activations per key, and the thing it counts is whatever
/// the app sends as <c>instance_name</c>. That name has to be the same on every
/// launch of the same computer and different between two computers, or the seat
/// count is either stuck or meaningless. A computer name does not qualify: it can
/// be renamed, and two machines on a shop floor are often both called DESKTOP.
///
/// So the identity is read from the operating system instead, in this order, and
/// the first value that yields a usable id wins:
/// - Windows: the MachineGuid under HKLM\SOFTWARE\Microsoft\Cryptography, written
///   by Setup and not editable from a normal settings screen.
/// - macOS: IOPlatformUUID out of ioreg, the same value Finder shows as the
///   machine's serial in the About window.
/// - Linux: /etc/machine-id, then /var/lib/dbus/machine-id for the systems that
///   keep the systemd one somewhere else.
///
/// What is deliberately never read: the computer name, the user name, the disk
/// serial numbers and the network adapter addresses. All four travel further than
/// they should (the name goes into the vendor's dashboard, the serials out of the
/// machine), and none of the first two is unique to a machine anyway. The value
/// sent out is a UUID under a short prefix, so the dashboard can tell PhoneGrade
/// rows apart without the row carrying anything about the shop.
///
/// <see cref="Unavailable"/> is a result in its own right rather than an empty
/// string. An empty fingerprint that still activated would let anybody bypass the
/// seat limit by breaking the fingerprint, so a machine that cannot be identified
/// is not activated at all: it stays on the free tier, which needs no identity.
/// </summary>
public sealed record MachineFingerprint
{
    /// <summary>Prefix on every value sent to Lemon Squeezy, so the dashboard can filter for this app.</summary>
    public const string Prefix = "pg-";

    /// <summary>How many hex characters a usable machine id has, before it is put back in UUID form.</summary>
    private const int HexLength = 32;

    private const string HexDigits = "0123456789abcdef";

    /// <summary>Which of the operating system's identifiers the value came from.</summary>
    public enum Source
    {
        /// <summary>Nothing on this machine yielded a usable id.</summary>
        None,

        /// <summary>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid.</summary>
        WindowsMachineGuid,

        /// <summary>IOPlatformUUID read through ioreg.</summary>
        MacPlatformUuid,

        /// <summary>/etc/machine-id.</summary>
        LinuxMachineId,

        /// <summary>/var/lib/dbus/machine-id, the fallback on systems that keep the first one elsewhere.</summary>
        DbusMachineId
    }

    /// <summary>Whether a usable identity was found on this machine.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// The value to send as <c>instance_name</c>, empty when
    /// <see cref="IsAvailable"/> is false. Never a hostname, a user name, a disk
    /// serial or a MAC address.
    /// </summary>
    public string Value { get; init; } = "";

    /// <summary>Where <see cref="Value"/> came from, which is what the tests pin per branch.</summary>
    public Source Origin { get; init; } = Source.None;

    /// <summary>The machine has no usable identity; activation must not be attempted.</summary>
    public static MachineFingerprint Unavailable { get; } = new();

    /// <summary>Reads this machine's identity. The result is cached: the value cannot change while the app runs.</summary>
    public static MachineFingerprint Current { get; } = Read();

    /// <summary>
    /// The short form for the operator: the last four hex characters of the
    /// fingerprint. The panel shows one of these per machine rather than a full
    /// UUID, because the operator is confirming "this bench, not the other one"
    /// and not reading a serial number.
    /// </summary>
    public string ShortValue =>
        Value.Length <= Prefix.Length ? "" : Value[^4..];

    /// <summary>
    /// The material the trial cipher keys on. A machine with an identity is keyed
    /// on that identity, so a copied settings file does not carry the free scan
    /// count to another computer. A machine without one is keyed on the computer
    /// and user name, which is weaker but keeps the free tier working: the ten
    /// free scans must not depend on being able to read a machine id.
    /// </summary>
    internal string CipherMaterial =>
        IsAvailable ? Value : $"{Environment.MachineName}|{Environment.UserName}";

    private static MachineFingerprint Read() =>
        Resolve(new MachineIdentitySource(), DetectPlatform());

    /// <summary>
    /// Picks the first usable id from the ordered candidates. The order is part of
    /// the contract, not an implementation detail: a machine with two readable
    /// identifiers must keep answering with the same one forever, or every launch
    /// looks like a new machine to the vendor.
    /// </summary>
    internal static MachineFingerprint Resolve(MachineIdentitySource source, Source platform)
    {
        var candidates = platform switch
        {
            Source.WindowsMachineGuid => new[]
            {
                (Source.WindowsMachineGuid, source.WindowsMachineGuid)
            },
            Source.MacPlatformUuid => new[]
            {
                (Source.MacPlatformUuid, source.MacPlatformUuid)
            },
            _ => new[]
            {
                (Source.LinuxMachineId, source.LinuxMachineId),
                (Source.DbusMachineId, source.DbusMachineId)
            }
        };

        foreach ((Source origin, Func<string?> read) in candidates)
        {
            string? raw = read();
            if (TryFormat(raw, out string value))
                return new MachineFingerprint { IsAvailable = true, Value = value, Origin = origin };
        }

        return Unavailable;
    }

    /// <summary>Which identifier family this operating system offers.</summary>
    private static Source DetectPlatform()
    {
        if (OperatingSystem.IsWindows()) return Source.WindowsMachineGuid;
        if (OperatingSystem.IsMacOS()) return Source.MacPlatformUuid;
        return Source.LinuxMachineId;
    }

    /// <summary>
    /// Turns a raw machine id into the value that goes on the wire, or reports
    /// that it cannot. All three sources are already UUIDs, so the rule is: strip
    /// the separators the operating system happens to write, require 32 hex
    /// characters, and put the dashes back in the canonical places. Anything else
    /// is refused, which is what keeps a name or an address from being mistaken
    /// for an identity.
    /// </summary>
    internal static bool TryFormat(string? raw, out string value)
    {
        value = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var hex = new System.Text.StringBuilder(HexLength);
        foreach (char character in raw)
        {
            if (character is '{' or '}' or '-' or '"' or ' ' or '\t' or '\r' or '\n') continue;
            int digit = HexDigits.IndexOf(char.ToLowerInvariant(character));
            if (digit < 0) return false; // not hex at all: this is not a machine id
            hex.Append(char.ToLowerInvariant(character));
        }

        if (hex.Length != HexLength) return false;

        string bare = hex.ToString();
        value = $"{Prefix}{bare[..8]}-{bare[8..12]}-{bare[12..16]}-{bare[16..20]}-{bare[20..]}";
        return true;
    }
}

/// <summary>
/// Where the identifier for each platform is read from. Every read is a delegate
/// so a test can exercise each branch, including the ones that cannot run on the
/// machine doing the testing, without pretending to be another operating system:
/// what is under test is which source is preferred and what a value from it turns
/// into, and that is the same everywhere.
/// </summary>
internal sealed class MachineIdentitySource
{
    /// <summary>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid.</summary>
    internal Func<string?> WindowsMachineGuid { get; init; } = ReadWindowsMachineGuid;

    /// <summary>IOPlatformUUID out of <c>ioreg -rd1 -c IOPlatformExpertDevice</c>.</summary>
    internal Func<string?> MacPlatformUuid { get; init; } = ReadMacPlatformUuid;

    /// <summary>/etc/machine-id.</summary>
    internal Func<string?> LinuxMachineId { get; init; } = () => ReadFile("/etc/machine-id");

    /// <summary>/var/lib/dbus/machine-id, read only when /etc/machine-id gave nothing.</summary>
    internal Func<string?> DbusMachineId { get; init; } = () => ReadFile("/var/lib/dbus/machine-id");

    /// <summary>
    /// The Windows MachineGuid, read through the registry rather than WMI: the
    /// key is written once by Setup, is readable without elevation, and does not
    /// drag the WMI service into a code path that must work on the first launch.
    /// Guarded because the registry does not exist off Windows.
    /// </summary>
    private static string? ReadWindowsMachineGuid()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var cryptography = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return cryptography?.GetValue("MachineGuid") as string;
        }
        catch (Exception)
        {
            // A locked-down machine, or no registry at all. Unavailable, not a fault.
            return null;
        }
    }

    private static string? ReadMacPlatformUuid()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        string output = Run("ioreg", "-rd1", "-c", "IOPlatformExpertDevice");
        if (output.Length == 0) return null;

        // The value sits in a plist dump on its own line: "IOPlatformUUID" = "XXXX-...".
        Match match = Regex.Match(output, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null; // unreadable machine-id is unreadable, not a crash on startup
        }
    }

    /// <summary>
    /// Runs a helper command and takes its output. A machine without ioreg, or a
    /// shell that refuses, is an ordinary outcome here and becomes an unavailable
    /// fingerprint rather than an error the operator has to read.
    /// </summary>
    private static string Run(string fileName, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);

            using Process? process = Process.Start(start);
            if (process is null) return "";
            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception)
        {
            return "";
        }
    }
}