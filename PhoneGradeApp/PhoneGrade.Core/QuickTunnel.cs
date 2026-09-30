using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core;

/// <summary>
/// Puts a public https address in front of the local test server.
///
/// A browser only hands out camera, microphone, motion, orientation, wake lock and
/// geolocation on a secure origin, and it decides that from the address alone:
/// WebKit and Chromium both treat https and localhost as secure and nothing else.
/// "adb reverse" gives an Android handset a localhost, but an iPhone has no
/// equivalent, because usbmuxd only ever lets this machine connect to the device
/// and never the other way round. A quick tunnel is the one way found to hand an
/// arbitrary customer phone a publicly trusted https address without putting
/// anything on it: no profile, no passcode, no trusted root left behind.
///
/// The trade is real and worth stating. The traffic between phone and machine
/// passes Cloudflare, the address changes every time the connector starts, and
/// Cloudflare gives these tunnels no uptime guarantee. The camera and microphone
/// stream never leaves the phone, only the test results do. The connector is
/// therefore never assumed to be there: every start is a fresh attempt and any
/// failure leaves the caller on the plain network address.
/// </summary>
public sealed partial class QuickTunnel : IDisposable
{
    /// <summary>How long the connector may take to print an address.</summary>
    public const int StartTimeoutMs = 45_000;

    /// <summary>How long a connection has to survive before it counts as healthy.</summary>
    private const int StableAfterMs = 60_000;

    /// <summary>Failures allowed inside <see cref="DropWindow"/> before giving up.</summary>
    private const int MaxFailures = 4;

    private static readonly TimeSpan DropWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Starts the connector for the given address and hands over every line it
    /// writes, from either stream. cloudflared prints its banner on stderr and
    /// could move it to stdout in a later version, so both are read.
    /// </summary>
    public delegate Task<IConnection> Launcher(string toolPath, string arguments, Action<string> onLine);

    /// <summary>A running connector.</summary>
    public interface IConnection : IDisposable
    {
        /// <summary>Completes when the connector stops by itself. Never faults.</summary>
        Task Exited { get; }

        void Kill();
    }

    private readonly Launcher _launch;
    private readonly Func<Task<string?>> _tool;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly int _startTimeoutMs;
    private readonly int _stableAfterMs;

    private IConnection? _connection;
    private string? _address;
    private int _port;
    private volatile bool _stopping;
    private volatile bool _disposed;
    private int _failures;
    private DateTimeOffset _lastFailure = DateTimeOffset.MinValue;

    /// <summary>
    /// Raised when an address that had been handed out goes away, so whoever
    /// printed the QR code for it can print another one. It is never raised by a
    /// start that worked, because that answer is published by the caller already.
    /// </summary>
    public event Action? AddressLost;

    /// <summary>
    /// Timeouts are arguments so a test can wait a second instead of three quarters
    /// of a minute; production passes the defaults.
    /// </summary>
    public QuickTunnel(Launcher launch, Func<Task<string?>> tool,
        int startTimeoutMs = StartTimeoutMs, int stableAfterMs = StableAfterMs)
    {
        _launch = launch;
        _tool = tool;
        _startTimeoutMs = startTimeoutMs;
        _stableAfterMs = stableAfterMs;
    }

    /// <summary>The production connector: a cloudflared of our own, installed on demand.</summary>
    public static QuickTunnel CreateDefault() =>
        new(LaunchAsync, ToolInstallerService.EnsureCloudflaredAsync);

    /// <summary>The address the phone should open, or null while there is none.</summary>
    public string? Address
    {
        get { lock (_sync) return _address; }
    }

    /// <summary>
    /// Returns the address for the given port, starting the connector if it is not
    /// already serving one. Null means no public address could be had and the
    /// caller should stay on the network address.
    /// </summary>
    public async Task<string?> StartAsync(int port, Action<string>? onStatus = null)
    {
        if (port <= 0 || _disposed) return null;

        await _gate.WaitAsync();
        try
        {
            if (IsLive(port)) return _address;
            if (IsGivingUp()) return null;

            string? tool = await _tool();
            if (string.IsNullOrWhiteSpace(tool))
            {
                NoteFailure();
                SystemEventLogger.Warning(LogSource.UsbDetector,
                    "No tunnel connector available, the phone gets the plain network address");
                return null;
            }

            onStatus?.Invoke("Beveiligde verbinding via internet opzetten...");

            // The path and the arguments are the first thing to check when a tunnel
            // will not come up, and neither of them was written anywhere before.
            string arguments = Arguments(port);
            SystemEventLogger.Info(LogSource.UsbDetector,
                $"Starting the internet tunnel with {tool} {arguments}");

            var tail = new LineTail();
            var found = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connection = await _launch(tool, arguments, line =>
            {
                tail.Add(line);
                LogLevel? level = ConnectorLogLevel(line);
                if (level != null) SystemEventLogger.Log(level.Value, LogSource.UsbDetector, line);
                string? printed = ParseAddress(line);
                if (printed != null) found.TrySetResult(printed);
            });

            lock (_sync)
            {
                _connection = connection;
                _address = null;
                _port = port;
            }

            if (_disposed)
            {
                // The window went away while the connector was still being started,
                // so it arrived after Dispose looked for something to kill. Without
                // this it would keep a public address pointed at a server that has
                // already been shut down, and nothing would ever take it down.
                Discard(connection);
                return null;
            }

            await Task.WhenAny(found.Task, connection.Exited, Task.Delay(_startTimeoutMs));

            string? address = found.Task.IsCompletedSuccessfully ? await found.Task : null;
            if (address == null)
            {
                // No address in time, or the connector died before printing one.
                Discard(connection);
                NoteFailure();
                SystemEventLogger.Warning(LogSource.UsbDetector,
                    $"The internet tunnel did not come up, the phone gets the plain network address. " +
                    $"The connector said: {tail.Describe()}");
                return null;
            }

            lock (_sync)
            {
                _address = address;
            }

            SystemEventLogger.Info(LogSource.UsbDetector,
                $"Internet tunnel ready, the phone sees the server at {address}");
            _ = WatchAsync(connection);
            return address;
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Stops the connector and forgets the address.</summary>
    public async Task StopAsync()
    {
        if (_disposed) return;

        await _gate.WaitAsync();
        try
        {
            _stopping = true;
            IConnection? connection;
            lock (_sync) connection = _connection;
            Discard(connection);
        }
        finally
        {
            Release();
            _stopping = false;
        }
    }

    public void Dispose()
    {
        // Set before anything is looked at: a start that is still running checks it
        // as soon as its connector exists, and between the two either side may kill
        // the process, which makes leaving one behind impossible.
        _disposed = true;
        _stopping = true;
        IConnection? connection;
        lock (_sync)
        {
            connection = _connection;
            _connection = null;
            _address = null;
        }

        try { connection?.Kill(); } catch { /* already gone */ }
        try { connection?.Dispose(); } catch { /* nothing left to release */ }
        _gate.Dispose();
    }

    /// <summary>
    /// Gives the gate back. The tunnel may have been disposed while this call held
    /// it, and releasing a disposed gate throws, which would turn a clean shutdown
    /// into a failure the caller has to deal with.
    /// </summary>
    private void Release()
    {
        try { _gate.Release(); }
        catch (ObjectDisposedException) { /* disposed while this start held the gate */ }
    }

    /// <summary>
    /// Pulls an address out of one line of connector output. The banner it comes in
    /// is a drawn table that is free to be reformatted, so the address is matched on
    /// its own shape and nothing else from the line is trusted.
    /// </summary>
    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();

    public static string? ParseAddress(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var match = AddressRegex().Match(line);
        if (!match.Success) return null;

        return match.Value.TrimEnd('/');
    }

    /// <summary>
    /// The level a connector line deserves, or null for the banner.
    ///
    /// cloudflared marks each line with a level tag of its own and prints the rest
    /// as a drawn table. That table is the same on every start and would push the
    /// lines that matter out of the log, so only what the connector complains
    /// about is kept.
    /// </summary>
    public static LogLevel? ConnectorLogLevel(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var match = FaultTag().Match(line);
        if (!match.Success) return null;

        return match.Value == "WRN" ? LogLevel.Warning : LogLevel.Error;
    }

    [GeneratedRegex(@"\b(ERR|WRN|FAT)\b")]
    private static partial Regex FaultTag();

    private static string Arguments(int port) =>
        $"tunnel --url http://localhost:{port} --no-autoupdate";

    private bool IsLive(int port)
    {
        lock (_sync)
        {
            return _address != null
                && _port == port
                && _connection != null
                && !_connection.Exited.IsCompleted;
        }
    }

    /// <summary>True once the connector has failed too often to be worth chasing.</summary>
    private bool IsGivingUp()
    {
        lock (_sync)
        {
            if (DateTimeOffset.UtcNow - _lastFailure > DropWindow) _failures = 0;
            return _failures >= MaxFailures;
        }
    }

    private void NoteFailure()
    {
        lock (_sync)
        {
            if (DateTimeOffset.UtcNow - _lastFailure > DropWindow) _failures = 0;
            _lastFailure = DateTimeOffset.UtcNow;
            _failures++;
        }
    }

    /// <summary>Forgets and kills whatever connector is currently attached.</summary>
    private void Discard(IConnection? connection)
    {
        lock (_sync)
        {
            if (_connection != null && !ReferenceEquals(_connection, connection)) return;
            _connection = null;
            _address = null;
        }

        try { connection?.Kill(); } catch { /* already gone */ }
        try { connection?.Dispose(); } catch { /* nothing left to release */ }
    }

    /// <summary>
    /// Notices the connector dying and gives the caller one chance to publish the
    /// new state. A connector that flaps is only chased a few times, because a QR
    /// code that silently points at nothing is worse than one that points at the
    /// plain network address.
    /// </summary>
    private async Task WatchAsync(IConnection connection)
    {
        // A connection that gets this far is healthy, so later drops start over.
        if (await Task.WhenAny(connection.Exited, Task.Delay(_stableAfterMs)) != connection.Exited)
        {
            lock (_sync) _failures = 0;
        }

        await connection.Exited;
        if (_stopping) return;

        lock (_sync)
        {
            if (!ReferenceEquals(_connection, connection)) return;
            _connection = null;
            _address = null;
        }

        try { connection.Dispose(); } catch { /* nothing left to release */ }
        NoteFailure();

        SystemEventLogger.Warning(LogSource.UsbDetector,
            "The internet tunnel dropped, asking for a new address");
        AddressLost?.Invoke();
    }

    /// <summary>
    /// Runs the connector and reports its output line by line. Output is drained
    /// from both streams as it arrives, because a connector left holding a full pipe
    /// stops writing and then looks like a hang.
    /// </summary>
    public static Task<IConnection> LaunchAsync(string toolPath, string arguments, Action<string> onLine)
    {
        var start = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {toolPath}");

        process.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // WaitForExit without an argument also waits for the redirected streams to
        // drain, which is what makes Exited mean the process is really finished.
        var exited = Task.Run(() =>
        {
            try { process.WaitForExit(); }
            catch { /* the handle went away underneath the wait */ }
        });

        return Task.FromResult<IConnection>(new ProcessConnection(process, exited));
    }

    /// <summary>
    /// Keeps the last few connector lines so a start that never got an address can
    /// still say why. A connector that dies without printing a level tag would
    /// otherwise leave behind nothing but a generic warning.
    /// </summary>
    private sealed class LineTail
    {
        private const int Keep = 4;

        private readonly Queue<string> _lines = new();
        private readonly object _sync = new();

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            lock (_sync)
            {
                _lines.Enqueue(line);
                while (_lines.Count > Keep) _lines.Dequeue();
            }
        }

        public string Describe()
        {
            lock (_sync)
            {
                return _lines.Count == 0
                    ? "it printed nothing at all"
                    : string.Join(" | ", _lines);
            }
        }
    }

    private sealed class ProcessConnection : IConnection
    {
        private readonly Process _process;

        public ProcessConnection(Process process, Task exited)
        {
            _process = process;
            Exited = exited;
        }

        /// <summary>
        /// Completes when the process is gone. Awaiting it while the handle is being
        /// torn down can make the wait throw, and a watcher that faults on its own
        /// connection stops watching, so the wait is swallowed here.
        /// </summary>
        public Task Exited { get; }

        public void Kill()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Gone already, or not ours to kill any more.
            }
        }

        public void Dispose()
        {
            try { _process.Dispose(); } catch { /* already disposed */ }
        }
    }
}
