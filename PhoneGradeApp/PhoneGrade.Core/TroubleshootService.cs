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

/// <summary>
/// One line of the diagnostics.
///
/// The words are keyed rather than written out: this assembly knows nothing
/// about the language files, so it hands over a key plus the values to fill in
/// and the view model puts the sentence together when the report arrives.
/// Category stays a plain identifier because it is also how the report decides
/// whether iOS and Android are ready.
/// </summary>
public class DiagnosticCheckItem
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string? TitleKey { get; set; }
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
    public string Message { get; set; } = "";
    public string? MessageKey { get; set; }
    public string[] MessageArgs { get; set; } = Array.Empty<string>();
    public string? Resolution { get; set; }
    public string? ResolutionKey { get; set; }
    public string[] ResolutionArgs { get; set; } = Array.Empty<string>();
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
    public string? OverallStatusKey { get; set; }
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

        // Reaching the phone's browser rather than the phone itself.
        report.Checks.Add(CheckTunnelConnector(ToolInstallerService.FindCloudflared()));

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
            report.OverallStatusKey = "Diag_StatusAllOk";
        }
        else if (report.CanDetectIos)
        {
            report.OverallStatusKey = "Diag_StatusIosOnly";
        }
        else if (report.CanDetectAndroid)
        {
            report.OverallStatusKey = "Diag_StatusAndroidOnly";
        }
        else
        {
            report.OverallStatusKey = "Diag_StatusNone";
        }

        SystemEventLogger.Info(LogSource.Diagnostic, $"Diagnostic scan completed: {report.OverallStatusKey}");

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
                TitleKey = "Diag_TitleIdeviceIdFound",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgAvailableVersion",
                MessageArgs = new[] { ideviceIdPath, stdout.Trim() }
            });
        }
        else
        {
            string resolutionKey;
            if (OperatingSystem.IsWindows())
            {
                resolutionKey = "Diag_ResolveIdeviceWin";
            }
            else if (OperatingSystem.IsMacOS())
            {
                resolutionKey = "Diag_ResolveIdeviceMac";
            }
            else
            {
                resolutionKey = "Diag_ResolveIdeviceLinux";
            }

            bool detailIsOutput = stderr.StartsWith("ERROR:");
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "iOS",
                TitleKey = "Diag_TitleIdeviceIdMissing",
                Severity = DiagnosticSeverity.Fail,
                MessageKey = detailIsOutput ? "Diag_MsgIdeviceIdFailed" : "Diag_MsgIdeviceIdExitCode",
                MessageArgs = new[] { ideviceIdPath, detailIsOutput ? stderr : exitCode.ToString() },
                ResolutionKey = resolutionKey,
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
                TitleKey = "Diag_TitleIdeviceinfoFound",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgAvailable",
                MessageArgs = new[] { ideviceInfoPath }
            });
        }
        else
        {
            bool detailIsOutput = infoErr.StartsWith("ERROR:");
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "iOS",
                TitleKey = "Diag_TitleIdeviceinfoMissing",
                Severity = DiagnosticSeverity.Warning,
                MessageKey = detailIsOutput ? "Diag_MsgIdeviceinfoFailed" : "Diag_MsgIdeviceinfoExitCode",
                MessageArgs = new[] { ideviceInfoPath, detailIsOutput ? infoErr : infoCode.ToString() },
                ResolutionKey = "Diag_ResolveIdeviceinfo"
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
                TitleKey = "Diag_TitleAdbFound",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgAvailableWith",
                MessageArgs = new[] { adbPath, firstLine }
            });
        }
        else
        {
            string resolutionKey;
            if (OperatingSystem.IsWindows())
            {
                resolutionKey = "Diag_ResolveAdbWin";
            }
            else if (OperatingSystem.IsMacOS())
            {
                resolutionKey = "Diag_ResolveAdbMac";
            }
            else
            {
                resolutionKey = "Diag_ResolveAdbLinux";
            }

            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Android",
                TitleKey = "Diag_TitleAdbMissing",
                Severity = DiagnosticSeverity.Warning,
                MessageKey = "Diag_MsgAdbFailed",
                MessageArgs = new[] { adbPath },
                ResolutionKey = resolutionKey,
                FixActionKey = "install_adb"
            });
        }
    }

    /// <summary>
    /// Reports whether this machine can give a phone a secure address.
    ///
    /// A machine without a connector still finds and grades devices over the cable,
    /// so this is a warning and not a failure. What it costs is the camera,
    /// microphone and location on a phone that has no cable route, because those
    /// are handed out only on a secure origin, and that used to arrive as a step
    /// that silently went missing rather than as a reason.
    /// </summary>
    public static DiagnosticCheckItem CheckTunnelConnector(string? connectorPath)
    {
        if (!string.IsNullOrWhiteSpace(connectorPath))
        {
            return new DiagnosticCheckItem
            {
                Category = "Connection",
                TitleKey = "Diag_TitleTunnel",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgAvailable",
                MessageArgs = new[] { connectorPath }
            };
        }

        return new DiagnosticCheckItem
        {
            Category = "Connection",
            TitleKey = "Diag_TitleTunnel",
            Severity = DiagnosticSeverity.Warning,
            MessageKey = "Diag_MsgNoTunnel",
            ResolutionKey = "Diag_ResolveTunnel",
            FixActionKey = "install_cloudflared"
        };
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
                    report.Checks.Add(new DiagnosticCheckItem
                    {
                        Category = "Service",
                        TitleKey = "Diag_TitleAppleMultiplexing",
                        Severity = DiagnosticSeverity.Pass,
                        MessageKey = isAmdsRunning ? "Diag_MsgServiceActiveAmds" : "Diag_MsgServiceActivePortable"
                    });
                }
                else
                {
                    string localUsbmuxd = Path.Combine(ToolRunner.ToolsDir, "usbmuxd.exe");
                    report.Checks.Add(new DiagnosticCheckItem
                    {
                        Category = "Service",
                        TitleKey = "Diag_TitleAppleUsbService",
                        Severity = DiagnosticSeverity.Warning,
                        MessageKey = "Diag_MsgNoService",
                        ResolutionKey = File.Exists(localUsbmuxd)
                            ? "Diag_ResolveUsbmuxdPortable"
                            : "Diag_ResolveAppleService",
                        FixActionKey = File.Exists(localUsbmuxd) ? "start_usbmuxd" : "fix_apple_service"
                    });
                }
            }
            catch (Exception ex)
            {
                report.Checks.Add(new DiagnosticCheckItem
                {
                    Category = "Service",
                    TitleKey = "Diag_TitleAppleDriverQuery",
                    Severity = DiagnosticSeverity.Info,
                    MessageKey = "Diag_MsgServiceQueryFailed",
                    MessageArgs = new[] { ex.Message }
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
                    TitleKey = "Diag_TitleUsbmuxdFound",
                    Severity = DiagnosticSeverity.Pass,
                    MessageKey = socketExists ? "Diag_MsgUsbmuxdSocket" : "Diag_MsgUsbmuxdProcess",
                    MessageArgs = socketExists ? Array.Empty<string>() : new[] { psOut.Trim() }
                });
            }
            else
            {
                report.Checks.Add(new DiagnosticCheckItem
                {
                    Category = "Service",
                    TitleKey = "Diag_TitleUsbmuxdStopped",
                    Severity = DiagnosticSeverity.Fail,
                    MessageKey = "Diag_MsgUsbmuxdStopped",
                    ResolutionKey = OperatingSystem.IsMacOS()
                        ? "Diag_ResolveUsbmuxdMac"
                        : "Diag_ResolveUsbmuxdLinux",
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
                TitleKey = "Diag_TitleUsbBusScan",
                Severity = DiagnosticSeverity.Info,
                MessageKey = "Diag_MsgUsbScanFailed",
                MessageArgs = new[] { ex.Message }
            });
        }

        if (report.RawUsbDevices.Count > 0)
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Hardware",
                TitleKey = "Diag_TitleUsbDetection",
                Severity = DiagnosticSeverity.Pass,
                MessageKey = "Diag_MsgDevicesFound",
                MessageArgs = new[] { report.RawUsbDevices.Count.ToString() }
            });
        }
        else
        {
            report.Checks.Add(new DiagnosticCheckItem
            {
                Category = "Hardware",
                TitleKey = "Diag_TitleUsbDetection",
                Severity = DiagnosticSeverity.Warning,
                MessageKey = "Diag_MsgNoUsbDevice",
                ResolutionKey = "Diag_ResolveUsbCable"
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
