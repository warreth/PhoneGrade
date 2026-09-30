using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core;
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

    /// <summary>
    /// Port 99999 is outside the range netsh accepts, so this call is refused on
    /// every Windows machine, administrator or not, and writes nothing. The refusal
    /// has to come back with netsh's own words attached.
    /// </summary>
    [Fact]
    public void ARefusalCarriesWhatNetshSaid()
    {
        if (!OperatingSystem.IsWindows()) return;

        string ruleName = TestRuleName();

        var (applied, answer) = TestRunnerServer.ApplyWindowsFirewallRule(ruleName, 99999);

        Assert.False(applied, "an invalid port cannot have produced a rule");
        Assert.False(string.IsNullOrWhiteSpace(answer),
            "the refusal came back empty, so nothing could tell the operator what went wrong");
        Assert.False(RuleExists(ruleName), "a refused add may not have written anything");
    }

    /// <summary>
    /// The whole point of checking the exit code: what is reported has to match what
    /// is really in the firewall. netsh refuses without administrator rights, and
    /// before this was checked the log said the rule was in place while the show
    /// command found nothing at all.
    /// </summary>
    [Fact]
    public void TheReportMatchesWhetherTheRuleIsThere()
    {
        if (!OperatingSystem.IsWindows()) return;

        string ruleName = TestRuleName();
        try
        {
            var (applied, answer) = TestRunnerServer.ApplyWindowsFirewallRule(ruleName, 5055);

            Assert.False(string.IsNullOrWhiteSpace(answer));
            Assert.Equal(RuleExists(ruleName), applied);
        }
        finally
        {
            DeleteRule(ruleName);
        }
    }

    [Fact]
    public async Task ARefusalIsNotReportedAsSuccess()
    {
        if (!OperatingSystem.IsWindows()) return;

        SystemEventLogger.ClearLogs();

        await TestRunnerServer.EnsureWindowsFirewallRuleAsync(5055);

        string[] messages = SystemEventLogger.GetRecentLogs().Select(entry => entry.Message).ToArray();

        bool claimedSuccess = messages.Any(message => message.Contains(
            "'PhoneGrade_PWA' ingesteld voor actieve poort", StringComparison.Ordinal));

        if (IsElevated())
        {
            Assert.True(claimedSuccess, "with the rights to write it, the rule has to be set");
            return;
        }

        Assert.False(claimedSuccess,
            "netsh refused without administrator rights, so nothing may say the rule was set");
        Assert.Contains(messages, message => message.Contains(
            "'PhoneGrade_PWA' is niet ingesteld", StringComparison.Ordinal));
    }

    private static string TestRuleName() => $"PhoneGrade_PWA_test_{Guid.NewGuid():N}";

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>Asks Windows whether the rule is really there. The exit code is 0 only when it is.</summary>
    private static bool RuleExists(string ruleName)
    {
        using var process = System.Diagnostics.Process.Start(Netsh($"advfirewall firewall show rule name=\"{ruleName}\""));
        if (process == null) return false;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return false;
        }

        try { Task.WaitAll(new Task[] { stdout, stderr }, 2000); } catch { /* the pipes went with it */ }
        return process.ExitCode == 0;
    }

    /// <summary>Best effort: without administrator rights there is nothing to remove.</summary>
    private static void DeleteRule(string ruleName)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                Netsh($"advfirewall firewall delete rule name=\"{ruleName}\""));
            process?.WaitForExit(3000);
        }
        catch
        {
            // A rule that was never written has nothing to delete.
        }
    }

    private static ProcessStartInfo Netsh(string arguments) => new()
    {
        FileName = "netsh",
        Arguments = arguments,
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
}
