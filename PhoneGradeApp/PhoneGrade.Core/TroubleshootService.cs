using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PhoneGrade.Core;

public enum DiagnosticSeverity
{
    Pass,
    Warning,
    Fail,
    Info
}

public class DiagnosticCheckItem
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
    public string Message { get; set; } = "";
    public string? Resolution { get; set; }
    public string? FixActionKey { get; set; }
    public bool IsFixable => !string.IsNullOrEmpty(FixActionKey);
}

public class TroubleshootReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string OsDescription { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string FrameworkDescription { get; set; } = "";
    public List<DiagnosticCheckItem> Checks { get; set; } = new();
    public List<string> RawUsbDevices { get; set; } = new();
    public string OverallStatus { get; set; } = "";
    public bool CanDetectIos { get; set; }
    public bool CanDetectAndroid { get; set; }

    public string ToFormattedText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PHONEGRADE HARDWARE & DRIVER DIAGNOSTIC REPORT ===");
        sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"OS: {OsDescription} ({Architecture})");
        sb.AppendLine($"Runtime: {FrameworkDescription}");
        sb.AppendLine($"Overall Status: {OverallStatus}");
        sb.AppendLine($"iOS Ready: {(CanDetectIos ? "YES" : "NO")} | Android Ready: {(CanDetectAndroid ? "YES" : "NO")}");
        sb.AppendLine();

        sb.AppendLine("--- DIAGNOSTIC CHECKS ---");
        foreach (var c in Checks)
        {
            string marker = c.Severity switch
            {
                DiagnosticSeverity.Pass => "[PASS]",
                DiagnosticSeverity.Warning => "[WARN]",
                DiagnosticSeverity.Fail => "[FAIL]",
                _ => "[INFO]"
            };
            sb.AppendLine($"{marker} [{c.Category}] {c.Title}");
            sb.AppendLine($"       Detail: {c.Message}");
            if (!string.IsNullOrWhiteSpace(c.Resolution))
            {
                sb.AppendLine($"       Action: {c.Resolution}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("--- DETECTED USB HARDWARE ---");
        if (RawUsbDevices.Count == 0)
        {
            sb.AppendLine("No matching mobile devices discovered on USB bus.");
        }
        else
        {
            foreach (var dev in RawUsbDevices)
            {
                sb.AppendLine($" - {dev}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("=== END OF REPORT ===");
        return sb.ToString();
    }
}

public static class TroubleshootService
{
    public static async Task<TroubleshootReport> RunFullDiagnosticsAsync()
    {
        var report = new TroubleshootReport
        {
            OsDescription = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription
        };

        SystemEventLogger.Info(LogSource.Diagnostic, "Starting comprehensive hardware and driver diagnostic scan...");

        // 1. Tool availability checks
        await CheckIosToolsAsync(report);
        await CheckAndroidToolsAsync(report);

        // 2. Daemon & Service checks
        await CheckDaemonsAsync(report);

        // 3. Low-level USB bus scan
        await CheckUsbSubsystemAsync(report);

        // 4. Determine overall readiness
        bool hasIosFail = report.Checks.Exists(c => c.Category == "iOS" && c.Severity == DiagnosticSeverity.Fail);
        bool hasAndroidFail = report.Checks.Exists(c => c.Category == "Android" && c.Severity == DiagnosticSeverity.Fail);

        report.CanDetectIos = !hasIosFail;
        report.CanDetectAndroid = !hasAndroidFail;

        if (report.CanDetectIos && report.CanDetectAndroid)
        {
            report.OverallStatus = "All diagnostic checks passed. System ready to detect iOS and Android devices.";
        }
        else if (report.CanDetectIos)
        {
            report.OverallStatus = "iOS detection ready. Android detection unavailable (see checks).";
        }
        else if (report.CanDetectAndroid)
        {
            report.OverallStatus = "Android detection ready. iOS detection unavailable (see checks).";
        }
        else
        {
            report.OverallStatus = "Critical drivers or tools missing. Unable to detect devices.";
        }

        SystemEventLogger.Info(LogSource.Diagnostic, $"Diagnostic scan completed: {report.OverallStatus}");

        return report;
    }

    private static async Task CheckIosToolsAsync(TroubleshootReport report)
    {
        // Check idevice_id
        string ideviceIdPath = ToolRunner.Resolve("idevice_id");
        bool fileExists = File.Exists(ideviceIdPath);
        
        var (stdout, stderr, exitCode) = await ToolRunner.ExecuteAsync("idevice_id", "-v", 5000);
        bool ideviceIdOk = exitCode == 0 && !stderr.StartsWith("ERROR:");
        
        if (ideviceIdOk && string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr) && !stderr.StartsWith("ERROR:"))
        {
            // Some tool versions print version info to stderr
            stdout = stderr;
        }

        if (ideviceIdOk || (!stderr.StartsWith("ERROR:") && stdout.Contains("idevice_id")))
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "iOS",
                Title = "idevice_id Executable",
                Severity = DiagnosticSeverity.Pass,
                Message = $"Available at: {ideviceIdPath} (Version: {stdout.Trim()})"
            });
        }
        else
        {
            string resolution;
            if (OperatingSystem.IsWindows())
            {
                resolution = "Place idevice_id.exe into the 'idevice-tools' directory or install libimobiledevice for Windows.";
            }
            else if (OperatingSystem.IsMacOS())
            {
                resolution = "Run: 'brew install libimobiledevice' in Terminal.";
            }
            else
            {
                resolution = "Run: 'sudo apt-get install libimobiledevice-utils' in Terminal.";
            }

            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "iOS",
                Title = "idevice_id Missing",
                Severity = DiagnosticSeverity.Fail,
                Message = $"Could not execute idevice_id. Resolved path: {ideviceIdPath}. Details: {(stderr.StartsWith("ERROR:") ? stderr : $"Exit code {exitCode}")}",
                Resolution = resolution,
                FixActionKey = "install_idevice_tools"
            });
        }

        // Check ideviceinfo
        string ideviceInfoPath = ToolRunner.Resolve("ideviceinfo");
        var (infoOut, infoErr, infoCode) = await ToolRunner.ExecuteAsync("ideviceinfo", "-v", 5000);
        bool ideviceInfoOk = infoCode == 0 && !infoErr.StartsWith("ERROR:");
        
        if (ideviceInfoOk || (!infoErr.StartsWith("ERROR:") && infoOut.Contains("ideviceinfo")))
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "iOS",
                Title = "ideviceinfo Executable",
                Severity = DiagnosticSeverity.Pass,
                Message = $"Available at: {ideviceInfoPath}"
            });
        }
        else
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "iOS",
                Title = "ideviceinfo Missing",
                Severity = DiagnosticSeverity.Warning,
                Message = $"Could not execute ideviceinfo at: {ideviceInfoPath}. Details: {(infoErr.StartsWith("ERROR:") ? infoErr : $"Exit code {infoCode}")}",
                Resolution = "Ensure the complete libimobiledevice suite is installed."
            });
        }
    }

    private static async Task CheckAndroidToolsAsync(TroubleshootReport report)
    {
        string adbPath = ToolRunner.Resolve("adb");
        var (stdout, stderr, exitCode) = await ToolRunner.ExecuteAsync("adb", "version", 5000);

        if (exitCode == 0 && stdout.Contains("Android Debug Bridge"))
        {
            string firstLine = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Android",
                Title = "Android Debug Bridge (adb)",
                Severity = DiagnosticSeverity.Pass,
                Message = $"Available at: {adbPath} ({firstLine})"
            });
        }
        else
        {
            string resolution;
            if (OperatingSystem.IsWindows())
            {
                resolution = "Install Android SDK Platform-Tools or copy adb.exe to idevice-tools directory.";
            }
            else if (OperatingSystem.IsMacOS())
            {
                resolution = "Run: 'brew install android-platform-tools' in Terminal.";
            }
            else
            {
                resolution = "Run: 'sudo apt-get install adb' in Terminal.";
            }

            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Android",
                Title = "adb Executable Missing",
                Severity = DiagnosticSeverity.Warning,
                Message = $"Could not execute adb. Resolved path: {adbPath}",
                Resolution = resolution,
                FixActionKey = "install_adb"
            });
        }
    }

    private static async Task CheckDaemonsAsync(TroubleshootReport report)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Check if AppleMobileDeviceService or portable usbmuxd is running
                bool isUsbmuxdRunning = Process.GetProcessesByName("usbmuxd").Length > 0;
                var (scOut, _, scCode) = await ToolRunner.ExecuteAsync("sc", "query AppleMobileDeviceService", 5000);
                bool isAmdsRunning = scCode == 0 && scOut.Contains("RUNNING");

                if (isAmdsRunning || isUsbmuxdRunning)
                {
                    string activeService = isAmdsRunning ? "Apple Mobile Device Service" : "Portable usbmuxd daemon";
                    report.Checks.Add(new DiagnosticCheckItem
                    {
                        Category = "Service",
                        Title = "Apple Multiplexing Service",
                        Severity = DiagnosticSeverity.Pass,
                        Message = $"{activeService} is active and listening for iOS devices."
                    });
                }
                else
                {
                    string localUsbmuxd = Path.Combine(ToolRunner.ToolsDir, "usbmuxd.exe");
                    string res = File.Exists(localUsbmuxd)
                        ? "Click Fix to start the portable usbmuxd background daemon."
                        : "Click Fix to automatically download the lightweight Apple driver and usbmuxd daemon.";

                    report.Checks.Add(new DiagnosticCheckItem
                    {
                        Category = "Service",
                        Title = "Apple USB Service",
                        Severity = DiagnosticSeverity.Warning,
                        Message = "Neither Apple Mobile Device Service nor usbmuxd daemon is currently active.",
                        Resolution = res,
                        FixActionKey = File.Exists(localUsbmuxd) ? "start_usbmuxd" : "fix_apple_service"
                    });
                }
            }
            catch (Exception ex)
            {
                report.Checks.Add(new DiagnosticCheckItem
                {
                    Category = "Service",
                    Title = "Apple Driver Query",
                    Severity = DiagnosticSeverity.Info,
                    Message = $"Could not query service status: {ex.Message}"
                });
            }
        }
        else
        {
            // macOS or Linux: check usbmuxd socket or process
            bool socketExists = File.Exists("/var/run/usbmuxd");
            var (psOut, _, _) = await ToolRunner.ExecuteAsync("pgrep", "usbmuxd", 3000);
            bool processRunning = !string.IsNullOrWhiteSpace(psOut);

            if (socketExists || processRunning)
            {
                report.Checks.Add(new DiagnosticCheckItem
                {
                    Category = "Service",
                    Title = "usbmuxd Daemon",
                    Severity = DiagnosticSeverity.Pass,
                    Message = $"usbmuxd is active (Socket: {socketExists}, Process PID: {psOut.Trim()})"
                });
            }
            else
            {
                string res = OperatingSystem.IsMacOS()
                    ? "Ensure usbmuxd is running or restart the computer."
                    : "Run: 'sudo systemctl start usbmuxd' or 'sudo usbmuxd -f -v'.";

                report.Checks.Add(new DiagnosticCheckItem
                {
                    Category = "Service",
                    Title = "usbmuxd Not Running",
                    Severity = DiagnosticSeverity.Fail,
                    Message = "usbmuxd daemon socket was not found. iOS USB multiplexing is inactive.",
                    Resolution = res,
                    FixActionKey = "start_usbmuxd"
                });
            }
        }
    }

    private static async Task CheckUsbSubsystemAsync(TroubleshootReport report)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Scan /sys/bus/usb/devices/ for vendor IDs
                string usbDir = "/sys/bus/usb/devices";
                if (Directory.Exists(usbDir))
                {
                    foreach (var dir in Directory.GetDirectories(usbDir))
                    {
                        string idVendorFile = Path.Combine(dir, "idVendor");
                        string idProductFile = Path.Combine(dir, "idProduct");
                        string productFile = Path.Combine(dir, "product");

                        if (File.Exists(idVendorFile))
                        {
                            string vid = (await File.ReadAllTextAsync(idVendorFile)).Trim().ToLowerInvariant();
                            string pid = File.Exists(idProductFile) ? (await File.ReadAllTextAsync(idProductFile)).Trim() : "unknown";
                            string prod = File.Exists(productFile) ? (await File.ReadAllTextAsync(productFile)).Trim() : "";

                            if (vid == "05ac") // Apple Inc.
                            {
                                report.RawUsbDevices.Add($"Apple Device (VID: 05ac, PID: {pid}) - {prod}");
                            }
                            else if (IsKnownAndroidVendor(vid))
                            {
                                report.RawUsbDevices.Add($"Android Device (VID: {vid}, PID: {pid}) - {prod}");
                            }
                        }
                    }
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var (ioregOut, _, code) = await ToolRunner.ExecuteAsync("ioreg", "-p IOUSB -l -w 0", 5000);
                if (code == 0 && !string.IsNullOrEmpty(ioregOut))
                {
                    if (ioregOut.Contains("Apple") || ioregOut.Contains("iPhone") || ioregOut.Contains("iPad"))
                    {
                        report.RawUsbDevices.Add("Apple iOS device detected in IORegistry USB tree.");
                    }
                    if (ioregOut.Contains("Android") || ioregOut.Contains("SAMSUNG") || ioregOut.Contains("Google"))
                    {
                        report.RawUsbDevices.Add("Android device detected in IORegistry USB tree.");
                    }
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                var (pnpOut, _, code) = await ToolRunner.ExecuteAsync("powershell", "-NoProfile -Command \"Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -like '*USB\\VID_05AC*' } | Select-Object -ExpandProperty FriendlyName\"", 8000);
                if (code == 0 && !string.IsNullOrWhiteSpace(pnpOut))
                {
                    foreach (var line in pnpOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        report.RawUsbDevices.Add($"Apple Device: {line.Trim()}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Hardware",
                Title = "USB Bus Scan",
                Severity = DiagnosticSeverity.Info,
                Message = $"USB scan exception: {ex.Message}"
            });
        }

        if (report.RawUsbDevices.Count > 0)
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Hardware",
                Title = "Physical USB Detection",
                Severity = DiagnosticSeverity.Pass,
                Message = $"Found {report.RawUsbDevices.Count} mobile device(s) on USB bus."
            });
        }
        else
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Hardware",
                Title = "Physical USB Detection",
                Severity = DiagnosticSeverity.Warning,
                Message = "No phone detected on physical USB bus.",
                Resolution = "Check physical USB cable, try a different USB port directly on the computer (avoid hubs), unlock device screen, and tap 'Trust' if prompted."
            });
        }
    }

    private static bool IsKnownAndroidVendor(string vid) => vid switch
    {
        "18d1" => true, // Google
        "04e8" => true, // Samsung
        "2717" => true, // Xiaomi
        "12d1" => true, // Huawei
        "22b8" => true, // Motorola
        "0bb4" => true, // HTC
        "1004" => true, // LG
        "2a70" => true, // OnePlus
        "22d9" => true, // OPPO
        "29a9" => true, // Vivo
        _ => false
    };
}
