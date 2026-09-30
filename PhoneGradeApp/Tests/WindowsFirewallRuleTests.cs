using System.Diagnostics;
using PhoneGrade.UI.Web;
using Xunit;

namespace Tests;

public class WindowsFirewallRuleTests
{
    /// <summary>
    /// StartAsync is called from the window's own thread, and the rule is two netsh calls
    /// that take about a second between them. Asking for it has to hand the work to
    /// somewhere else and come straight back, otherwise the window sits there while Windows
    /// gets around to the second call.
    /// </summary>
    [Fact]
    public async Task EnsureWindowsFirewallRuleAsync_ComesBackWithoutRunningNetshHere()
    {
        if (!OperatingSystem.IsWindows()) return;

        var stopwatch = Stopwatch.StartNew();
        Task applying = TestRunnerServer.EnsureWindowsFirewallRuleAsync(5098);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 250,
            $"asking for the firewall rule took {stopwatch.ElapsedMilliseconds} ms: netsh ran on the calling thread");

        await applying;
    }
}
