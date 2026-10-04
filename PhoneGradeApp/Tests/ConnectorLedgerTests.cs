using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// Covers the note that has to outlive a run.
///
/// Tidying up the connector is the one part of a tunnel's shutdown that a process
/// which is killed outright cannot do for itself: the window never closes, the
/// tunnel is never disposed, and the connector carries on serving a public address
/// for a server that is already gone. Everything here therefore runs against real
/// processes, and a stand-in that is named after the connector is what the
/// reaper's own filter is measured against rather than a shortcut around it.
/// </summary>
public class ConnectorLedgerTests
{
    [Fact]
    public async Task StartingATunnelTakesDownWhatARunThatIsGoneStarted()
    {
        using StandIn standIn = StandIn.Create().Start();

        (int ownerPid, long ownerStartTicks) = DepartedRun();
        ConnectorLedger.Record(ownerPid, ownerStartTicks, standIn.Process!.Id);

        using (var tunnel = NewTunnel())
        {
            string? address = await tunnel.StartAsync(5099);

            Assert.NotNull(address);
        }

        Assert.True(standIn.WaitForExit(15_000),
            "A connector noted by a run that is no longer there should have been taken down.");
    }

    [Fact]
    public async Task StartingATunnelLeavesTheConnectorsOfARunStillGoing()
    {
        using StandIn standIn = StandIn.Create().Start();
        ConnectorLedger.Record(standIn.Process!.Id);

        using (var tunnel = NewTunnel())
        {
            await tunnel.StartAsync(5101);
        }

        Assert.False(standIn.Process!.HasExited,
            "A connector belonging to a run that is still going must be left alone.");
    }

    [Fact]
    public async Task ALaunchIsNotedBeforeItsFirstLineArrives()
    {
        // Cleared first, so the note that follows can only be the one this launch
        // wrote. Without this the connector would be unreachable to the next run
        // and the whole note would be decoration.
        try { File.Delete(ConnectorLedger.NoteForThisProcess); }
        catch { /* nothing there to clear */ }

        using StandIn standIn = StandIn.Create();

        QuickTunnel.IConnection connection = await QuickTunnel.LaunchAsync(
            standIn.Executable, standIn.Arguments, _ => { });

        try
        {
            Assert.True(File.Exists(ConnectorLedger.NoteForThisProcess),
                "The connector should have been noted before it produced anything.");

            string note = await File.ReadAllTextAsync(ConnectorLedger.NoteForThisProcess);
            Assert.True(note.Trim().Length > 0, "The note for this run was left empty.");
        }
        finally
        {
            try { connection.Kill(); } catch { /* already gone */ }
            connection.Dispose();
        }
    }

    /// <summary>
    /// A run that was handed an id this process now holds is not this run, and its
    /// connectors are as orphaned as any other. Windows gives ids back quickly, so
    /// the start time is what keeps a fresh run from inheriting a predecessor's
    /// leftovers by accident.
    /// </summary>
    [Fact]
    public async Task AProcessHandedTheIdOfARunThatIsGoneIsNotTakenForThatRun()
    {
        using StandIn standIn = StandIn.Create().Start();

        long thisRun = CurrentStartTicks();
        ConnectorLedger.Record(Environment.ProcessId, thisRun - 1, standIn.Process!.Id);

        using (var tunnel = NewTunnel())
        {
            await tunnel.StartAsync(5103);
        }

        Assert.True(standIn.WaitForExit(15_000),
            "A note written under a start time this process does not have should have been reaped.");
    }

    private static QuickTunnel NewTunnel() =>
        new(
            (toolPath, arguments, onLine) =>
            {
                // An address straight away, so the start finishes without waiting out
                // a timeout that would only be measuring the clock.
                onLine("https://leftover-cleaner-brocchetti.trycloudflare.com");
                return Task.FromResult<QuickTunnel.IConnection>(new SilentConnection());
            },
            () => Task.FromResult<string?>("cloudflared"),
            startTimeoutMs: 5_000);

    /// <summary>A process that has finished, and the id and start time it had.</summary>
    private static (int Pid, long StartTicks) DepartedRun()
    {
        // The start time can only be read while the process is still there. A
        // Windows process object keeps answering long after the process has gone,
        // but on Unix .NET forgets it the moment it sees the exit and refuses with
        // an error that reads nothing like "this run has left". So the run is given
        // a moment in which it can be measured before it departs.
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sh",
            Arguments = OperatingSystem.IsWindows() ? "/c exit 0" : "-c \"sleep 1; exit 0\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start a process to depart.");

        int pid = process.Id;
        long startTicks = process.StartTime.Ticks;
        process.WaitForExit();
        return (pid, startTicks);
    }

    private static long CurrentStartTicks()
    {
        using var current = Process.GetCurrentProcess();
        return current.StartTime.Ticks;
    }

    /// <summary>
    /// A process that answers to the connector's name, made by giving a harmless
    /// tool that name. Killing it for any other reason than its name would make the
    /// filter in the reaper untested.
    /// </summary>
    private sealed class StandIn : IDisposable
    {
        private readonly string _directory;

        public string Executable { get; }
        public string Arguments { get; }
        public Process? Process { get; private set; }

        private StandIn(string directory, string executable, string arguments)
        {
            _directory = directory;
            Executable = executable;
            Arguments = arguments;
        }

        public static StandIn Create()
        {
            bool windows = OperatingSystem.IsWindows();
            string source = windows
                ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
                : File.Exists("/bin/sleep") ? "/bin/sleep" : "/usr/bin/sleep";

            string directory = Path.Combine(Path.GetTempPath(), "pg-connector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            string executable = Path.Combine(directory, ToolInstallerService.CloudflaredExecutableName);
            File.Copy(source, executable);

            if (!windows)
            {
                File.SetUnixFileMode(executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return new StandIn(
                directory,
                executable,
                windows ? "/c ping -n 300 127.0.0.1 > nul" : "300");
        }

        public StandIn Start()
        {
            Process = Process.Start(new ProcessStartInfo
            {
                FileName = Executable,
                Arguments = Arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not start the stand-in connector.");

            return this;
        }

        public bool WaitForExit(int milliseconds) => Process?.WaitForExit(milliseconds) ?? true;

        public void Dispose()
        {
            try
            {
                if (Process is { HasExited: false }) Process.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }

            try { Process?.Dispose(); } catch { /* already gone */ }
            try { Directory.Delete(_directory, recursive: true); } catch { /* held open elsewhere */ }
        }
    }

    /// <summary>A connector that never says anything and never leaves on its own.</summary>
    private sealed class SilentConnection : QuickTunnel.IConnection
    {
        private readonly TaskCompletionSource<bool> _gone =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Exited => _gone.Task;

        public void Kill() => _gone.TrySetResult(true);

        public void Dispose() => _gone.TrySetResult(true);
    }
}
