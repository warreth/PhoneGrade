using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Web;
using Xunit;

namespace PhoneGrade.Tests;

public class IntegrationTests_AllTasks : IAsyncLifetime
{
    private TestRunnerServer _server;
    private HttpClient _client;

    public async Task InitializeAsync()
    {
        _server = new TestRunnerServer(6124);
        await _server.StartAsync();
        _client = new HttpClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_server != null)
        {
            await _server.DisposeAsync();
        }
    }

    // Task 1: PWA Capability Scanner & Desktop Logging
    [Fact]
    public async Task Task1_CapabilityScanner_LogsWarningWhenApiMissing()
    {
        // Arrange
        var sessionId = "TEST_CAP_001";
        var request = new
        {
            sessionId = sessionId,
            missingApi = "navigator.geolocation",
            userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)",
            osVersion = "iOS 17.0"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync($"http://localhost:{_server.BoundPort}/api/pwa/log-warning", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    // Task 2: Motion Sensor Permissions
    [Fact]
    public async Task Task2_MotionSensor_HandlesPermissionDenial()
    {
        // Arrange: Verify that SensorTest.js exists and has requestPermission logic
        var sensorTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/SensorTest.js");
        
        // Assert: Verify the permission handling code is present
        Assert.Contains("requestPermission", sensorTestContent);
        Assert.Contains("Permission denied", sensorTestContent);
        Assert.Contains("handleFallbackAndSkip", sensorTestContent);
    }

    // Task 3: GPS Error Recovery
    [Fact]
    public async Task Task3_GPS_ShowsRetryButtonOnPermissionDenied()
    {
        var locationTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/LocationTest.js");

        // The retry button must exist and must be wired up, and a denial must
        // settle the step instead of leaving the suite waiting forever.
        Assert.Contains("btn-retry-location", locationTestContent);
        Assert.Contains("btnRetry.onclick", locationTestContent);
        Assert.Contains("permission denied", locationTestContent);
        Assert.Contains("finish(DENIAL_RETRY_GRACE_MS)", locationTestContent);
    }

    // Task 3: Display Brightness Integration
    [Fact]
    public async Task Task3_Display_MergesBrightnessCheck()
    {
        var displayTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");

        // The brightness gate is still there, and the operator is still told to
        // turn the brightness up first.
        Assert.Contains("showBrightnessCheck", displayTestContent);
        Assert.Contains("helderheid", displayTestContent);
        Assert.Contains("maximaal", displayTestContent);

        // What changed is that it can no longer condemn the display. A phone at a
        // third brightness is a brightness setting, not a broken panel, and
        // recording it as a display failure let a dim phone be graded faulty for
        // it. The only way forward is "yes"; the way out is the runner's skip.
        Assert.DoesNotContain("brightness-no", displayTestContent);
        Assert.DoesNotContain("Scherm niet helder genoeg om dode pixels te beoordelen", displayTestContent);
    }

    // Task 3: Display inspection is per patch, not one verdict for the whole panel
    [Fact]
    public async Task Task3_Display_RecordsAVerdictPerPatch()
    {
        var displayTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");
        var inspectionContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayInspection.js");

        // A stuck subpixel is smaller than the eye resolves at arm's length, so a
        // single pass/fail over the whole panel cannot find one. The step walks a
        // grid and records a verdict per patch per colour.
        Assert.Contains("patchRect", displayTestContent);
        Assert.Contains("setVerdict", displayTestContent);
        Assert.Contains("INSPECTION_COLORS", displayTestContent);

        // The counting has to exist and be the shared version, or the label would
        // still say nothing about where a defect is.
        Assert.Contains("describeDefects", inspectionContent);
        Assert.Contains("isDisplayFaulty", inspectionContent);
        Assert.Contains("inspectedCount", inspectionContent);
    }

    // Task 3: The display step is the only one that escapes the test container
    [Fact]
    public async Task Task3_Display_CleansUpItsFullScreenOverlay()
    {
        var displayTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");

        // The overlay goes on document.body so it can sit above everything, which
        // means the runner clearing the container does not touch it. Without a
        // dispose, a skip or the failsafe in the middle of a colour left a
        // full-screen block of red over the next step.
        Assert.Contains("dispose()", displayTestContent);
        Assert.Contains("removeOverlay", displayTestContent);
        Assert.Contains("document.body.removeChild", displayTestContent);
    }

    // Task 3: iOS Fullscreen Banner
    [Fact]
    public async Task Task3_iOS_DisplaysFullscreenBanner()
    {
        var appJsContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        var indexHtml = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");

        // Verify standalone detection lives in the bootstrap...
        Assert.Contains("display-mode: standalone", appJsContent);
        Assert.Contains("ios-standalone-banner", appJsContent);

        // ...and the banner it reveals is declared in the markup.
        Assert.Contains("ios-standalone-banner", indexHtml);
        Assert.Contains("Add to Home Screen", indexHtml);
    }

    // Task 4: Camera WebRTC Stream
    [Fact]
    public async Task Task4_Camera_UsesWebRTCNotFileInput()
    {
        var cameraTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
        // Verify file input is removed
        Assert.DoesNotContain("<input type='file'", cameraTestContent.Replace("\"", "'"));
        // Verify WebRTC is used
        Assert.Contains("getUserMedia", cameraTestContent);
        Assert.Contains("facingMode", cameraTestContent);
    }

    // Task 4: Photo Review Step
    [Fact]
    public async Task Task4_Camera_HasPhotoReviewWorkflow()
    {
        var cameraTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
        // Verify review buttons
        Assert.Contains("Retake Photo", cameraTestContent);
        Assert.Contains("Use Photo", cameraTestContent);
        // Verify canvas snapshot
        Assert.Contains("canvas", cameraTestContent);
        Assert.Contains("drawImage", cameraTestContent);
    }

    // Task 4: Torch Fallback UI
    [Fact]
    public async Task Task4_Camera_HasTorchFallbackUI()
    {
        var cameraTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
        Assert.Contains("Ensure the lighting is adequate before confirming", cameraTestContent);
        Assert.Contains("torch-overlay", cameraTestContent);
    }

    // Task 5: Automatic Results Sync
    [Fact]
    public async Task Task5_Results_NoExportButton()
    {
        var indexHtml = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");
        
        // Verify Export button is removed
        Assert.DoesNotContain("export-results-btn", indexHtml);
    }

    [Fact]
    public async Task Task5_Results_AutoSyncToServer()
    {
        var appJsContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify auto-sync endpoints
        Assert.Contains("/api/pwa/submit-step", appJsContent);
        Assert.Contains("/api/pwa/submit", appJsContent);
    }

    [Fact]
    public async Task Task5_Results_OfflineFallback()
    {
        var appJsContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify offline queue
        Assert.Contains("pwa_offline_queue", appJsContent);
        Assert.Contains("localStorage", appJsContent);
        Assert.Contains("window.addEventListener('online'", appJsContent);
    }

    // Task 6: USB Event Watcher
    [Fact]
    public void Task6_USB_EventWatcherExistsAndIsWindows()
    {
        // The watcher must only report native monitoring on Windows, where WMI
        // is available. Everywhere else the polling loop stays the source of truth.
        UsbEventWatcher.StopMonitoring();
        UsbEventWatcher.StartMonitoring();

        Assert.Equal(OperatingSystem.IsWindows(), UsbEventWatcher.IsNativeMonitoringActive);

        // Subscribers must be reachable through the public surface the view model uses.
        int connected = 0;
        int disconnected = 0;
        EventHandler onConnected = (_, _) => connected++;
        EventHandler onDisconnected = (_, _) => disconnected++;
        UsbEventWatcher.UsbDeviceConnected += onConnected;
        UsbEventWatcher.UsbDeviceDisconnected += onDisconnected;
        try
        {
            UsbEventWatcher.RaiseConnected();
            UsbEventWatcher.RaiseDisconnected();
        }
        finally
        {
            UsbEventWatcher.UsbDeviceConnected -= onConnected;
            UsbEventWatcher.UsbDeviceDisconnected -= onDisconnected;
            UsbEventWatcher.StopMonitoring();
        }

        Assert.Equal(1, connected);
        Assert.Equal(1, disconnected);
    }

    // Task 6: ADB Diagnostic Warning
    [Fact]
    public void Task6_ADB_WarningCardInUI()
    {
        var content = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");

        // Verify warning card exists
        Assert.Contains("ShowAdbWarning", content);
        Assert.Contains("Android device connected", content);
        Assert.Contains("USB Debugging", content);
        Assert.Contains("RetryAdbDetectionCommand", content);
    }

    // Task 1: Grading Penalty Integration
    [Fact]
    public void Task1_Grading_PenalizesGradeAWithMissingApis()
    {
        var missingApiCheck = new ComponentStatus
        {
            Name = "API Missing: navigator.geolocation",
            Status = ComponentStatusType.Failed,
            Description = "Device is missing navigator.geolocation capability."
        };

        // A grade A device that lost a mandatory browser API drops to B.
        var penalised = GradePolicy.ApplyMissingApiPenalty("A", new[] { missingApiCheck });
        Assert.Equal("B", penalised);

        // The penalty only caps A; the grades below it are unaffected.
        Assert.Equal("B", GradePolicy.ApplyMissingApiPenalty("B", new[] { missingApiCheck }));
        Assert.Equal("C", GradePolicy.ApplyMissingApiPenalty("C", new[] { missingApiCheck }));

        // Without the missing-API check a grade A device keeps its grade.
        var healthyCheck = new ComponentStatus
        {
            Name = "Battery Health",
            Status = ComponentStatusType.Passed
        };
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", new[] { healthyCheck }));
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", Array.Empty<ComponentStatus>()));
    }

    [Fact]
    public void Task1_Grading_IgnoresNonFailedAndUnrelatedChecks()
    {
        // Only failed checks whose name carries the capability-scanner prefix
        // count, so an unrelated failed check cannot cap the grade.
        var unrelated = new ComponentStatus
        {
            Name = "Rear Camera",
            Status = ComponentStatusType.Failed
        };
        Assert.False(GradePolicy.HasMissingBrowserApis(new[] { unrelated }));

        // A detected-but-passed capability is not a penalty either.
        var reportedOk = new ComponentStatus
        {
            Name = "API Missing: navigator.wakeLock",
            Status = ComponentStatusType.Passed
        };
        Assert.False(GradePolicy.HasMissingBrowserApis(new[] { reportedOk }));
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", new[] { reportedOk }));
    }

    // Integration: All endpoints respond
    [Fact]
    public async Task Integration_AllEndpointsRespond()
    {
        var sessionId = "TEST_INTEGRATION";
        var endpoints = new[] 
        { 
            "/api/pwa/handshake",
            "/api/pwa/telemetry", 
            "/api/pwa/log-warning",
            "/api/pwa/submit-step",
            "/api/pwa/submit"
        };

        foreach (var endpoint in endpoints)
        {
            var request = new { sessionId = sessionId };
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            
            var response = await _client.PostAsync($"http://localhost:{_server.BoundPort}{endpoint}", content);
            Assert.True(response.IsSuccessStatusCode, $"Endpoint {endpoint} failed");
        }
    }
}
