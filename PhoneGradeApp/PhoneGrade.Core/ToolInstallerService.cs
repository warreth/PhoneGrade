using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
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
            "fix_apple_service" => await FixAppleServiceAsync(progress),
            "start_usbmuxd" => await StartUsbmuxdAsync(progress),
            _ => false
        };
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
                progress?.Report((25, "Downloading libimobiledevice package for Windows..."));
                // Download Windows precompiled libimobiledevice binary release bundle
                string zipUrl = "https://github.com/libimobiledevice-win32/imobiledevice-net/releases/download/v1.3.17/imobiledevice-x64.zip";
                
                string tempZip = Path.Combine(Path.GetTempPath(), $"idevice-tools-{Guid.NewGuid():N}.zip");
                
                await DownloadFileWithProgressAsync(zipUrl, tempZip, progress, 25, 75);

                progress?.Report((80, "Extracting tools to idevice-tools directory..."));
                ZipFile.ExtractToDirectory(tempZip, toolsDir, overwriteFiles: true);

                try { File.Delete(tempZip); } catch { }

                ToolRunner.EnsureToolPermissions(toolsDir);
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
                progress?.Report((30, "Checking Homebrew package manager..."));
                var (brewPath, _, brewCode) = await ToolRunner.ExecuteAsync("which", "brew");
                if (brewCode == 0 && !string.IsNullOrWhiteSpace(brewPath))
                {
                    progress?.Report((50, "Running: brew install libimobiledevice..."));
                    var (outStr, errStr, code) = await ToolRunner.ExecuteAsync("brew", "install libimobiledevice", 120_000);
                    if (code == 0)
                    {
                        progress?.Report((100, "libimobiledevice installed via Homebrew."));
                        SystemEventLogger.Info(LogSource.Desktop, "libimobiledevice installed via Homebrew.");
                        return true;
                    }
                    progress?.Report((100, $"Homebrew install returned code {code}: {errStr}"));
                    return false;
                }
                else
                {
                    progress?.Report((100, "Homebrew is not installed. Please install Homebrew from https://brew.sh first."));
                    return false;
                }
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

    public static async Task<bool> FixAppleServiceAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return await StartUsbmuxdAsync(progress);
        }

        progress?.Report((20, "Attempting to start Apple Mobile Device Service..."));
        var (startOut, startErr, startCode) = await ToolRunner.ExecuteAsync("sc", "start AppleMobileDeviceService", 8000);
        if (startCode == 0 || startOut.Contains("START_PENDING") || startOut.Contains("RUNNING"))
        {
            progress?.Report((100, "Apple Mobile Device Service started successfully."));
            SystemEventLogger.Info(LogSource.Desktop, "Apple Mobile Device Service started.");
            return true;
        }

        progress?.Report((40, "Apple Mobile Device Service not found. Downloading iTunes 64-bit installer..."));
        
        string installerPath = Path.Combine(Path.GetTempPath(), "iTunes64Setup.exe");

        try
        {
            // Download iTunes 64-bit direct installer
            string directUrl = "https://secure-appldnld.apple.com/itunes12/001-97787-20210421-F0E5A3C2-A2C9-11EB-8B94-F8F615D1A2AC/iTunes64Setup.exe";
            await DownloadFileWithProgressAsync(directUrl, installerPath, progress, 40, 85);

            progress?.Report((90, "Launching Apple Mobile Device Support installer..."));
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            };
            Process.Start(psi);

            progress?.Report((100, "Installer launched. Please complete the installer window to finish setup."));
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report((100, $"Could not download installer: {ex.Message}. Please install iTunes manually."));
            return false;
        }
    }

    public static async Task<bool> StartUsbmuxdAsync(IProgress<(int Percent, string Message)>? progress = null)
    {
        progress?.Report((30, "Attempting to restart usbmuxd..."));
        
        if (OperatingSystem.IsMacOS())
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
