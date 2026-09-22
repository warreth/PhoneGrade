using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using PhoneGrade.Core;

namespace Tests;

public class PwaCommunicationTests
{
    [Fact]
    public async Task PwaRestApi_SubmitSuite_TriggersEventAndUpdatesSummary()
    {
        // 1. Arrange: TestRunnerServer running on active port
        await using var server = new PhoneGrade.UI.Web.TestRunnerServer(5057);
        await server.StartAsync();
        int port = server.BoundPort;
        string baseUrl = $"http://127.0.0.1:{port}";
        using var client = new HttpClient();

        bool suiteCompletedFired = false;
        PhoneGrade.Core.InteractiveTestSuiteResult? receivedPayload = null;

        server.SuiteCompleted += (s, e) =>
        {
            suiteCompletedFired = true;
            receivedPayload = e.Message?.Payload;
        };

        // 2. Step: Final Suite Submit (POST /api/pwa/submit)
        var suitePayload = new PhoneGrade.Core.DeviceSessionMessage
        {
            Type = "suite_complete",
            SessionId = "POW_TEST_SESSION_999",
            Payload = new PhoneGrade.Core.InteractiveTestSuiteResult
            {
                SessionId = "POW_TEST_SESSION_999",
                Platform = "Android",
                Tests = new System.Collections.Generic.List<PhoneGrade.Core.InteractiveTestResult>
                {
                    new PhoneGrade.Core.InteractiveTestResult { Id = "touch_canvas", Name = "Touch Canvas", Status = PhoneGrade.Core.TestStatus.Passed },
                    new PhoneGrade.Core.InteractiveTestResult { Id = "html5_camera", Name = "Camera Capture", Status = PhoneGrade.Core.TestStatus.Passed }
                }
            }
        };
        var submitResp = await client.PostAsync($"{baseUrl}/api/pwa/submit", new StringContent(JsonSerializer.Serialize(suitePayload), Encoding.UTF8, "application/json"));
        Assert.True(submitResp.IsSuccessStatusCode, "Final suite submission failed!");

        // Wait a tiny bit for the async event
        await Task.Delay(100);

        Assert.True(suiteCompletedFired, "Server SuiteCompleted event was not fired!");
        Assert.NotNull(receivedPayload);
        Assert.Equal("Android", receivedPayload.Platform);
        Assert.Equal(2, receivedPayload.Tests.Count);
    }
}