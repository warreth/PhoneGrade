using System.Diagnostics;
using System.Text;

namespace AutoDymoLabel.Core;

/// <summary>Executes external tools (ideviceinfo &amp; friends), resolving them from the app directory first.</summary>
public static class ToolRunner
{
    /// <summary>Name of the directory (next to the app) that ships the libimobiledevice tools.</summary>
    public const string ToolsDirName = "idevice-tools";

    public static string ToolsDir =>
        Path.Combine(AppContext.BaseDirectory, ToolsDirName);

    /// <summary>Resolves a tool executable: app-dir/idevice-tools first, then PATH, then bare name.</summary>
    public static string Resolve(string tool)
    {
        string exe = OperatingSystem.IsWindows() ? $"{tool}.exe" : tool;
        string local = Path.Combine(ToolsDir, exe);
        if (File.Exists(local)) return local;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* skip malformed PATH entries */ }
        }
        return tool; // last resort: let the OS resolve it
    }

    /// <summary>Runs a tool and returns (stdout + stderr, exitCode). Never throws for normal failures.</summary>
    public static async Task<(string Output, int ExitCode)> RunAsync(string tool, string arguments, int timeoutMs = 30_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Resolve(tool),
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process is null) return ("ERROR: failed to start process", -1);

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return ("ERROR: command timed out", -1);
            }

            string output = (await stdout).Trim() + (await stderr).Trim();
            return (output.Length == 0 ? "NO OUTPUT" : output, process.ExitCode);
        }
        catch (Exception ex)
        {
            return ($"ERROR: {ex.Message}", -1);
        }
    }

    /// <summary>True if the libimobiledevice tool set is available (bundled or on PATH).</summary>
    public static bool ToolsAvailable() => File.Exists(Path.Combine(ToolsDir,
        OperatingSystem.IsWindows() ? "idevice_id.exe" : "idevice_id"))
        || !RunAsync("idevice_id", "-l", 5000).GetAwaiter().GetResult().Output.StartsWith("ERROR:");
}
