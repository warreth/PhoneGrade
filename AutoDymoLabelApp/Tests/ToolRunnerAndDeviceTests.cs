using System.IO;
using System.Text;
using AutoDymoLabel.Core;
using AutoDymoLabelApp.UI.ViewModels;
using Xunit;

namespace Tests;

public class ToolRunnerAndDeviceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _logDir;
    private readonly string? _origLogDir;

    public ToolRunnerAndDeviceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"toolrunner-test-{Guid.NewGuid():N}");
        _logDir = Path.Combine(Path.GetTempPath(), $"toolrunner-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_logDir);
        _origLogDir = Environment.GetEnvironmentVariable("AUTODYMO_LOG_DIR");
        Environment.SetEnvironmentVariable("AUTODYMO_LOG_DIR", _logDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUTODYMO_LOG_DIR", _origLogDir);
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); } catch { }
        try { if (Directory.Exists(_logDir)) Directory.Delete(_logDir, true); } catch { }
    }

    [Fact]
    public void ToolRunner_Logging_WritesExecutionDetailsAndRollsOver()
    {
        // Act: Log entries
        ToolRunner.Log("mock_tool", "-arg1 -arg2", 0, "test stdout", "test stderr");

        // Assert: Log file created and contains structured content
        Assert.True(File.Exists(ToolRunner.LogFilePath));
        string logContent = File.ReadAllText(ToolRunner.LogFilePath);
        Assert.Contains("mock_tool", logContent);
        Assert.Contains("-arg1 -arg2", logContent);
        Assert.Contains("Exit: 0", logContent);
        Assert.Contains("Stderr: test stderr", logContent);
    }

    [Fact]
    public void ToolRunner_Logging_RollsOverWhenFileExceedsLimit()
    {
        Directory.CreateDirectory(_logDir);
        // Create 11MB file
        byte[] dummy = new byte[1024 * 1024];
        using (var fs = new FileStream(ToolRunner.LogFilePath, FileMode.Create, FileAccess.Write))
        {
            for (int i = 0; i < 11; i++)
            {
                fs.Write(dummy, 0, dummy.Length);
            }
        }

        // Trigger log
        ToolRunner.Log("tool_rollover", "--test", 0, "out", "err");

        string rolledPath = Path.Combine(_logDir, "toolrunner.log.1");
        Assert.True(File.Exists(rolledPath));
        Assert.True(File.Exists(ToolRunner.LogFilePath));
        Assert.True(new FileInfo(ToolRunner.LogFilePath).Length < 1024 * 1024);
    }

    [Fact]
    public void ToolRunner_EnsurePermissions_AppliesExecutableBitOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        string scriptPath = Path.Combine(_testDir, "dummy_script.sh");
        File.WriteAllText(scriptPath, "#!/bin/sh\necho hello\n");

        // Initially remove execute bit
        var mode = File.GetUnixFileMode(scriptPath);
        mode &= ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        File.SetUnixFileMode(scriptPath, mode);

        // Verify execute bit is off
        var modeBefore = File.GetUnixFileMode(scriptPath);
        Assert.False(modeBefore.HasFlag(UnixFileMode.UserExecute));

        // Act
        ToolRunner.EnsureToolPermissions(_testDir);

        // Assert: execute bit restored
        var modeAfter = File.GetUnixFileMode(scriptPath);
        Assert.True(modeAfter.HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task ToolRunner_ExecuteAsync_SeparatesStdoutAndStderr()
    {
        string tool = OperatingSystem.IsWindows() ? "cmd" : "sh";
        string args = OperatingSystem.IsWindows() ? "/c echo OUT & echo ERR 1>&2" : "-c \"echo OUT; echo ERR >&2\"";

        var (stdout, stderr, exitCode) = await ToolRunner.ExecuteAsync(tool, args);

        Assert.Equal(0, exitCode);
        Assert.Contains("OUT", stdout);
        Assert.Contains("ERR", stderr);
        Assert.DoesNotContain("ERR", stdout);
        Assert.DoesNotContain("OUT", stderr);
    }

    [Fact]
    public async Task ToolRunner_RunAsync_MaintainsBackwardCompatibility()
    {
        string tool = OperatingSystem.IsWindows() ? "cmd" : "sh";
        string args = OperatingSystem.IsWindows() ? "/c echo HELLO" : "-c \"echo HELLO\"";

        var (output, exitCode) = await ToolRunner.RunAsync(tool, args);

        Assert.Equal(0, exitCode);
        Assert.Equal("HELLO", output);
    }

    [Fact]
    public async Task DeviceService_CheckDaemonStatusAsync_ReturnsValidDiagnosis()
    {
        var diagnosis = await DeviceService.CheckDaemonStatusAsync();
        Assert.NotNull(diagnosis);
        Assert.False(string.IsNullOrWhiteSpace(diagnosis.StatusMessage));
    }

    [Fact]
    public async Task DeviceService_ListUdidsSafeAsync_DiagnosesMissingToolsOrDevices()
    {
        var (udids, raw, state) = await DeviceService.ListUdidsSafeAsync();
        Assert.NotNull(udids);
        Assert.NotNull(raw);
        Assert.True(Enum.IsDefined(typeof(DeviceService.ConnectionState), state));
    }

    [Theory]
    [InlineData(DeviceService.ConnectionState.PermissionDenied, "Executable permissions missing")]
    [InlineData(DeviceService.ConnectionState.DriverMissing, "Apple USB Driver missing")]
    [InlineData(DeviceService.ConnectionState.NotTrusted, "Waiting for trust confirmation on device")]
    public void MainWindowViewModel_StatusDiagnosticLabel_MapsStateProperly(DeviceService.ConnectionState state, string expectedSnippet)
    {
        string status = state switch
        {
            DeviceService.ConnectionState.PermissionDenied =>
                "Executable permissions missing: voer chmod +x uit op de tools of controleer Gatekeeper.",
            DeviceService.ConnectionState.DriverMissing =>
                "Apple USB Driver missing: installeer iTunes of Apple Mobile Device Support.",
            DeviceService.ConnectionState.DaemonStopped =>
                OperatingSystem.IsWindows()
                    ? "Apple Mobile Device Service is gestopt: start de Windows service 'Apple Mobile Device Service'."
                    : "usbmuxd daemon draait niet: start usbmuxd via launchctl of systemctl.",
            DeviceService.ConnectionState.NotTrusted =>
                "Waiting for trust confirmation on device: ontgrendel toestel en tik op 'Vertrouwen'.",
            _ =>
                "Geen toestel gevonden. Kabel/poort proberen of toestel ontgrendelen en 'Vertrouwen' tikken."
        };

        Assert.Contains(expectedSnippet, status);
    }
}
