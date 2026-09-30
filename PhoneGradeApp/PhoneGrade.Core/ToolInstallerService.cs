using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneGrade.Core;

public static class ToolInstallerService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<bool> ExecuteFixAsync(string actionKey, IProgress<(int Percent, string Message)>? progress = null)
    {
        return actionKey switch
        {
            "install_idevice_tools" => await InstallIdeviceToolsAsync(progress),
            "install_adb" => await InstallAdbAsync(progress),
            "fix_apple_service" or "install_apple_driver" => await FixAppleServiceAsync(progress),
            "start_usbmuxd" => await StartUsbmuxdAsync(progress),
            _ => false
        };
    }

    private static async Task<string?> GetLatestLibimobiledeviceAssetUrlAsync(string platform)
    {
        try
        {
            string apiUrl = "https://api.github.com/repos/libimobiledevice-win32/imobiledevice-net/releases/latest";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("User-Agent", "PhoneGrade-Auto-Installer");
            
            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assets)) return null;

            string? targetPattern = platform switch
            {
                "win-x64" => "libimobiledevice.*-win-x64.zip",
                "osx-x64" => "libimobiledevice.*-osx-x64.zip",
                _ => null
            };

            if (targetPattern == null) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) &&
                    asset.TryGetProperty("browser_download_url", out var url))
                {
                    string assetName = name.GetString() ?? "";
                    if (System.Text.RegularExpressions.Regex.IsMatch(assetName, targetPattern))
                    {
                        return url.GetString();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> InstallIdeviceToolsAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        SystemEventLogger.Info(LogSource.Desktop, "Starting libimobiledevice tools installation...");
        progress?.Report((10, "Preparing tools directory..."));

        string toolsDir = ToolRunner.ToolsDir;
        Directory.CreateDirectory(toolsDir);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                progress?.Report((20, "Fetching latest libimobiledevice release from GitHub..."));
                string? zipUrl = await GetLatestLibimobiledeviceAssetUrlAsync("win-x64");

                if (string.IsNullOrEmpty(zipUrl))
                {
                    progress?.Report((100, "Could not fetch latest release. Check network connection."));
                    return false;
                }

                progress?.Report((30, "Downloading latest libimobiledevice suite for Windows..."));
                string tempZip = Path.Combine(Path.GetTempPath(), $"idevice-tools-{Guid.NewGuid():N}.zip");
                
                await DownloadFileWithProgressAsync(zipUrl, tempZip, progress, 30, 75);

                progress?.Report((80, "Extracting tools to idevice-tools directory..."));
                ZipFile.ExtractToDirectory(tempZip, toolsDir, overwriteFiles: true);

                try { File.Delete(tempZip); } catch { }

                ToolRunner.EnsureToolPermissions(toolsDir);

                // Auto-start portable usbmuxd daemon on Windows if present
                await EnsurePortableUsbmuxdRunningAsync();

                progress?.Report((100, "libimobiledevice tools installed successfully."));
                SystemEventLogger.Info(LogSource.Desktop, $"libimobiledevice tools installed in: {toolsDir}");
                return true;
            }
            catch (Exception ex)
            {
                SystemEventLogger.Error(LogSource.Desktop, $"Failed to install libimobiledevice tools on Windows: {ex.Message}");
                progress?.Report((100, $"Installation failed: {ex.Message}"));
                return false;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            try
            {
                progress?.Report((25, "Checking Homebrew package manager..."));
                var (brewPath, _, brewCode) = await ToolRunner.ExecuteAsync("which", "brew");
                if (brewCode == 0 && !string.IsNullOrWhiteSpace(brewPath))
                {
                    progress?.Report((50, "Running: brew install libimobiledevice..."));
                    var (outStr, errStr, code) = await ToolRunner.ExecuteAsync("brew", "install libimobiledevice", 180_000);
                    if (code == 0)
                    {
                        progress?.Report((100, "libimobiledevice installed via Homebrew."));
                        SystemEventLogger.Info(LogSource.Desktop, "libimobiledevice installed via Homebrew.");
                        return true;
                    }
                }

                // Fallback: download macOS portable precompiled binaries
                progress?.Report((40, "Fetching latest libimobiledevice release for macOS..."));
                string? macZipUrl = await GetLatestLibimobiledeviceAssetUrlAsync("osx-x64");
                
                if (string.IsNullOrEmpty(macZipUrl))
                {
                    progress?.Report((100, "Could not fetch latest macOS release."));
                    return false;
                }

                progress?.Report((55, "Downloading macOS portable libimobiledevice bundle..."));
                string tempZip = Path.Combine(Path.GetTempPath(), $"idevice-tools-mac-{Guid.NewGuid():N}.zip");
                
                await DownloadFileWithProgressAsync(macZipUrl, tempZip, progress, 55, 85);
                ZipFile.ExtractToDirectory(tempZip, toolsDir, overwriteFiles: true);
                try { File.Delete(tempZip); } catch { }

                ToolRunner.EnsureToolPermissions(toolsDir);
                progress?.Report((100, "libimobiledevice portable tools installed for macOS."));
                return true;
            }
            catch (Exception ex)
            {
                progress?.Report((100, $"Error: {ex.Message}"));
                return false;
            }
        }
        else
        {
            // Linux
            try
            {
                progress?.Report((30, "Attempting apt package install..."));
                var (outStr, errStr, code) = await ToolRunner.ExecuteAsync("pkexec", "apt-get install -y libimobiledevice-utils usbmuxd", 120_000);
                if (code == 0)
                {
                    progress?.Report((100, "Installed libimobiledevice-utils and usbmuxd successfully."));
                    return true;
                }
                
                progress?.Report((100, "Manual install required: sudo apt-get install libimobiledevice-utils usbmuxd"));
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report((100, $"Error: {ex.Message}"));
                return false;
            }
        }
    }

    public static async Task<bool> InstallAdbAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        SystemEventLogger.Info(LogSource.Desktop, "Starting Android Platform-Tools (adb) installation...");
        progress?.Report((10, "Preparing platform-tools download..."));

        string osTag = OperatingSystem.IsWindows() ? "windows" : (OperatingSystem.IsMacOS() ? "darwin" : "linux");
        string url = $"https://dl.google.com/android/repository/platform-tools-latest-{osTag}.zip";

        string toolsDir = ToolRunner.ToolsDir;
        Directory.CreateDirectory(toolsDir);

        try
        {
            string tempZip = Path.Combine(Path.GetTempPath(), $"platform-tools-{Guid.NewGuid():N}.zip");
            await DownloadFileWithProgressAsync(url, tempZip, progress, 20, 75);

            progress?.Report((80, "Extracting adb to idevice-tools directory..."));
            string extractDir = Path.Combine(Path.GetTempPath(), $"platform-tools-extract-{Guid.NewGuid():N}");
            ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);

            string sourcePlatformTools = Path.Combine(extractDir, "platform-tools");
            if (Directory.Exists(sourcePlatformTools))
            {
                foreach (var file in Directory.GetFiles(sourcePlatformTools))
                {
                    string dest = Path.Combine(toolsDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                }
            }

            try { File.Delete(tempZip); Directory.Delete(extractDir, true); } catch { }

            ToolRunner.EnsureToolPermissions(toolsDir);

            progress?.Report((100, "Android adb installed successfully."));
            SystemEventLogger.Info(LogSource.Desktop, "adb installed into tools directory.");
            return true;
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.Desktop, $"Failed to install adb: {ex.Message}");
            progress?.Report((100, $"adb install failed: {ex.Message}"));
            return false;
        }
    }

    /// <summary>Executable name of the tunnel connector on the running platform.</summary>
    public static string CloudflaredExecutableName => OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared";

    private static readonly object CloudflaredSync = new();
    private static Task<string?>? CloudflaredPending;

    /// <summary>
    /// The cloudflared release asset for a platform, or null when Cloudflare ships
    /// nothing that runs there. Windows on arm has no arm64 build of its own and
    /// runs the amd64 one through emulation instead.
    /// </summary>
    public static string? CloudflaredAssetName(bool isWindows, bool isMac, Architecture architecture)
    {
        if (isWindows)
        {
            return architecture switch
            {
                Architecture.X64 or Architecture.Arm64 => "cloudflared-windows-amd64.exe",
                Architecture.X86 => "cloudflared-windows-386.exe",
                _ => null
            };
        }

        if (isMac)
        {
            return architecture switch
            {
                Architecture.X64 => "cloudflared-darwin-amd64.tgz",
                Architecture.Arm64 => "cloudflared-darwin-arm64.tgz",
                _ => null
            };
        }

        return architecture switch
        {
            Architecture.X64 => "cloudflared-linux-amd64",
            Architecture.Arm64 => "cloudflared-linux-arm64",
            Architecture.Arm => "cloudflared-linux-arm",
            Architecture.X86 => "cloudflared-linux-386",
            _ => null
        };
    }

    /// <summary>
    /// A usable tunnel connector: one already sitting in the tools directory, one
    /// found on the system path, or one downloaded now. Null means the phone has to
    /// do without a public https address, which is an ordinary fallback and not an
    /// error worth reporting.
    /// </summary>
    public static async Task<string?> EnsureCloudflaredAsync()
    {
        string installed = Path.Combine(ToolRunner.ToolsDir, CloudflaredExecutableName);
        if (File.Exists(installed)) return installed;

        string resolved = ToolRunner.Resolve("cloudflared");
        if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved)) return resolved;

        // Two phones in a row must not start two downloads of the same binary.
        Task<string?> download;
        lock (CloudflaredSync)
        {
            download = CloudflaredPending ??= DownloadCloudflaredAsync();
        }

        try
        {
            return await download;
        }
        finally
        {
            lock (CloudflaredSync)
            {
                if (download.IsCompleted) CloudflaredPending = null;
            }
        }
    }

    private static async Task<string?> DownloadCloudflaredAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        string? asset = CloudflaredAssetName(
            OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.OSArchitecture);

        if (asset == null)
        {
            SystemEventLogger.Warning(LogSource.Desktop,
                $"No cloudflared build for {RuntimeInformation.OSArchitecture} on this platform.");
            return null;
        }

        string target = Path.Combine(ToolRunner.ToolsDir, CloudflaredExecutableName);
        if (File.Exists(target)) return target;

        SystemEventLogger.Info(LogSource.Desktop, "Downloading the tunnel connector for the first time...");
        string url = $"https://github.com/cloudflare/cloudflared/releases/latest/download/{asset}";
        string temp = Path.Combine(Path.GetTempPath(), $"cloudflared-{Guid.NewGuid():N}");

        try
        {
            await DownloadFileWithProgressAsync(url, temp, progress, 20, 100);
            Directory.CreateDirectory(ToolRunner.ToolsDir);

            if (asset.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTarGz(temp, target);
            }
            else
            {
                File.Copy(temp, target, overwrite: true);
            }

            try { File.Delete(temp); } catch { }

            ToolRunner.EnsureToolPermissions(ToolRunner.ToolsDir);
            SystemEventLogger.Info(LogSource.Desktop, $"Tunnel connector installed in: {target}");
            return target;
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.Desktop, $"Failed to install the tunnel connector: {ex.Message}");
            try { File.Delete(temp); } catch { }
            return null;
        }
    }

    /// <summary>Unpacks a gzipped tarball that holds a single executable.</summary>
    private static void ExtractTarGz(string archivePath, string targetPath)
    {
        string unpack = Path.Combine(Path.GetTempPath(), $"cloudflared-unpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(unpack);

        try
        {
            using (var compressed = File.OpenRead(archivePath))
            using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gzip, unpack, overwriteFiles: true);
            }

            string[] binaries = Directory.GetFiles(unpack, CloudflaredExecutableName, SearchOption.AllDirectories);
            if (binaries.Length == 0)
            {
                throw new InvalidDataException("The tunnel connector archive held no executable.");
            }

            File.Copy(binaries[0], targetPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(unpack, recursive: true); } catch { }
        }
    }

    public static async Task<bool> FixAppleServiceAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return await StartUsbmuxdAsync(progress);
        }

        // 1. Try starting the standard Windows service first if present
        progress?.Report((15, "Checking Apple Mobile Device Service..."));
        var (startOut, _, startCode) = await ToolRunner.ExecuteAsync("sc", "start AppleMobileDeviceService", 5000);
        if (startCode == 0 || startOut.Contains("START_PENDING") || startOut.Contains("RUNNING"))
        {
            progress?.Report((100, "Apple Mobile Device Service started successfully."));
            SystemEventLogger.Info(LogSource.Desktop, "Apple Mobile Device Service started.");
            return true;
        }

        // 2. Install lightweight Apple USB Driver from Microsoft Update Catalog (88 KB CAB)
        progress?.Report((30, "Downloading lightweight Apple USB Driver (Microsoft Update Catalog 88KB)..."));
        string destFolder = Path.Combine(Path.GetTempPath(), $"AppleDri_{Guid.NewGuid():N}");
        Directory.CreateDirectory(destFolder);

        try
        {
            string cabUrl1 = "https://catalog.s.download.windowsupdate.com/d/msdownload/update/driver/drvs/2020/11/01d96dfd-2f6f-46f7-8bc3-fd82088996d2_a31ff7000e504855b3fa124bf27b3fe5bc4d0893.cab";
            string cabPath1 = Path.Combine(destFolder, "AppleUSB.cab");

            await DownloadFileWithProgressAsync(cabUrl1, cabPath1, progress, 30, 60);

            progress?.Report((65, "Extracting Apple USB Driver package..."));
            await ToolRunner.ExecuteAsync("expand.exe", $"-F:* \"{cabPath1}\" \"{destFolder}\"", 15000);

            progress?.Report((80, "Installing Apple USB Driver via pnputil..."));
            var (pnpOut, pnpErr, pnpCode) = await ToolRunner.ExecuteAsync("pnputil.exe", $"/add-driver \"{destFolder}\\*.inf\" /install", 30000);

            try { Directory.Delete(destFolder, true); } catch { }

            // 3. Ensure portable usbmuxd is running
            progress?.Report((90, "Starting usbmuxd daemon..."));
            await EnsurePortableUsbmuxdRunningAsync();

            progress?.Report((100, "Apple drivers and USB multiplexer configured successfully."));
            SystemEventLogger.Info(LogSource.Desktop, $"Apple driver installed via pnputil (Code: {pnpCode})");
            return true;
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.Desktop, $"Lightweight driver installation failed: {ex.Message}");
            progress?.Report((100, $"Driver install failed: {ex.Message}"));
            return false;
        }
    }

    public static async Task<bool> StartUsbmuxdAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        progress?.Report((30, "Attempting to restart usbmuxd..."));
        
        if (OperatingSystem.IsWindows())
        {
            return await EnsurePortableUsbmuxdRunningAsync();
        }
        else if (OperatingSystem.IsMacOS())
        {
            var (outStr, errStr, code) = await ToolRunner.ExecuteAsync("sudo", "launchctl kickstart -k system/com.apple.usbmuxd", 5000);
            if (code == 0)
            {
                progress?.Report((100, "usbmuxd restarted successfully."));
                return true;
            }
            progress?.Report((100, $"Failed to restart usbmuxd: {errStr}"));
            return false;
        }
        else if (OperatingSystem.IsLinux())
        {
            var (outStr, errStr, code) = await ToolRunner.ExecuteAsync("pkexec", "systemctl restart usbmuxd", 8000);
            if (code == 0)
            {
                progress?.Report((100, "usbmuxd restarted successfully via systemd."));
                return true;
            }
            progress?.Report((100, $"Could not restart usbmuxd: {errStr}"));
            return false;
        }

        return false;
    }

    private static async Task<bool> EnsurePortableUsbmuxdRunningAsync()
    {
        if (!OperatingSystem.IsWindows()) return true;

        string localUsbmuxd = Path.Combine(ToolRunner.ToolsDir, "usbmuxd.exe");
        if (!File.Exists(localUsbmuxd)) return false;

        // Check if usbmuxd process is already running
        var existing = Process.GetProcessesByName("usbmuxd");
        if (existing.Length > 0) return true;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = localUsbmuxd,
                Arguments = "",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            SystemEventLogger.Info(LogSource.Desktop, "Started portable usbmuxd background process.");
            await Task.Delay(500);
            return true;
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.Desktop, $"Could not start portable usbmuxd: {ex.Message}");
            return false;
        }
    }

    private static async Task DownloadFileWithProgressAsync(
        string url,
        string destinationPath,
        IProgress<(int Percent, string Message)>? progress,
        int startPercent,
        int endPercent)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                double fraction = (double)totalRead / totalBytes.Value;
                int scaled = startPercent + (int)(fraction * (endPercent - startPercent));
                int mbDownloaded = (int)(totalRead / (1024 * 1024));
                int mbTotal = (int)(totalBytes.Value / (1024 * 1024));
                progress?.Report((scaled, $"Downloading: {mbDownloaded}MB / {mbTotal}MB ({scaled}%)..."));
            }
        }
    }
}
