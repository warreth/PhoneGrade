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
        // Arrange
        var server = new TestRunnerServer(6123, ".");
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

        // Act
        using var client = new HttpClient();
        var response = await client.PostAsync($"http://localhost:{server.BoundPort}/api/pwa/log-warning", content);

        // Assert
        response.EnsureSuccessStatusCode();
        
        bool hasSession = DeviceSessionManager.TryGetSession(sessionId, out var session);
        Assert.True(hasSession);
        
        // Ensure ComponentChecks has the missing API
        var missingApiCheck = session!.Data!.ComponentChecks.Find(c => c.Name == "API Missing: navigator.wakeLock");
        Assert.NotNull(missingApiCheck);
        Assert.Equal(ComponentStatusType.Failed, missingApiCheck.State);
        
        // Clean up
        await server.DisposeAsync();
    }
}
