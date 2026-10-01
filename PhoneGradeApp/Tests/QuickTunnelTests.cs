using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the connector that gives a phone without adb a public https address.
///
/// Two kinds of test are deliberately mixed here. The banner cloudflared actually
/// printed is committed as a fixture and parsed, because that format is the part we
/// do not control. The rest starts a real shell and lets an address arrive through
/// a real pipe, because a launcher that only ever returns canned values would hide
/// the reading, the waiting and the killing, which is where this goes wrong.
/// </summary>
public class QuickTunnelTests
{
    private const string SampleAddress = "https://easier-apply-athletic-thin.trycloudflare.com";
    private const string OtherAddress = "https://quiet-marble-otter.trycloudflare.com";

    private static string[] CapturedBanner() =>
        File.ReadAllLines(RepoPath.Get("PhoneGradeApp", "Tests", "Fixtures", "cloudflared-quick-tunnel.log"));

    // ---- the banner cloudflared really printed --------------------------------

    [Fact]
    public void ParseAddress_FindsTheAddressInTheCapturedBanner()
    {
        string? found = CapturedBanner()
            .Select(QuickTunnel.ParseAddress)
            .FirstOrDefault(address => address != null);

        Assert.Equal(SampleAddress, found);
    }

    [Fact]
    public void ParseAddress_LeavesTheOtherAddressesInTheBannerAlone()
    {
        // The disclaimer above the banner links to cloudflare.com and the line that
        // requests the tunnel names the bare domain. Neither is somewhere to send a
        // phone, and there are two more https:// links further down the output.
        string?[] others = CapturedBanner()
            .Where(line => !line.Contains(SampleAddress, StringComparison.Ordinal))
            .Select(QuickTunnel.ParseAddress)
            .ToArray();

        Assert.All(others, address => Assert.Null(address));
    }

    [Fact]
    public void ParseAddress_ReturnsNothingForOrdinaryOutput()
    {
        Assert.Null(QuickTunnel.ParseAddress(null));
        Assert.Null(QuickTunnel.ParseAddress(""));
        Assert.Null(QuickTunnel.ParseAddress("   "));
        Assert.Null(QuickTunnel.ParseAddress("2026-09-30T09:01:03Z INF Version 2026.9.3"));
        Assert.Null(QuickTunnel.ParseAddress("https://www.cloudflare.com/website-terms/"));
        Assert.Null(QuickTunnel.ParseAddress("http://localhost:5055"));
    }

    [Fact]
    public void ParseAddress_HandsBackTheAddressWithNoTrailingSlash()
    {
        // The session url builder appends "/?sessionId=", so a slash here would
        // produce "//?sessionId=" and a page the server does not serve.
        Assert.Equal(OtherAddress, QuickTunnel.ParseAddress($"|  {OtherAddress}/   |"));
    }

    // ---- what the connector is complaining about ----------------------------

    private static string[] ErrorFixture() =>
        File.ReadAllLines(RepoPath.Get("PhoneGradeApp", "Tests", "Fixtures", "cloudflared-connector-error.log"));

    [Fact]
    public void ConnectorLogLevel_LeavesTheHealthyBannerAlone()
    {
        // The banner is the same on every start. Logging it would fill the log and
        // push out the line that says what actually went wrong.
        Assert.All(CapturedBanner(), line => Assert.Null(QuickTunnel.ConnectorLogLevel(line)));
        Assert.All(ErrorFixture().Where(line => !line.Contains(" ERR ", StringComparison.Ordinal)),
            line => Assert.Null(QuickTunnel.ConnectorLogLevel(line)));
    }

    [Fact]
    public void ConnectorLogLevel_FlagsWhatARealConnectorComplainedAbout()
    {
        // Captured by running a connector against a port nothing listened on, which
        // is the failure a customer sees when the desktop server has gone away.
        LogLevel[] faults = ErrorFixture()
            .Select(QuickTunnel.ConnectorLogLevel)
            .Where(level => level != null)
            .Select(level => level!.Value)
            .ToArray();

        Assert.Equal(2, faults.Length);
        Assert.All(faults, level => Assert.Equal(LogLevel.Error, level));
        Assert.Contains(ErrorFixture(), line =>
            line.Contains("Unable to reach the origin service", StringComparison.Ordinal));
    }

    // ---- a real connector process --------------------------------------------

    private static string Shell => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

    /// <summary>Prints an address and then keeps running, as a working connector does.</summary>
    private static string PrintAndWait(string address, bool toStderr) => OperatingSystem.IsWindows()
        ? $"/c \"echo {address}{(toStderr ? " 1>&2" : "")} & ping -n 7 127.0.0.1 > nul\""
        : $"-c \"echo {address}{(toStderr ? " 1>&2" : "")}; sleep 5\"";

    /// <summary>Prints nothing and keeps running, as a connector waiting on the network does.</summary>
    private static string JustWait() => OperatingSystem.IsWindows()
        ? "/c ping -n 7 127.0.0.1 > nul"
        : "-c \"sleep 5\"";

    /// <summary>Runs for hours, as a connector that has to be killed rather than waited for does.</summary>
    private static string SleepLong() => OperatingSystem.IsWindows()
        ? "/c ping -n 10000 127.0.0.1 > nul"
        : "-c \"sleep 3600\"";

    /// <summary>Exits immediately without printing anything.</summary>
    private static string ExitSilently() => OperatingSystem.IsWindows()
        ? "/c exit 1"
        : "-c \"exit 1\"";

    private sealed class Harness
    {
        private readonly string _arguments;

        public int Launches;
        public QuickTunnel.IConnection? Last;
        public QuickTunnel Tunnel { get; }

        public Harness(string arguments, int startTimeoutMs = 15_000)
        {
            _arguments = arguments;
            Tunnel = new QuickTunnel(Launch, () => Task.FromResult<string?>(Shell), startTimeoutMs);
        }

        private async Task<QuickTunnel.IConnection> Launch(string toolPath, string arguments, Action<string> onLine)
        {
            // The arguments QuickTunnel builds are the ones cloudflared wants; here
            // they are swapped for a shell that behaves the way we need it to.
            var connection = await QuickTunnel.LaunchAsync(Shell, _arguments, onLine);

            lock (this)
            {
                Last = connection;
                Launches++;
            }

            return connection;
        }

        /// <summary>Waits for the connector to be gone, and says whether it was.</summary>
        public async Task<bool> IsGoneAsync(int timeoutMs = 15_000)
        {
            QuickTunnel.IConnection? connection;
            lock (this) connection = Last;
            if (connection == null) return false;

            return await Task.WhenAny(connection.Exited, Task.Delay(timeoutMs)) == connection.Exited;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Start_TakesTheAddressFromAShellThatPrintsIt(bool toStderr)
    {
        // cloudflared writes everything to stderr today, but its own documentation
        // claims stdout, so neither stream may be the only one that is read.
        var harness = new Harness(PrintAndWait(OtherAddress, toStderr));

        string? address = await harness.Tunnel.StartAsync(5055);

        Assert.Equal(OtherAddress, address);
        Assert.Equal(1, harness.Launches);

        await harness.Tunnel.StopAsync();
        Assert.True(await harness.IsGoneAsync(), "stopping should kill the connector");
    }

    [Fact]
    public async Task Start_KeepsAConnectorThatIsStillRunning()
    {
        var harness = new Harness(PrintAndWait(OtherAddress, toStderr: true));

        string? first = await harness.Tunnel.StartAsync(5055);
        string? second = await harness.Tunnel.StartAsync(5055);

        Assert.Equal(OtherAddress, first);
        Assert.Equal(OtherAddress, second);
        Assert.Equal(OtherAddress, harness.Tunnel.Address);

        // A second connector would take the address away from the first, so only one
        // may be running for a port at a time.
        Assert.Equal(1, harness.Launches);

        await harness.Tunnel.StopAsync();
        Assert.True(await harness.IsGoneAsync(), "stopping should kill the connector");
    }

    [Fact]
    public async Task Start_GivesUpOnAConnectorThatNeverPrintsAnAddress()
    {
        var harness = new Harness(JustWait(), startTimeoutMs: 900);

        Assert.Null(await harness.Tunnel.StartAsync(5055));

        Assert.True(await harness.IsGoneAsync(),
            "a connector we gave up on must not be left running in the background");
        Assert.Null(harness.Tunnel.Address);
    }

    [Fact]
    public async Task Start_GivesUpOnAConnectorThatExitsFirst()
    {
        var harness = new Harness(ExitSilently(), startTimeoutMs: 15_000);

        Assert.Null(await harness.Tunnel.StartAsync(5055));
        Assert.Null(harness.Tunnel.Address);
        Assert.True(await harness.IsGoneAsync());
    }

    /// <summary>Prints a real complaint from the captured error run and then dies.</summary>
    private static string ComplainAndExit() => OperatingSystem.IsWindows()
        ? "/c \"echo 2026-09-30T14:31:42Z ERR Unable to reach the origin service & exit 1\""
        : "-c \"echo 2026-09-30T14:31:42Z ERR Unable to reach the origin service; exit 1\"";

    [Fact]
    public async Task Start_QuotesWhatTheConnectorSaidWhenItCannotComeUp()
    {
        // A connector that dies leaves the caller on the plain network address. The
        // warning it leaves behind has to carry the reason, otherwise a tunnel that
        // will never work looks exactly like one that is merely slow.
        LogThrottler.Reset();
        SystemEventLogger.ClearLogs();

        var harness = new Harness(ComplainAndExit(), startTimeoutMs: 15_000);
        Assert.Null(await harness.Tunnel.StartAsync(5055));

        var logged = SystemEventLogger.GetRecentLogs()
            .Where(entry => entry.Message.Contains("The internet tunnel did not come up", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(logged, entry => entry.Message.Contains(
            "Unable to reach the origin service", StringComparison.Ordinal));
        Assert.Contains(logged, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Start_PutsWhatTheConnectorComplainedAboutOnTheStatusLine()
    {
        // The log keeps the same sentence, but the operator is looking at the
        // window while a phone waits on the QR code, and the log is a menu away.
        // Without this the window could only ever say the tunnel had not opened.
        var seen = new List<string>();
        var harness = new Harness(ComplainAndExit(), startTimeoutMs: 15_000);

        Assert.Null(await harness.Tunnel.StartAsync(5055, seen.Add));

        Assert.Contains(seen, line => line.Contains(
            "Unable to reach the origin service", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_SaysThereIsNoConnectorRatherThanSilentlyGivingUp()
    {
        // The connector is downloaded on demand, so the download failing is an
        // everyday case. It used to end in one log line and a QR code pointing at
        // the plain network address with no explanation of why.
        var seen = new List<string>();
        var tunnel = new QuickTunnel(
            (_, _, _) => Task.FromResult<QuickTunnel.IConnection>(null!),
            () => Task.FromResult<string?>(null));

        Assert.Null(await tunnel.StartAsync(5055, seen.Add));

        Assert.NotEmpty(seen);
        Assert.Contains(seen, line => line.Contains(
            "tunnelprogramma", StringComparison.Ordinal));
        tunnel.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Start_WritesTheCommandItRan()
    {
        // Without the path and the arguments there is nothing to check when a tunnel
        // refuses to come up: the log said only that it had not come up.
        LogThrottler.Reset();
        SystemEventLogger.ClearLogs();

        var harness = new Harness(PrintAndWait(OtherAddress, toStderr: true));
        Assert.Equal(OtherAddress, await harness.Tunnel.StartAsync(5056));

        var logged = SystemEventLogger.GetRecentLogs().Select(entry => entry.Message).ToArray();

        Assert.Contains(logged, message => message.Contains(
            $"Starting the internet tunnel with {Shell} tunnel --url http://localhost:5056 --no-autoupdate --protocol http2",
            StringComparison.Ordinal));
        Assert.Contains(logged, message => message.Contains(OtherAddress, StringComparison.Ordinal));

        await harness.Tunnel.StopAsync();
    }

    [Fact]
    public async Task Start_ReportsAnAddressThatGoesAway()
    {
        var harness = new Harness(PrintAndWait(OtherAddress, toStderr: true));

        Assert.Equal(OtherAddress, await harness.Tunnel.StartAsync(5055));

        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Tunnel.AddressLost += () => lost.TrySetResult();

        Assert.True(await Task.WhenAny(lost.Task, Task.Delay(30_000)) == lost.Task,
            "an address that disappeared has to be reported, or the QR code keeps pointing at nothing");
        Assert.Null(harness.Tunnel.Address);
    }

    [Fact]
    public async Task Start_DoesNotLaunchAConnectorThatIsNotThere()
    {
        // No connector on the platform, or none that could be installed: the caller
        // falls back to the network address, so nothing may be started in vain.
        var launched = false;
        var tunnel = new QuickTunnel(
            (_, _, _) => { launched = true; return Task.FromResult<QuickTunnel.IConnection>(null!); },
            () => Task.FromResult<string?>(null));

        Assert.Null(await tunnel.StartAsync(5055));
        Assert.False(launched);
        tunnel.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Dispose_WhileAConnectorIsStillStarting_TakesItDown()
    {
        // Closing the window while the tunnel is still coming up is how this ends in
        // practice, because the connector has to be downloaded first and nobody waits
        // for that. The connector that arrives afterwards was never visible to
        // Dispose, so without an explicit check it keeps a public address pointed at
        // a server that has already been shut down, for as long as it feels like
        // running.
        var started = new TaskCompletionSource<QuickTunnel.IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tunnel = new QuickTunnel(
            async (_, _, _) =>
            {
                var connection = await QuickTunnel.LaunchAsync(Shell, SleepLong(), _ => { });
                started.TrySetResult(connection);
                await release.Task;
                return connection;
            },
            () => Task.FromResult<string?>(Shell),
            startTimeoutMs: 60_000);

        var start = tunnel.StartAsync(5055);

        QuickTunnel.IConnection connection = await started.Task;
        tunnel.Dispose();
        release.SetResult();

        // Waiting out the start timeout would also end with the connector killed, so
        // the point is that it happens now instead of in an hour.
        Assert.Same(start, await Task.WhenAny(start, Task.Delay(15_000)));
        Assert.Null(await start);

        Assert.True(await Task.WhenAny(connection.Exited, Task.Delay(15_000)) == connection.Exited,
            "a connector that only arrives after the tunnel was disposed must not be left running");
    }

    // ---- picking the right download -------------------------------------------

    [Fact]
    public async Task EnsureCloudflared_FindsTheConnectorNextToTheOtherTools()
    {
        // The connector is installed on demand, and a phone must never be left
        // waiting on a download because the lookup looked in the wrong place. The
        // file is stood in for here, so this runs without touching the network.
        string local = Path.Combine(ToolRunner.ToolsDir, ToolInstallerService.CloudflaredExecutableName);
        bool stoodIn = false;

        if (!File.Exists(local))
        {
            Directory.CreateDirectory(ToolRunner.ToolsDir);
            await File.WriteAllTextAsync(local, "stood in for a real connector");
            stoodIn = true;
        }

        try
        {
            Assert.Equal(local, await ToolInstallerService.EnsureCloudflaredAsync());
        }
        finally
        {
            if (stoodIn)
            {
                try { File.Delete(local); } catch { /* something else claimed it */ }
            }
        }
    }

    [Theory]
    [InlineData(true, false, Architecture.X64, "cloudflared-windows-amd64.exe")]
    [InlineData(true, false, Architecture.Arm64, "cloudflared-windows-amd64.exe")]
    [InlineData(true, false, Architecture.X86, "cloudflared-windows-386.exe")]
    [InlineData(false, true, Architecture.X64, "cloudflared-darwin-amd64.tgz")]
    [InlineData(false, true, Architecture.Arm64, "cloudflared-darwin-arm64.tgz")]
    [InlineData(false, false, Architecture.X64, "cloudflared-linux-amd64")]
    [InlineData(false, false, Architecture.Arm64, "cloudflared-linux-arm64")]
    [InlineData(false, false, Architecture.Arm, "cloudflared-linux-arm")]
    public void CloudflaredAssetName_CoversEveryPlatformWeShipOn(
        bool isWindows, bool isMac, Architecture architecture, string expected)
    {
        Assert.Equal(expected, ToolInstallerService.CloudflaredAssetName(isWindows, isMac, architecture));
    }

    [Fact]
    public void CloudflaredAssetName_RefusesRatherThanGuesses()
    {
        // A platform Cloudflare does not publish for would otherwise be handed a
        // download that 404s and a phone that quietly loses its secure origin.
        Assert.Null(ToolInstallerService.CloudflaredAssetName(true, false, Architecture.S390x));
        Assert.Null(ToolInstallerService.CloudflaredAssetName(false, true, Architecture.Arm));
    }
}
