using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PhoneGrade.Core;

/// <summary>
/// Whether DYMO's own label software is on this machine.
///
/// The app talks to the DYMO Connect daemon directly when a printer of ours is
/// connected, and every other case went through the label PDF. With the software
/// installed the .dymo file itself can be opened instead: it carries the label's
/// own layout and prints from the program the operator already knows, where a
/// PDF hops through the generic print dialog. This is the question that decides
/// between the two.
///
/// It asks after the software rather than after the daemon, because it has to
/// answer while DYMO Connect is closed: that is exactly the moment the .dymo
/// file would be opened to start it.
/// </summary>
public static class DymoSoftware
{
    private static bool? _installed;

    /// <summary>True when DYMO's label software is installed. Probed once per run.</summary>
    public static bool IsInstalled => _installed ??= Probe();

    /// <summary>
    /// True for the name of DYMO's own software in an uninstall entry.
    ///
    /// The match is on DYMO together with one of its product words, not on DYMO
    /// alone: this app shipped as AutoDymoLabel until v5, and an old install's
    /// uninstall entry would otherwise read as DYMO's software and send labels
    /// to a program that is not there.
    /// </summary>
    public static bool LooksLikeDymoSoftware(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        return Regex.IsMatch(name, @"\bDYMO\s+(Connect|Label|ID|Print|Web)\b", RegexOptions.IgnoreCase);
    }

    private static bool Probe()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ProbeWindows();
            if (OperatingSystem.IsMacOS()) return ProbeMac();
        }
        catch (Exception)
        {
            // A probe that cannot read the machine answers "not installed", and
            // the print path stays on the PDF, which works everywhere.
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool ProbeWindows()
    {
        if (UninstallEntries().Any(LooksLikeDymoSoftware)) return true;

        // The uninstall entry is the answer when there is one; the install
        // folder covers a registration that was scrubbed by an upgrade. The
        // app itself installs under the user's local app data, so a DYMO folder
        // here can only be DYMO's own.
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];

        foreach (string root in roots)
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;

            try
            {
                if (Directory.EnumerateDirectories(root, "DYMO*").Any()) return true;
            }
            catch (Exception)
            {
                // An unreadable folder is not an answer either way.
            }
        }

        return false;
    }

    [SupportedOSPlatform("macos")]
    private static bool ProbeMac()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (string root in new[] { "/Applications", Path.Combine(home, "Applications") })
        {
            try
            {
                if (Directory.Exists(root) &&
                    Directory.EnumerateDirectories(root, "DYMO*")
                        .Any(path => LooksLikeDymoSoftware(Path.GetFileName(path))))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // An unreadable folder is not an answer either way.
            }
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static List<string> UninstallEntries()
    {
        var names = new List<string>();

        (RegistryHive Hive, RegistryView View)[] places =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
        ];

        foreach ((RegistryHive hive, RegistryView view) in places)
        {
            try
            {
                using RegistryKey? baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstall = baseKey?.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;

                foreach (string name in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? entry = uninstall.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string display && display.Length > 0)
                        names.Add(display);
                }
            }
            catch (Exception)
            {
                // One unreadable place does not decide the others.
            }
        }

        return names;
    }
}
