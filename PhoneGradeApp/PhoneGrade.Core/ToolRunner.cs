using System.Diagnostics;
using System.Text;

namespace PhoneGrade.Core;

/// <summary>Executes external tools (ideviceinfo &amp; friends), resolving them from the app directory first.</summary>
public static class ToolRunner
{
    /// <summary>Name of the directory (next to the app) that ships the libimobiledevice tools.</summary>
    public const string ToolsDirName = "idevice-tools";

    private static readonly object LogLock = new();
    private static bool _sanitized;
    private const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10MB rolling limit

    public static string ToolsDir =>
        Path.Combine(AppContext.BaseDirectory, ToolsDirName);

    public static string LogDir =>
        Environment.GetEnvironmentVariable("AUTODYMO_LOG_DIR") is { Length: > 0 } customDir
            ? customDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneGrade", "logs");

    public static string LogFilePath => Path.Combine(LogDir, "toolrunner.log");

    /// <summary>Ensures permissions on non-Windows platforms and strips quarantine on macOS.</summary>
    public static void EnsureToolPermissions(string? targetDir = null)
    {
        string dir = targetDir ?? ToolsDir;
        if (!Directory.Exists(dir)) return;

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // chmod +x on all files in targetDir
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var mode = File.GetUnixFileMode(file);
                        mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                        File.SetUnixFileMode(file, mode);
                    }
                    catch
                    {
                        // Fallback to chmod command if SetUnixFileMode is restricted
                        try
                        {
                            using var chmod = Process.Start(new ProcessStartInfo
                            {
                                FileName = "chmod",
                                Arguments = $"+x \"{file}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                            chmod?.WaitForExit(2000);
                        }
                        catch { }
                    }

                    if (OperatingSystem.IsMacOS())
                    {
                        try
                        {
                            using var xattr = Process.Start(new ProcessStartInfo
                            {
                                FileName = "xattr",
                                Arguments = $"-d com.apple.quarantine \"{file}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                            xattr?.WaitForExit(2000);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("EnsurePermissions", dir, -1, "", $"Permission setup failed: {ex.Message}");
            }
        }
    }

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

    /// <summary>Executes a tool and returns structured (Stdout, Stderr, ExitCode).</summary>
    public static async Task<(string Stdout, string Stderr, int ExitCode)> ExecuteAsync(string tool, string arguments, int timeoutMs = 30_000)
    {
        if (!_sanitized)
        {
            EnsureToolPermissions();
            _sanitized = true;
        }

        string resolvedPath = Resolve(tool);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                Log(resolvedPath, arguments, -1, "", "Failed to start process");
                return ("", "ERROR: failed to start process", -1);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                Log(resolvedPath, arguments, -1, "", "Command timed out");
                return ("", "ERROR: command timed out", -1);
            }

            string stdout = (await stdoutTask).Trim();
            string stderr = (await stderrTask).Trim();
            int exitCode = process.ExitCode;

            Log(resolvedPath, arguments, exitCode, stdout, stderr);
            return (stdout, stderr, exitCode);
        }
        catch (Exception ex)
        {
            Log(resolvedPath, arguments, -1, "", ex.Message);
            return ("", $"ERROR: {ex.Message}", -1);
        }
    }

    /// <summary>Backward-compatible wrapper for RunAsync: returns (combinedOutput, exitCode).</summary>
    public static async Task<(string Output, int ExitCode)> RunAsync(string tool, string arguments, int timeoutMs = 30_000)
    {
        var (stdout, stderr, exitCode) = await ExecuteAsync(tool, arguments, timeoutMs);
        if (stderr.StartsWith("ERROR:") && string.IsNullOrEmpty(stdout))
            return (stderr, exitCode);

        string separator = stdout.Length > 0 && stderr.Length > 0 ? Environment.NewLine : "";
        string combined = (stdout + separator + stderr).Trim();
        return (combined.Length == 0 ? "NO OUTPUT" : combined, exitCode);
    }

    /// <summary>True if the libimobiledevice tool set is available (bundled or on PATH).</summary>
    public static bool ToolsAvailable() => File.Exists(Path.Combine(ToolsDir,
        OperatingSystem.IsWindows() ? "idevice_id.exe" : "idevice_id"))
        || !RunAsync("idevice_id", "-l", 5000).GetAwaiter().GetResult().Output.StartsWith("ERROR:");

    /// <summary>Appends execution details to a rolling debug log file.</summary>
    public static void Log(string executable, string arguments, int exitCode, string stdout, string stderr)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                if (File.Exists(LogFilePath))
                {
                    var fileInfo = new FileInfo(LogFilePath);
                    if (fileInfo.Length > MaxLogSizeBytes)
                    {
                        string oldPath = Path.Combine(LogDir, "toolrunner.log.1");
                        File.Delete(oldPath);
                        File.Move(LogFilePath, oldPath);
                    }
                }

                string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Path: {executable} | Args: {arguments} | Exit: {exitCode}"
                             + (string.IsNullOrEmpty(stderr) ? "" : $"{Environment.NewLine}  Stderr: {stderr}")
                             + Environment.NewLine;
                File.AppendAllText(LogFilePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the application
        }
    }
}
