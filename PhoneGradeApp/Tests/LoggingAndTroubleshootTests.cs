using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

public class LoggingAndTroubleshootTests
{
    [Fact]
    public void SystemEventLogger_RingBuffer_StoresAndReplaysLogs()
    {
        SystemEventLogger.ClearLogs();

        SystemEventLogger.Info(LogSource.Desktop, "Test info message 1");
        SystemEventLogger.Warning(LogSource.UsbDetector, "Test warning message 2");
        SystemEventLogger.Error(LogSource.Diagnostic, "Test error message 3");

        var recent = SystemEventLogger.GetRecentLogs();
        Assert.NotNull(recent);
        Assert.True(recent.Count >= 3);

        var lastThree = recent.TakeLast(3).ToList();
        Assert.Equal("Test info message 1", lastThree[0].Message);
        Assert.Equal(PhoneGrade.Core.LogLevel.Info, lastThree[0].Level);
        Assert.Equal(LogSource.Desktop, lastThree[0].Source);

        Assert.Equal("Test warning message 2", lastThree[1].Message);
        Assert.Equal(PhoneGrade.Core.LogLevel.Warning, lastThree[1].Level);

        Assert.Equal("Test error message 3", lastThree[2].Message);
        Assert.Equal(PhoneGrade.Core.LogLevel.Error, lastThree[2].Level);
    }

    [Fact]
    public void SystemEventLogger_LogEventEmitted_FiresEvent()
    {
        LogEvent? received = null;
        EventHandler<LogEvent> handler = (sender, e) => received = e;

        SystemEventLogger.LogEventEmitted += handler;
        try
        {
            SystemEventLogger.Info(LogSource.WebSocket, "Real-time event test", "SESSION_99");
            Assert.NotNull(received);
            Assert.Equal("Real-time event test", received.Message);
            Assert.Equal(LogSource.WebSocket, received.Source);
            Assert.Equal("SESSION_99", received.SessionId);
        }
        finally
        {
            SystemEventLogger.LogEventEmitted -= handler;
        }
    }

    [Fact]
    public void PhoneGradeLogger_MapsCategoriesAndLevelsProperly()
    {
        SystemEventLogger.ClearLogs();

        using var provider = new PhoneGradeLoggerProvider();
        var logger = provider.CreateLogger("PhoneGrade.Core.UsbDetector");

        logger.LogInformation("USB scan initiated");
        logger.LogWarning("Potential timeout on probe");

        var recent = SystemEventLogger.GetRecentLogs();
        var usbLogs = recent.Where(l => l.Source == LogSource.UsbDetector).ToList();
        Assert.NotEmpty(usbLogs);
        Assert.Contains(usbLogs, l => l.Message == "USB scan initiated" && l.Level == PhoneGrade.Core.LogLevel.Info);
        Assert.Contains(usbLogs, l => l.Message == "Potential timeout on probe" && l.Level == PhoneGrade.Core.LogLevel.Warning);
    }

    [Fact]
    public void TroubleshootReport_ToFormattedText_GeneratesCleanReport()
    {
        var report = new TroubleshootReport
        {
            OsDescription = "Linux 6.12.107 (x86_64)",
            Architecture = "X64",
            FrameworkDescription = ".NET 8.0.0",
            OverallStatus = "Critical tools missing.",
            CanDetectIos = false,
            CanDetectAndroid = false
        };

        report.Checks.Add(new DiagnosticCheckItem
        {
            Category = "iOS",
            Title = "idevice_id Missing",
            Severity = DiagnosticSeverity.Fail,
            Message = "Could not find idevice_id executable.",
            Resolution = "Run: sudo apt-get install libimobiledevice-utils"
        });

        report.Checks.Add(new DiagnosticCheckItem
        {
            Category = "Android",
            Title = "adb Available",
            Severity = DiagnosticSeverity.Pass,
            Message = "adb found on PATH."
        });

        string formatted = report.ToFormattedText();

        Assert.Contains("=== PHONEGRADE HARDWARE & DRIVER DIAGNOSTIC REPORT ===", formatted);
        Assert.Contains("OS: Linux 6.12.107 (x86_64) (X64)", formatted);
        Assert.Contains("[FAIL] [iOS] idevice_id Missing", formatted);
        Assert.Contains("Action: Run: sudo apt-get install libimobiledevice-utils", formatted);
        Assert.Contains("[PASS] [Android] adb Available", formatted);
        // Guarantee no emoji or m-dashes
        Assert.DoesNotContain("—", formatted);
        Assert.DoesNotContain("⚠️", formatted);
        Assert.DoesNotContain("❌", formatted);
        Assert.DoesNotContain("✅", formatted);
    }

    [Fact]
    public async Task TroubleshootService_RunsDiagnosticsSuccessfully()
    {
        var report = await TroubleshootService.RunFullDiagnosticsAsync();
        Assert.NotNull(report);
        Assert.NotEmpty(report.OsDescription);
        Assert.NotEmpty(report.Architecture);
        Assert.NotEmpty(report.OverallStatus);
        Assert.NotEmpty(report.Checks);

        // Should check iOS, Android, Service and Hardware categories
        var categories = report.Checks.Select(c => c.Category).Distinct().ToList();
        Assert.Contains("iOS", categories);
        Assert.Contains("Android", categories);
        Assert.Contains("Service", categories);
        Assert.Contains("Hardware", categories);
    }

    [Fact]
    public async Task DeviceService_ListUdidsSafeAsync_DetectsMissingToolsGracefully()
    {
        // When idevice_id and adb are missing on this test container
        var (udids, raw, state) = await DeviceService.ListUdidsSafeAsync();
        Assert.Empty(udids);
        // Either ToolsMissing, DaemonStopped, or NotFound depending on environment
        Assert.True(state == DeviceService.ConnectionState.ToolsMissing ||
                    state == DeviceService.ConnectionState.DaemonStopped ||
                    state == DeviceService.ConnectionState.NotFound);
    }

    [Fact]
    public async Task TroubleshootService_MissingIdeviceId_DoesNotReportFalsePositivePass()
    {
        // On this test machine without idevice_id, ensure it is NOT reported as Pass!
        var report = await TroubleshootService.RunFullDiagnosticsAsync();
        var ideviceCheck = report.Checks.FirstOrDefault(c => c.Title.Contains("idevice_id"));
        
        Assert.NotNull(ideviceCheck);
        // Must be Fail because idevice_id is not present, never Pass with an error message
        Assert.Equal(DiagnosticSeverity.Fail, ideviceCheck.Severity);
        Assert.Equal("install_idevice_tools", ideviceCheck.FixActionKey);
        Assert.True(ideviceCheck.IsFixable);
    }

    [Fact]
    public void DiagnosticCheckItem_FixableProperties_WorkCorrectly()
    {
        var itemWithFix = new DiagnosticCheckItem
        {
            Title = "Missing ADB",
            FixActionKey = "install_adb"
        };
        Assert.True(itemWithFix.IsFixable);

        var itemWithoutFix = new DiagnosticCheckItem
        {
            Title = "Check USB Cable",
            FixActionKey = null
        };
        Assert.False(itemWithoutFix.IsFixable);
    }

    [Fact]
    public async Task ToolInstallerService_HandlesUnknownActionGracefully()
    {
        var progressLog = new System.Collections.Generic.List<(int, string)>();
        var progress = new Progress<(int Percent, string Message)>(p => progressLog.Add(p));

        bool result = await ToolInstallerService.ExecuteFixAsync("unknown_action_key", progress);
        Assert.False(result);
    }

    [Fact]
    public async Task ToolInstallerService_VerifyLiveDownloadUrls()
    {
        // Un-faked, live HTTP HEAD request to ensure the Windows zip and CAB driver URLs exist and are valid.
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        
        // 1. Check libimobiledevice GitHub release asset
        string libiUrl = "https://github.com/libimobiledevice-win32/imobiledevice-net/releases/download/v1.3.17/libimobiledevice.1.2.1-r1122-win-x64.zip";
        using var req1 = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, libiUrl);
        var res1 = await client.SendAsync(req1, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        
        Assert.True(res1.IsSuccessStatusCode || res1.StatusCode == System.Net.HttpStatusCode.Found, 
            $"libimobiledevice URL failed with {(int)res1.StatusCode}");
            
        // 2. Check Microsoft Update Catalog Apple USB Driver CAB (88KB)
        string cabUrl = "https://catalog.s.download.windowsupdate.com/d/msdownload/update/driver/drvs/2020/11/01d96dfd-2f6f-46f7-8bc3-fd82088996d2_a31ff7000e504855b3fa124bf27b3fe5bc4d0893.cab";
        using var req2 = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, cabUrl);
        var res2 = await client.SendAsync(req2, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        
        Assert.True(res2.IsSuccessStatusCode, $"CAB driver URL failed with {(int)res2.StatusCode}");
        long size = res2.Content.Headers.ContentLength ?? 0;
        Assert.True(size > 50_000 && size < 200_000, $"CAB driver size {size} is outside expected 50KB-200KB range");
    }
}
