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
        await _server?.DisposeAsync();
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
        var sensorTestContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/SensorTest.js");
        
        // Assert: Verify the permission handling code is present
        Assert.Contains("requestPermission", sensorTestContent);
        Assert.Contains("Permission denied", sensorTestContent);
        Assert.Contains("handleFallbackAndSkip", sensorTestContent);
    }

    // Task 3: GPS Error Recovery
    [Fact]
    public async Task Task3_GPS_ShowsRetryButtonOnPermissionDenied()
    {
        var locationTestContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/LocationTest.js");
        
        // Verify the retry button UI is present
        Assert.Contains("btn-retry-location", locationTestContent);
        Assert.Contains("Retry", locationTestContent);
        Assert.Contains("Permission denied", locationTestContent);
    }

    // Task 3: Display Brightness Integration
    [Fact]
    public async Task Task3_Display_MergesBrightnessCheck()
    {
        var displayTestContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");
        
        // Verify brightness check is in DisplayTest
        Assert.Contains("showBrightnessCheck", displayTestContent);
        Assert.Contains("100 percent", displayTestContent);
        Assert.Contains("Control Center", displayTestContent);
    }

    // Task 3: iOS Fullscreen Banner
    [Fact]
    public async Task Task3_iOS_DisplaysFullscreenBanner()
    {
        var appJsContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify standalone detection
        Assert.Contains("display-mode: standalone", appJsContent);
        Assert.Contains("ios-standalone-banner", appJsContent);
        Assert.Contains("Add to Home Screen", appJsContent);
    }

    // Task 4: Camera WebRTC Stream
    [Fact]
    public async Task Task4_Camera_UsesWebRTCNotFileInput()
    {
        var cameraTestContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
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
        var cameraTestContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
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
        var cameraTestContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
        Assert.Contains("Ensure the lighting is adequate before confirming", cameraTestContent);
        Assert.Contains("torch-overlay", cameraTestContent);
    }

    // Task 5: Automatic Results Sync
    [Fact]
    public async Task Task5_Results_NoExportButton()
    {
        var indexHtml = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");
        
        // Verify Export button is removed
        Assert.DoesNotContain("export-results-btn", indexHtml);
    }

    [Fact]
    public async Task Task5_Results_AutoSyncToServer()
    {
        var appJsContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify auto-sync endpoints
        Assert.Contains("/api/pwa/submit-step", appJsContent);
        Assert.Contains("/api/pwa/submit", appJsContent);
    }

    [Fact]
    public async Task Task5_Results_OfflineFallback()
    {
        var appJsContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify offline queue
        Assert.Contains("pwa_offline_queue", appJsContent);
        Assert.Contains("localStorage", appJsContent);
        Assert.Contains("window.addEventListener('online'", appJsContent);
    }

    // Task 6: USB Event Watcher
    [Fact]
    public async Task Task6_USB_EventWatcherExistsAndIsWindows()
    {
        var usbWatcherPath = "PhoneGradeApp/PhoneGrade.Core/UsbEventWatcher.cs";
        Assert.True(System.IO.File.Exists(usbWatcherPath));
        
        var content = System.IO.File.ReadAllText(usbWatcherPath);
        Assert.Contains("ManagementEventWatcher", content);
        Assert.Contains("UsbDeviceConnected", content);
        Assert.Contains("UsbDeviceDisconnected", content);
        Assert.Contains("IsOSPlatform(OSPlatform.Windows)", content);
    }

    // Task 6: ADB Diagnostic Warning
    [Fact]
    public async Task Task6_ADB_WarningCardInUI()
    {
        var mainWindowPath = "PhoneGradeApp/PhoneGrade.UI/Views/MainWindow.axaml";
        var content = System.IO.File.ReadAllText(mainWindowPath);
        
        // Verify warning card exists
        Assert.Contains("ShowAdbWarning", content);
        Assert.Contains("Android device connected", content);
        Assert.Contains("USB Debugging", content);
        Assert.Contains("RetryAdbDetectionCommand", content);
    }

    // Task 1: Grading Penalty Integration
    [Fact]
    public async Task Task1_Grading_PenalizesGradeAWithMissingApis()
    {
        var mainVmContent = System.IO.File.ReadAllText("PhoneGradeApp/PhoneGrade.UI/ViewModels/MainWindowViewModel.cs");
        
        // Verify grading penalty logic
        Assert.Contains("hasMissingApis", mainVmContent);
        Assert.Contains("ComponentStatusType.Failed", mainVmContent);
        Assert.Contains("API Missing", mainVmContent);
        Assert.Contains("targetQuality == "A"", mainVmContent);
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
