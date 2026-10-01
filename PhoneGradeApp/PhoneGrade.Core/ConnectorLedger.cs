using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PhoneGrade.Core;

/// <summary>
/// Notes the connectors this machine has started, so the next run can take down the
/// ones nobody is holding any more.
///
/// A connector is a separate process, and a process that is killed outright leaves
/// its children behind: the window never gets to close, the tunnel is never
/// disposed, and the connector goes on forwarding a public address to a server that
/// is already gone. Nothing inside the app can notice that, because the code that
/// would have noticed went down with it, so the note has to outlive the run that
/// wrote it.
///
/// One file per run, named after the owner's process id and its start time. The
/// start time is what tells a fresh run from a process that was simply handed the
/// same id afterwards, which Windows hands out again quickly. Files are never
/// removed on the way out: a run that ended cleanly leaves a note full of
/// processes that are already gone, and the next run deletes it as a matter of
/// course. A run that could not write the note still starts; this is a safety net
/// and not a dependency.
///
/// Only the connector's own name is ever killed, so a note can never reach past it
/// into something else that happens to be recorded.
/// </summary>
public static class ConnectorLedger
{
    private const string Prefix = "connector-";
    private const string Extension = ".txt";

    /// <summary>Where the notes live: the app's own state directory.</summary>
    private static string LedgerDirectory => SystemEventLogger.LogDir;

    private static string LedgerFor(int ownerPid, long ownerStartTicks) =>
        Path.Combine(LedgerDirectory,
            Prefix + ownerPid.ToString(CultureInfo.InvariantCulture)
            + "-" + ownerStartTicks.ToString(CultureInfo.InvariantCulture) + Extension);

    /// <summary>The note this process writes, for a caller that wants to look.</summary>
    public static string NoteForThisProcess => LedgerFor(Environment.ProcessId, CurrentStartTicks());

    /// <summary>Notes a connector this process just started.</summary>
    public static void Record(int connectorPid) =>
        Record(Environment.ProcessId, CurrentStartTicks(), connectorPid);

    /// <summary>
    /// Notes a connector under the process that started it. Production always
    /// passes its own id; the owner is a parameter so a note left by a run that is
    /// no longer there can be set up and checked without pretending.
    /// </summary>
    public static void Record(int ownerPid, long ownerStartTicks, int connectorPid)
    {
        try
        {
            Directory.CreateDirectory(LedgerDirectory);
            File.AppendAllText(
                LedgerFor(ownerPid, ownerStartTicks),
                connectorPid.ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector,
                $"Could not note the tunnel connector: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes down every connector whose owner is no longer the process that noted
    /// it, and says how many went. A connector owned by a live run is left alone,
    /// so one instance can never bring down the tunnel of another.
    /// </summary>
    public static int ReapOrphans()
    {
        string[] files;
        try
        {
            files = Directory.Exists(LedgerDirectory)
                ? Directory.GetFiles(LedgerDirectory, Prefix + "*" + Extension)
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.UsbDetector,
                $"Could not read the tunnel connector notes: {ex.Message}");
            return 0;
        }

        int reaped = 0;

        foreach (string file in files)
        {
            if (!TryParseOwner(Path.GetFileNameWithoutExtension(file), out int ownerPid, out long ownerStartTicks))
                continue;

            // Own notes are not special here. A previous run that was handed the id
            // this process now holds writes a note that would otherwise be skipped
            // forever, and its start time is what says it is not this run.
            if (IsStillTheSameRun(ownerPid, ownerStartTicks))
                continue;

            foreach (int connectorPid in ReadConnectors(file))
            {
                if (KillIfConnector(connectorPid)) reaped++;
            }

            try { File.Delete(file); }
            catch { /* another run got there first, or the note is locked */ }
        }

        if (reaped > 0)
        {
            SystemEventLogger.Info(LogSource.UsbDetector,
                $"Took down {reaped} tunnel connector(s) left behind by a run that is no longer there.");
        }

        return reaped;
    }

    /// <summary>Splits "connector-&lt;pid&gt;-&lt;start ticks&gt;".</summary>
    private static bool TryParseOwner(string? name, out int ownerPid, out long ownerStartTicks)
    {
        ownerPid = 0;
        ownerStartTicks = 0;

        if (name == null || !name.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        string rest = name.Substring(Prefix.Length);
        int separator = rest.IndexOf('-');
        if (separator <= 0) return false;

        return int.TryParse(rest.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out ownerPid)
            && long.TryParse(rest.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out ownerStartTicks);
    }

    /// <summary>Reads the connector ids out of a note, ignoring anything odd.</summary>
    private static int[] ReadConnectors(string file)
    {
        try
        {
            string[] lines = File.ReadAllLines(file);
            var pids = new int[lines.Length];
            int found = 0;

            foreach (string line in lines)
            {
                if (int.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                    pids[found++] = pid;
            }

            Array.Resize(ref pids, found);
            return pids;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// Whether that id still belongs to the run that noted it. A run that is gone
    /// answers false in three different ways, and each of them is safe: no such
    /// process, a process with a different start time, or a start time that cannot
    /// be read at all, which leaves the note alone rather than risking a connector
    /// that is still in use.
    /// </summary>
    private static bool IsStillTheSameRun(int ownerPid, long ownerStartTicks)
    {
        try
        {
            using var owner = Process.GetProcessById(ownerPid);
            if (owner.HasExited) return false;
            if (ownerStartTicks <= 0) return true;
            if (!TryGetStartTicks(owner, out long actual)) return true;
            return actual == ownerStartTicks;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Kills one process, but only if it is the connector. Returns false for
    /// anything already gone, anything named differently, and anything that cannot
    /// be touched, none of which are failures worth reporting.
    /// </summary>
    private static bool KillIfConnector(int connectorPid)
    {
        // ProcessName carries no extension, so the executable name is reduced the
        // same way rather than compared as it is written on disk.
        string expected = Path.GetFileNameWithoutExtension(ToolInstallerService.CloudflaredExecutableName);

        try
        {
            using var connector = Process.GetProcessById(connectorPid);
            if (connector.HasExited) return false;
            if (!string.Equals(connector.ProcessName, expected, StringComparison.OrdinalIgnoreCase)) return false;

            connector.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long CurrentStartTicks()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            return current.StartTime.Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetStartTicks(Process process, out long ticks)
    {
        try
        {
            ticks = process.StartTime.Ticks;
            return true;
        }
        catch
        {
            ticks = 0;
            return false;
        }
    }
}
