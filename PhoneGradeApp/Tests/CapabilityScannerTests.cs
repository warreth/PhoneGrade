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

        deviceData.ComponentChecks.Add(new ComponentStatus
        {
            Name = GradePolicy.MissingApiPrefix + " DeviceMotionEvent",
            Status = ComponentStatusType.Failed,
            Description = "Device is missing DeviceMotionEvent capability."
        });

        // The penalty is read from the device's own checks by the production
        // policy, not reimplemented here.
        Assert.True(GradePolicy.HasMissingBrowserApis(deviceData.ComponentChecks));
        Assert.Equal("B", GradePolicy.ApplyMissingApiPenalty(deviceData.Quality, deviceData.ComponentChecks));
    }

    [Fact]
    public async Task LogWarningEndpoint_DoesNotDuplicateTheSameMissingApi()
    {
        // The capability scanner runs on every page load, so a reload must not
        // stack identical failed checks on the session.
        var server = new TestRunnerServer(6131, ".");
        await server.StartAsync();

        string sessionId = "TEST_SESSION_CAP_DEDUPE";
        DeviceSessionManager.UpdateSessionData(sessionId, new DeviceData { Identifier = "Device_Dedupe", Quality = "A" });

        var requestBody = JsonSerializer.Serialize(new
        {
            sessionId,
            missingApi = "navigator.wakeLock",
            userAgent = "Mozilla/5.0",
            osVersion = "Windows 10"
        });

        using var client = new HttpClient();
        for (int i = 0; i < 3; i++)
        {
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://localhost:{server.BoundPort}/api/pwa/log-warning", content);
            response.EnsureSuccessStatusCode();
        }

        Assert.True(DeviceSessionManager.TryGetSession(sessionId, out var session));
        int duplicates = session!.Data!.ComponentChecks
            .Count(c => c.Name == "API Missing: navigator.wakeLock");

        Assert.Equal(1, duplicates);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task LogWarningEndpoint_RejectsMalformedJsonWithoutFailingTheServer()
    {
        // The PWA does not check response.ok, so a bad body must be answered
        // quietly rather than taking the request pipeline down with a 500.
        var server = new TestRunnerServer(6132, ".");
        await server.StartAsync();

        using var client = new HttpClient();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"http://localhost:{server.BoundPort}/api/pwa/log-warning", content);

        Assert.NotEqual(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        // The server must still be serving afterwards.
        var health = await client.GetAsync($"http://localhost:{server.BoundPort}/api/pwa/status");
        health.EnsureSuccessStatusCode();

        await server.DisposeAsync();
    }

    [Fact]
    public async Task LogWarningEndpoint_PermissionDenied_DoesNotCostTheDeviceAGrade()
    {
        // A refused permission prompt means the API is there. Recording it as a
        // missing capability would cap a perfectly good device at grade B.
        var server = new TestRunnerServer(6133, ".");
        await server.StartAsync();

        string sessionId = "TEST_SESSION_CAP_DENIED";
        var deviceData = new DeviceData { Identifier = "Device_Denied", Quality = "A" };
        DeviceSessionManager.UpdateSessionData(sessionId, deviceData);

        var requestBody = JsonSerializer.Serialize(new
        {
            sessionId,
            missingApi = "navigator.geolocation",
            userAgent = "Mozilla/5.0",
            osVersion = "Android 14",
            reason = "denied"
        });

        using var client = new HttpClient();
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://localhost:{server.BoundPort}/api/pwa/log-warning", content);

        response.EnsureSuccessStatusCode();

        Assert.True(DeviceSessionManager.TryGetSession(sessionId, out var session));
        Assert.DoesNotContain(session!.Data!.ComponentChecks,
            c => c.Name == "API Missing: navigator.geolocation");

        // The grade an untouched grade A device would keep.
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", session.Data.ComponentChecks));

        await server.DisposeAsync();
    }

    [Fact]
    public async Task LogWarningEndpoint_SanitisesControlCharactersBeforeLogging()
    {
        // The api value is interpolated into a log line and a component name,
        // so an embedded newline must not be able to forge a second entry.
        var server = new TestRunnerServer(6134, ".");
        await server.StartAsync();

        string sessionId = "TEST_SESSION_CAP_SANITISE";
        DeviceSessionManager.UpdateSessionData(sessionId, new DeviceData { Identifier = "Device_San", Quality = "A" });

        var requestBody = JsonSerializer.Serialize(new
        {
            sessionId,
            missingApi = "evil\nFORGED ENTRY: audit cleared",
            userAgent = "Mozilla/5.0",
            osVersion = "Windows 10"
        });

        using var client = new HttpClient();
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://localhost:{server.BoundPort}/api/pwa/log-warning", content);

        response.EnsureSuccessStatusCode();

        Assert.True(DeviceSessionManager.TryGetSession(sessionId, out var session));
        var check = session!.Data!.ComponentChecks.Single();

        Assert.DoesNotContain('\n', check.Name);
        Assert.DoesNotContain('\r', check.Name);

        await server.DisposeAsync();
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
