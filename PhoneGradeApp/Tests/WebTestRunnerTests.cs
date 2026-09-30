using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Web;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

public class WebTestRunnerTests : IAsyncLifetime
{
    private TestRunnerServer? _server;

    public async Task InitializeAsync()
    {
        // Create test server on a unique port to avoid conflicts
        _server = new TestRunnerServer(preferredPort: 5060);
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_server != null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>Asks the status endpoint the way the phone does, every two seconds.</summary>
    private async Task<string> GetStatusAsync(string sessionId)
    {
        using var client = new HttpClient();
        return await client.GetStringAsync(
            $"http://127.0.0.1:{_server!.BoundPort}/api/pwa/status?sessionId={sessionId}");
    }

    [Fact]
    public async Task TestRunnerServer_StartsAndAcceptsConnections()
    {
        Assert.NotNull(_server);
        Assert.True(_server!.IsRunning);
        Assert.InRange(_server.BoundPort, 5060, 5070);
    }

    [Fact]
    public async Task StatusEndpoint_HandsOverAQueuedCommandOnce()
    {
        // The phone polls status every two seconds. That poll is the only channel it
        // listens on, so a command left waiting for a client that never opened a
        // socket would never be picked up at all.
        string sessionId = "COMMAND_TEST_123";
        _server!.QueueCommand(new { type = "auto_start_suite", sessionId }, sessionId);

        string first = await GetStatusAsync(sessionId);
        string second = await GetStatusAsync(sessionId);

        Assert.Contains("\"auto_start_suite\"", first);
        Assert.DoesNotContain("\"command\"", second);
    }

    [Fact]
    public async Task StatusEndpoint_LeavesACommandThatIsForAnotherDevice()
    {
        string wanted = "DEVICE_THAT_WANTS_IT";
        string other = "DEVICE_THAT_DOES_NOT";
        _server!.QueueCommand(new { type = "auto_start_suite", sessionId = wanted }, wanted);

        string elsewhere = await GetStatusAsync(other);
        string home = await GetStatusAsync(wanted);

        Assert.DoesNotContain("\"command\"", elsewhere);
        Assert.Contains("\"auto_start_suite\"", home);
    }

    [Fact]
    public async Task StatusEndpoint_StillAnswersWhenNothingIsQueued()
    {
        string status = await GetStatusAsync("NOTHING_QUEUED");

        Assert.Contains("active", status);
        Assert.DoesNotContain("\"command\"", status);
    }

    [Fact]
    public async Task DeviceSessionMessage_SerializationRoundTrip()
    {
        // Arrange
        var originalMessage = new DeviceSessionMessage
        {
            Type = "test_progress",
            SessionId = "SESS123",
            TestId = "touch",
            TestName = "Touchscreen Test",
            Status = TestStatus.Running,
            Progress = 50.0,
            Message = "Test in progress"
        };

        // Act
        var json = JsonSerializer.Serialize(originalMessage);
        var deserialized = JsonSerializer.Deserialize<DeviceSessionMessage>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(originalMessage.Type, deserialized!.Type);
        Assert.Equal(originalMessage.SessionId, deserialized.SessionId);
        Assert.Equal(originalMessage.TestId, deserialized.TestId);
        Assert.Equal(originalMessage.TestName, deserialized.TestName);
        Assert.Equal(originalMessage.Status, deserialized.Status);
        Assert.Equal(originalMessage.Progress, deserialized.Progress);
        Assert.Equal(originalMessage.Message, deserialized.Message);
    }

    [Fact]
    public async Task InteractiveTestSuiteResult_SerializationRoundTrip()
    {
        // Arrange
        var suite = new InteractiveTestSuiteResult
        {
            SessionId = "SESSION_123",
            DeviceUdid = "DEVICE_ABC",
            UserAgent = "TestAgent/1.0",
            Platform = "iOS",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow,
            Tests = new List<InteractiveTestResult>
            {
                new InteractiveTestResult
                {
                    Id = "touch",
                    Name = "Touch Test",
                    Status = TestStatus.Passed,
                    Notes = "All cells touched",
                    DurationMs = 1234,
                    Details = new Dictionary<string, object>
                    {
                        ["coverage"] = 100.0,
                        ["touchedCells"] = 24
                    }
                },
                new InteractiveTestResult
                {
                    Id = "display",
                    Name = "Display Test",
                    Status = TestStatus.Failed,
                    Notes = "Dead pixels detected",
                    DurationMs = 5678,
                    Details = new Dictionary<string, object>
                    {
                        ["deadPixels"] = 3
                    }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(suite);
        var deserialized = JsonSerializer.Deserialize<InteractiveTestSuiteResult>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(suite.SessionId, deserialized!.SessionId);
        Assert.Equal(suite.DeviceUdid, deserialized.DeviceUdid);
        Assert.Equal(suite.UserAgent, deserialized.UserAgent);
        Assert.Equal(suite.Platform, deserialized.Platform);
        Assert.Equal(2, deserialized.Tests.Count);
        Assert.Equal(TestStatus.Passed, deserialized.Tests[0].Status);
        Assert.Equal(TestStatus.Failed, deserialized.Tests[1].Status);
        Assert.True(suite.AllPassed == false); // One test failed
    }

    [Fact]
    public async Task PostSubmitStepEndpoint_ParsesAndDispatchesStepPayload()
    {
        // Arrange
        var sessionId = "TEST_STEP_SYNC_123";
        DeviceSessionEventArgs? receivedArgs = null;
        _server!.MessageReceived += (s, e) => receivedArgs = e;

        var message = new DeviceSessionMessage
        {
            Type = "test_step_complete",
            SessionId = sessionId,
            TestId = "camera",
            TestName = "Camera Test",
            Status = TestStatus.Passed,
            Message = "Front and rear camera verified"
        };

        var json = JsonSerializer.Serialize(message);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        using var client = new HttpClient();
        var response = await client.PostAsync($"http://localhost:{_server.BoundPort}/api/pwa/submit-step", content);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.NotNull(receivedArgs);
        Assert.Equal(sessionId, receivedArgs!.SessionId);
        Assert.Equal("camera", receivedArgs.Message?.TestId);
        Assert.Equal(TestStatus.Passed, receivedArgs.Message?.Status);
        Assert.Equal("Front and rear camera verified", receivedArgs.Message?.Message);
    }

    [Fact]
    public async Task PostSubmitEndpoint_ParsesAndDispatchesSuiteCompletePayload()
    {
        // Arrange
        var sessionId = "TEST_SUITE_SYNC_456";
        DeviceSessionEventArgs? receivedArgs = null;
        _server!.SuiteCompleted += (s, e) => receivedArgs = e;

        var suiteResult = new InteractiveTestSuiteResult
        {
            SessionId = sessionId,
            DeviceUdid = sessionId,
            Tests = new System.Collections.Generic.List<InteractiveTestResult>
            {
                new() { Id = "touch", Name = "Touch", Status = TestStatus.Passed },
                new() { Id = "display", Name = "Display", Status = TestStatus.Passed }
            }
        };

        var message = new DeviceSessionMessage
        {
            Type = "suite_complete",
            SessionId = sessionId,
            Payload = suiteResult
        };

        var json = JsonSerializer.Serialize(message);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        using var client = new HttpClient();
        var response = await client.PostAsync($"http://localhost:{_server.BoundPort}/api/pwa/submit", content);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.NotNull(receivedArgs);
        Assert.Equal(sessionId, receivedArgs!.SessionId);
        Assert.NotNull(receivedArgs.Message?.Payload);
        Assert.Equal(2, receivedArgs.Message?.Payload?.Tests.Count);
    }

    [Fact]
    public async Task MalformedJSON_DoesNotCrashServer()
    {
        // A step the server cannot read must not escape the middleware as a 500.
        // The phone carries on regardless, so every later step still has to land.
        using var client = new HttpClient();
        var content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"http://127.0.0.1:{_server!.BoundPort}/api/pwa/submit-step", content);
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("\"ok\":false", body);
        Assert.True(_server.IsRunning);

        Assert.Contains("active", await GetStatusAsync("AFTER_BAD_JSON"));
    }

    [Fact]
    public void QrCodeService_GeneratesValidSessionUrl()
    {
        // Arrange
        var localIp = "192.168.1.100";
        var port = 5055;
        var udid = "DEVICE_UDID_123";

        // Act
        var url = QrCodeService.GenerateSessionUrl(localIp, port, udid);

        // Assert
        Assert.Equal($"http://192.168.1.100:5055/?sessionId=DEVICE_UDID_123&lang={LocalizationManager.CurrentLanguage}", url);
    }

    [Fact]
    public void QrCodeService_UriEncodesSpecialCharacters()
    {
        // Arrange
        var localIp = "10.0.0.1";
        var port = 5055;
        var udid = "DEVICE UDID/with spaces & special chars";

        // Act
        var url = QrCodeService.GenerateSessionUrl(localIp, port, udid);

        // Assert
        Assert.Contains("http://10.0.0.1:5055/?sessionId=DEVICE%20UDID%2Fwith%20spaces%20%26%20special%20chars", url);
    }

    [Fact]
    public async Task TestRunnerServer_FiresTelemetryReceivedOnClientTelemetry()
    {
        // Arrange
        var sessionId = "TELEMETRY_TEST";
        ClientTelemetry? receivedTelemetry = null;

        _server!.TelemetryReceived += (s, e) =>
        {
            if (e.SessionId == sessionId) receivedTelemetry = e.Telemetry;
        };

        var telemetryMessage = new LogEventMessage
        {
            Type = "client_telemetry",
            SessionId = sessionId,
            ClientTelemetry = new ClientTelemetry
            {
                Browser = "Safari",
                BrowserVersion = "17.0",
                Os = "iOS",
                OsVersion = "17.2",
                ScreenWidth = 390,
                ScreenHeight = 844,
                PixelRatio = 3.0,
                TouchSupport = true,
                VibrationSupport = false
            }
        };

        using var client = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(telemetryMessage), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync(
            $"http://127.0.0.1:{_server.BoundPort}/api/pwa/telemetry", content);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.NotNull(receivedTelemetry);
        Assert.Equal("Safari", receivedTelemetry!.Browser);
        Assert.Equal("iOS", receivedTelemetry.Os);
        Assert.Equal(390, receivedTelemetry.ScreenWidth);
        Assert.True(receivedTelemetry.TouchSupport);
    }

    [Fact]
    public async Task TestRunnerServer_FiresLogEventReceivedOnLogMessage()
    {
        // Arrange
        var sessionId = "LOG_TEST";
        LogEvent? receivedLog = null;

        _server!.LogEventReceived += (s, e) =>
        {
            if (e.SessionId == sessionId) receivedLog = e.LogEvent;
        };

        var logMessage = new LogEventMessage
        {
            Type = "log_event",
            SessionId = sessionId,
            LogEvent = new LogEvent
            {
                Level = LogLevel.Warning,
                Source = LogSource.PwaClient,
                Message = "User touch delayed",
                SessionId = sessionId
            }
        };

        using var client = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(logMessage), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync(
            $"http://127.0.0.1:{_server.BoundPort}/api/pwa/log", content);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.NotNull(receivedLog);
        Assert.Equal(LogLevel.Warning, receivedLog!.Level);
        Assert.Equal("User touch delayed", receivedLog.Message);
    }

    [Fact]
    public void SystemEventLogger_PubSubBroadcastsCorrectly()
    {
        LogEvent? captured = null;
        EventHandler<LogEvent> handler = (s, e) => 
        {
            if (e.SessionId == "DEV_999") captured = e;
        };

        SystemEventLogger.LogEventEmitted += handler;
        try
        {
            SystemEventLogger.Warning(LogSource.UsbDetector, "Device handshake timeout", "DEV_999");

            Assert.NotNull(captured);
            Assert.Equal(LogLevel.Warning, captured!.Level);
            Assert.Equal(LogSource.UsbDetector, captured.Source);
            Assert.Equal("Device handshake timeout", captured.Message);
            Assert.Equal("DEV_999", captured.SessionId);
        }
        finally
        {
            SystemEventLogger.LogEventEmitted -= handler;
        }
    }

    [Fact]
    public void SystemEventLogger_TraceLevelAndVerboseNetworkLogging_Works()
    {
        LogEvent? captured = null;
        EventHandler<LogEvent> handler = (s, e) => { if (e.Level == LogLevel.Trace) captured = e; };

        SystemEventLogger.LogEventEmitted += handler;
        try
        {
            ToolRunner.EnableVerboseNetworkLogging = true;
            TestRunnerServer.EnableVerboseNetworkLogging = true;

            SystemEventLogger.Trace(LogSource.Desktop, "[CLI EXEC] ideviceinfo -k ProductType (exit: 0)");

            Assert.NotNull(captured);
            Assert.Equal(LogLevel.Trace, captured!.Level);
            Assert.Contains("[CLI EXEC]", captured.Message);
        }
        finally
        {
            ToolRunner.EnableVerboseNetworkLogging = false;
            TestRunnerServer.EnableVerboseNetworkLogging = false;
            SystemEventLogger.LogEventEmitted -= handler;
        }
    }

    [Fact]
    public async Task HttpRest_HandshakeAndStatus_Succeeds()
    {
        using var httpClient = new HttpClient();
        int port = _server!.BoundPort;
        string baseUrl = $"http://127.0.0.1:{port}";

        // 1. GET /api/pwa/status
        var statusResp = await httpClient.GetAsync($"{baseUrl}/api/pwa/status?sessionId=REST_TEST_123");
        Assert.True(statusResp.IsSuccessStatusCode);
        string statusJson = await statusResp.Content.ReadAsStringAsync();
        Assert.Contains("active", statusJson);

        // 2. POST /api/pwa/handshake
        bool handshakeFired = false;
        string connectedId = "";
        _server.DeviceConnected += (s, e) =>
        {
            if (e.SessionId == "REST_TEST_123")
            {
                handshakeFired = true;
                connectedId = e.SessionId;
            }
        };

        var handshakeContent = new StringContent("{\"sessionId\":\"REST_TEST_123\",\"device\":\"iOS Safari\"}", Encoding.UTF8, "application/json");
        var handshakeResp = await httpClient.PostAsync($"{baseUrl}/api/pwa/handshake", handshakeContent);
        Assert.True(handshakeResp.IsSuccessStatusCode);
        string handshakeJson = await handshakeResp.Content.ReadAsStringAsync();
        Assert.Contains("ok", handshakeJson);
        Assert.True(handshakeFired);
        Assert.Equal("REST_TEST_123", connectedId);
    }

    [Fact]
    public async Task HttpRest_SuiteSubmit_FiresSuiteCompleted()
    {
        using var httpClient = new HttpClient();
        int port = _server!.BoundPort;
        string baseUrl = $"http://127.0.0.1:{port}";

        bool suiteCompletedFired = false;
        InteractiveTestSuiteResult? receivedPayload = null;

        _server.SuiteCompleted += (s, e) =>
        {
            if (e.SessionId == "REST_SUITE_123")
            {
                suiteCompletedFired = true;
                receivedPayload = e.Message?.Payload;
            }
        };

        var suitePayload = new DeviceSessionMessage
        {
            Type = "suite_complete",
            SessionId = "REST_SUITE_123",
            Payload = new InteractiveTestSuiteResult
            {
                SessionId = "REST_SUITE_123",
                Platform = "iOS",
                CompletedAt = DateTime.UtcNow,
                Tests = new List<InteractiveTestResult>
                {
                    new InteractiveTestResult { Id = "touch", Name = "Touch Test", Status = TestStatus.Passed, DurationMs = 1200 },
                    new InteractiveTestResult { Id = "screen", Name = "Screen Test", Status = TestStatus.Passed, DurationMs = 2500 }
                }
            }
        };

        var json = JsonSerializer.Serialize(suitePayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await httpClient.PostAsync($"{baseUrl}/api/pwa/submit", content);
        
        Assert.True(resp.IsSuccessStatusCode);
        Assert.True(suiteCompletedFired);
        Assert.NotNull(receivedPayload);
        Assert.Equal(2, receivedPayload!.Tests.Count);
        Assert.True(receivedPayload.AllPassed);
    }

    [Fact]
    public async Task PwaRestApi_Localhost_ProofOfWork_FullWorkflow()
    {
        // 1. Arrange: TestRunnerServer running on active port
        int port = _server!.BoundPort;
        string baseUrl = $"http://127.0.0.1:{port}";
        using var client = new HttpClient();

        bool handshakeFired = false;
        bool telemetryFired = false;
        bool stepFired = false;
        bool submitFired = false;

        string sessionId = "POW_TEST_SESSION_777";

        _server.DeviceConnected += (s, e) =>
        {
            if (e.SessionId == sessionId) handshakeFired = true;
        };

        _server.TelemetryReceived += (s, e) =>
        {
            if (e.SessionId == sessionId) telemetryFired = true;
        };

        _server.MessageReceived += (s, e) =>
        {
            if (e.SessionId == sessionId && e.Message?.Type == "touch_canvas_result") stepFired = true;
        };

        _server.SuiteCompleted += (s, e) =>
        {
            if (e.SessionId == sessionId) submitFired = true;
        };

        // 2. Step 1: Handshake (POST /api/pwa/handshake)
        var handshakeJson = "{\"sessionId\":\"" + sessionId + "\",\"device\":\"Mobile Safari on iOS 17.5\"}";
        var hsResp = await client.PostAsync($"{baseUrl}/api/pwa/handshake", new StringContent(handshakeJson, Encoding.UTF8, "application/json"));
        Assert.True(hsResp.IsSuccessStatusCode, $"Handshake failed with status {hsResp.StatusCode}");
        string hsBody = await hsResp.Content.ReadAsStringAsync();
        Assert.Contains("ok", hsBody);
        Assert.True(handshakeFired, "Server DeviceConnected event was not fired by HTTP handshake!");

        // 3. Step 2: Telemetry (POST /api/pwa/telemetry)
        var telPayload = new LogEventMessage
        {
            Type = "client_telemetry",
            SessionId = sessionId,
            ClientTelemetry = new ClientTelemetry
            {
                Browser = "Safari",
                BrowserVersion = "17.5",
                Os = "iOS",
                OsVersion = "17.5",
                ScreenWidth = 390,
                ScreenHeight = 844,
                TouchSupport = true
            }
        };
        var telResp = await client.PostAsync($"{baseUrl}/api/pwa/telemetry", new StringContent(JsonSerializer.Serialize(telPayload), Encoding.UTF8, "application/json"));
        Assert.True(telResp.IsSuccessStatusCode, "Telemetry submission failed!");
        Assert.True(telemetryFired, "Server TelemetryReceived event was not fired!");

        // 4. Step 3: Submit Touch Canvas Step (POST /api/pwa/submit-step)
        var stepPayload = new DeviceSessionMessage
        {
            Type = "touch_canvas_result",
            SessionId = sessionId,
            Message = "Touch grid 100% completed without deadzones"
        };
        var stepResp = await client.PostAsync($"{baseUrl}/api/pwa/submit-step", new StringContent(JsonSerializer.Serialize(stepPayload), Encoding.UTF8, "application/json"));
        Assert.True(stepResp.IsSuccessStatusCode, "Step submission failed!");
        Assert.True(stepFired, "Server MessageReceived event was not fired for step submission!");

        // 5. Step 4: Final Suite Submit (POST /api/pwa/submit)
        var suitePayload = new DeviceSessionMessage
        {
            Type = "suite_complete",
            SessionId = sessionId,
            Payload = new InteractiveTestSuiteResult
            {
                SessionId = sessionId,
                Platform = "iOS",
                CompletedAt = DateTime.UtcNow,
                Tests = new List<InteractiveTestResult>
                {
                    new InteractiveTestResult { Id = "touch_canvas", Name = "Touch Canvas Test", Status = TestStatus.Passed, DurationMs = 1500 },
                    new InteractiveTestResult { Id = "html5_camera", Name = "Camera Capture Test", Status = TestStatus.Passed, DurationMs = 2100 }
                }
            }
        };
        var submitResp = await client.PostAsync($"{baseUrl}/api/pwa/submit", new StringContent(JsonSerializer.Serialize(suitePayload), Encoding.UTF8, "application/json"));
        Assert.True(submitResp.IsSuccessStatusCode, "Final suite submission failed!");
        Assert.True(submitFired, "Server SuiteCompleted event was not fired!");
    }

}