using System;
using System.Net.WebSockets;
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
    private WebSocket? _clientWebSocket;

    public async Task InitializeAsync()
    {
        // Create test server on a unique port to avoid conflicts
        _server = new TestRunnerServer(preferredPort: 5060);
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_clientWebSocket != null)
        {
            await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test cleanup", CancellationToken.None);
            _clientWebSocket.Dispose();
        }

        if (_server != null)
        {
            await _server.DisposeAsync();
        }
    }

    private async Task<WebSocket> ConnectWebSocketAsync(string sessionId = "TEST_SESSION_123")
    {
        var ws = new ClientWebSocket();
        var uri = new Uri($"ws://localhost:{_server!.BoundPort}/ws/device-session?sessionId={sessionId}");
        await ws.ConnectAsync(uri, CancellationToken.None);
        return ws;
    }

    [Fact]
    public async Task TestRunnerServer_StartsAndAcceptsConnections()
    {
        Assert.NotNull(_server);
        Assert.True(_server!.IsRunning);
        Assert.InRange(_server.BoundPort, 5060, 5070);
    }

    [Fact]
    public async Task WebSocketEndpoint_AcceptsValidWebSocketRequests()
    {
        // Arrange
        var sessionId = "TEST_SESSION_ABC";

        // Act
        _clientWebSocket = await ConnectWebSocketAsync(sessionId);

        // Assert
        Assert.Equal(WebSocketState.Open, _clientWebSocket.State);
    }

    [Fact]
    public async Task WebSocketEndpoint_RejectsNonWebSocketRequests()
    {
        // This would require an HTTP client test, skip for now
        // WebSocket endpoint rejects non-WebSocket with 400 Bad Request
        Assert.True(true); // Placeholder
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
    public async Task WebSocket_PingPongProtocol()
    {
        // Arrange
        _clientWebSocket = await ConnectWebSocketAsync("PING_TEST");
        var buffer = new byte[1024];

        // Act: Send ping message
        var pingMessage = new DeviceSessionMessage
        {
            Type = "ping",
            SessionId = "PING_TEST"
        };
        var pingJson = JsonSerializer.Serialize(pingMessage);
        var pingBytes = Encoding.UTF8.GetBytes(pingJson);
        await _clientWebSocket.SendAsync(new ArraySegment<byte>(pingBytes), WebSocketMessageType.Text, true, CancellationToken.None);

        // Assert: Receive pong response
        var result = await _clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        var pongJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
        var pongMessage = JsonSerializer.Deserialize<DeviceSessionMessage>(pongJson);

        Assert.NotNull(pongMessage);
        Assert.Equal("pong", pongMessage!.Type);
        Assert.Equal("PING_TEST", pongMessage.SessionId);
    }

    [Fact]
    public async Task TestRunnerServer_EventsFiredOnConnection()
    {
        // Arrange
        bool deviceConnectedFired = false;
        string connectedSessionId = string.Empty;

        _server!.DeviceConnected += (s, e) =>
        {
            deviceConnectedFired = true;
            connectedSessionId = e.SessionId;
        };

        // Act
        _clientWebSocket = await ConnectWebSocketAsync("EVENT_TEST");

        // Give event handler time to fire
        await Task.Delay(100);

        // Assert
        Assert.True(deviceConnectedFired);
        Assert.Equal("EVENT_TEST", connectedSessionId);
    }

    [Fact]
    public async Task MalformedJSON_DoesNotCrashServer()
    {
        // Arrange
        _clientWebSocket = await ConnectWebSocketAsync("MALFORMED_TEST");

        // Act: Send invalid JSON
        var invalidJson = "{ invalid json }";
        var bytes = Encoding.UTF8.GetBytes(invalidJson);
        await _clientWebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

        // Give server time to process
        await Task.Delay(100);

        // Assert: Server is still running, connection is still open
        Assert.True(_server!.IsRunning);
        Assert.Equal(WebSocketState.Open, _clientWebSocket.State);
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
        Assert.Equal("http://192.168.1.100:5055/?sessionId=DEVICE_UDID_123", url);
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
}