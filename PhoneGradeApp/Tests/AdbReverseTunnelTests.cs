using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the adb reverse tunnel that gives the PWA a secure origin.
///
/// The command log is asserted rather than a return value alone, because the
/// order of the adb calls is the part that decides whether a stale mapping from
/// an earlier run can win.
/// </summary>
public class AdbReverseTunnelTests
{
    private sealed record Call(string Arguments, int ExitCode, string Stdout = "", string Stderr = "");

    private static (AdbReverseTunnel Tunnel, List<Call> Calls) Build(
        params (string Match, int ExitCode, string Stdout, string Stderr)[] script)
    {
        var calls = new List<Call>();
        var tunnel = new AdbReverseTunnel((args, _) =>
        {
            foreach (var step in script)
                if (args.Contains(step.Match, StringComparison.Ordinal))
                    return Task.FromResult((step.Stdout, step.Stderr, step.ExitCode));

            return Task.FromResult(("", "", 1));
        });

        return (tunnel, calls);
    }

    /// <summary>Records every call so the order can be asserted after the fact.</summary>
    private static (AdbReverseTunnel Tunnel, List<string> Log) Recording()
    {
        var log = new List<string>();
        var tunnel = new AdbReverseTunnel((args, _) =>
        {
            lock (log) log.Add(args);
            return Task.FromResult(("", "", 0));
        });

        return (tunnel, log);
    }

    [Fact]
    public async Task Open_BindsTheSamePortOnBothSides()
    {
        var (tunnel, log) = Recording();

        Assert.True(await tunnel.OpenAsync("38091FDJG00EMF", 5055));

        // Both sides have to be the same port: the phone asks its own 5055 and the
        // request has to arrive here on 5055, which is where the server listens.
        Assert.Contains("-s 38091FDJG00EMF reverse tcp:5055 tcp:5055", log);
    }

    [Fact]
    public async Task Open_DropsTheOldMappingFirst()
    {
        var (tunnel, log) = Recording();

        await tunnel.OpenAsync("38091FDJG00EMF", 5055);

        // adb refuses to rebind a port it already holds, and a mapping left over
        // from a previous run would send the phone to whatever listened then.
        int remove = log.FindIndex(a => a.Contains("reverse --remove", StringComparison.Ordinal));
        int add = log.FindIndex(a => a.Contains("reverse tcp:", StringComparison.Ordinal));
        Assert.True(remove >= 0, "the stale mapping was never removed");
        Assert.True(add > remove, "the new mapping was set before the old one was dropped");
    }

    [Fact]
    public async Task Open_FailsWhenTheDeviceIsNotAuthorised()
    {
        // The output adb prints when the handset has not accepted the debugging
        // key, which is the most common reason this stops working.
        var (tunnel, _) = Build(("reverse tcp:", 1, "", "error: device unauthorized"));

        Assert.False(await tunnel.OpenAsync("38091FDJG00EMF", 5055));
    }

    [Fact]
    public async Task Open_RefusesAnEmptySerialOrPort()
    {
        var (tunnel, log) = Recording();

        Assert.False(await tunnel.OpenAsync("", 5055));
        Assert.False(await tunnel.OpenAsync("38091FDJG00EMF", 0));
        Assert.False(await tunnel.OpenAsync("38091FDJG00EMF", -1));
        Assert.Empty(log);
    }

    [Fact]
    public async Task Remove_IsQuietWhenAdbIsMissing()
    {
        var tunnel = new AdbReverseTunnel((_, _) => throw new InvalidOperationException("adb not found"));

        // Nothing to undo is not a problem worth surfacing to the operator.
        await tunnel.RemoveAsync("38091FDJG00EMF", 5055);
    }

    [Fact]
    public async Task IsOpen_ReadsTheMappingList()
    {
        // Verbatim output of "adb reverse --list" on a Pixel 8 Pro with a tunnel
        // open. The first column is the transport, not the serial.
        var (tunnel, _) = Build(("reverse --list", 0,
            "UsbFfs tcp:5055 tcp:5055\n", ""));

        Assert.True(await tunnel.IsOpenAsync("38091FDJG00EMF", 5055));
    }

    [Fact]
    public async Task IsOpen_FalseForADifferentPort()
    {
        // A tunnel left open on the port of a previous run must not be mistaken
        // for a working one on the port in use now.
        var (tunnel, _) = Build(("reverse --list", 0,
            "38091FDJG00EMF tcp:5050 tcp:5050\n", ""));

        Assert.False(await tunnel.IsOpenAsync("38091FDJG00EMF", 5055));
    }

    [Fact]
    public async Task IsOpen_FalseWhenTheListCannotBeRead()
    {
        var (tunnel, _) = Build(("reverse --list", 1, "", "error: no devices/emulators found"));
        Assert.False(await tunnel.IsOpenAsync("38091FDJG00EMF", 5055));

        var throwing = new AdbReverseTunnel((_, _) => throw new InvalidOperationException("gone"));
        Assert.False(await throwing.IsOpenAsync("38091FDJG00EMF", 5055));
    }

    [Theory]
    // A Pixel is an adb serial and gets the tunnel. An iPhone has no adb, and a
    // 40 character Apple udid must never be sent to it.
    [InlineData("38091FDJG00EMF", true)]
    [InlineData("R5CT30XXXXX", true)]
    [InlineData("emulator-5554", true)]
    [InlineData("00008030-001234567890ABCD", false)]
    [InlineData("d841d4a4f1e5a2c9b3e7f6d8a1c0b9e8f7d6c5b4", false)]
    [InlineData("DEMO", false)]
    [InlineData("", false)]
    public void SupportsReverse_SeparatesPixelFromApple(string serial, bool expected)
        => Assert.Equal(expected, AdbReverseTunnel.SupportsReverse(serial));

    [Fact]
    public void SessionUrl_UsesTheTunnelHostWhenAsked()
    {
        // localhost is the one plain address a browser treats as a secure origin,
        // which is the entire reason the tunnel exists.
        string url = QrCodeService.GenerateSessionUrl(AdbReverseTunnel.LoopbackHost, 5055, "38091FDJG00EMF");

        Assert.Equal("http://localhost:5055/?sessionId=38091FDJG00EMF", url);
    }

    [Fact]
    public void SessionUrl_KeepsTheLanHostAsTheFallback()
    {
        // iOS stays on the network address, which is the pre-tunnel behaviour.
        string url = QrCodeService.GenerateSessionUrl("192.168.0.216", 5055, "DEVICE_UDID_123");
        Assert.Equal("http://192.168.0.216:5055/?sessionId=DEVICE_UDID_123", url);
    }

    [Fact]
    public void SessionUrl_FallsBackToLoopbackWithoutAHost()
    {
        Assert.Contains("http://127.0.0.1:5055/", QrCodeService.GenerateSessionUrl("", 5055, "X"));
        Assert.Contains("http://127.0.0.1:5055/", QrCodeService.GenerateSessionUrl("   ", 5055, "X"));
    }
}
