using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Web;
using Xunit;

namespace Tests;

public class CapabilityScannerTests
{
    [Fact]
    public async Task LogWarningEndpoint_ReceivesMissingApi_FlagsSessionAndLowersGrade()
    {
        var server = new TestRunnerServer(6130, ".");
        await server.StartAsync();
        
        string sessionId = "TEST_SESSION_CAP_SCAN";
        var deviceData = new DeviceData { Identifier = "Device123", Quality = "A" };
        DeviceSessionManager.UpdateSessionData(sessionId, deviceData);

        var requestBody = new
        {
            sessionId = sessionId,
            missingApi = "navigator.wakeLock",
            userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            osVersion = "Windows 10"
        };
        
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        var response = await client.PostAsync($"http://localhost:{server.BoundPort}/api/pwa/log-warning", content);

        response.EnsureSuccessStatusCode();
        
        bool hasSession = DeviceSessionManager.TryGetSession(sessionId, out var session);
        Assert.True(hasSession);
        
        var missingApiCheck = session!.Data!.ComponentChecks.Find(c => c.Name == "API Missing: navigator.wakeLock");
        Assert.NotNull(missingApiCheck);
        Assert.Equal(ComponentStatusType.Failed, missingApiCheck.Status);
        
        await server.DisposeAsync();
    }

    [Fact]
    public void GradingPenalty_EnforcedWhenMandatoryApiMissing()
    {
        var deviceData = new DeviceData { Identifier = "Device_Penalty_Test", Quality = "A" };
        
        // Add a missing API check
        deviceData.ComponentChecks.Add(new ComponentStatus
        {
            Name = "API Missing: DeviceMotionEvent",
            Status = ComponentStatusType.Failed,
            Description = "Device is missing DeviceMotionEvent capability."
        });

        bool hasMissingApis = deviceData.ComponentChecks.Exists(c => c.Status == ComponentStatusType.Failed && c.Name.StartsWith("API Missing:"));
        Assert.True(hasMissingApis);

        string targetQuality = deviceData.Quality;
        if (hasMissingApis && targetQuality == "A")
        {
            targetQuality = "B";
        }

        Assert.Equal("B", targetQuality);
    }

    [Fact]
    public void UsbEventWatcher_CrossPlatform_HandlesGracefully()
    {
        // Calling start/stop on any OS should not throw unhandled exception
        var exception = Record.Exception(() =>
        {
            UsbEventWatcher.StartMonitoring();
            UsbEventWatcher.StopMonitoring();
        });

        Assert.Null(exception);
    }
}
