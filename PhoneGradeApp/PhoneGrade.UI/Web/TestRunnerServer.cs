using System;
using System.IO;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PhoneGrade.Core;

namespace PhoneGrade.UI.Web;

/// <summary>
/// Event arguments for test session messages and completion.
/// </summary>
public class DeviceSessionEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public DeviceSessionMessage? Message { get; init; }
}

public class TelemetryEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required ClientTelemetry Telemetry { get; init; }
}

public class LogMessageEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required LogEvent LogEvent { get; init; }
}

/// <summary>
/// Embedded Kestrel minimal web server serving the PWA test suite and WebSocket hub.
/// </summary>
public class TestRunnerServer : IAsyncDisposable
{
    private IHost? _host;
    private readonly int _preferredPort;
    private readonly string _contentRootPath;
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public int BoundPort { get; private set; }
    public bool IsRunning => _host != null;

    public event EventHandler<DeviceSessionEventArgs>? DeviceConnected;
    public event EventHandler<DeviceSessionEventArgs>? MessageReceived;
    public event EventHandler<DeviceSessionEventArgs>? SuiteCompleted;
    public event EventHandler<LogMessageEventArgs>? LogEventReceived;
    public event EventHandler<TelemetryEventArgs>? TelemetryReceived;

    public TestRunnerServer(int preferredPort = 5055, string? contentRootPath = null)
    {
        _preferredPort = preferredPort;
        _contentRootPath = contentRootPath ?? ResolveWwwRootPath();
    }

    public static string ResolveWwwRootPath()
    {
        var appBase = AppContext.BaseDirectory;
        var candidate1 = Path.Combine(appBase, "wwwroot");
        if (Directory.Exists(candidate1)) return candidate1;

        var candidate2 = Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "wwwroot"));
        if (Directory.Exists(candidate2)) return candidate2;

        return candidate1;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host != null) return;

        if (!Directory.Exists(_contentRootPath))
        {
            Directory.CreateDirectory(_contentRootPath);
        }

        int port = _preferredPort;
        Exception? lastEx = null;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var builder = Host.CreateDefaultBuilder()
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.ListenAnyIP(port);
                        });
                        webBuilder.Configure(app =>
                        {
                            app.UseDefaultFiles(new DefaultFilesOptions
                            {
                                FileProvider = new PhysicalFileProvider(_contentRootPath)
                            });

                            app.UseStaticFiles(new StaticFileOptions
                            {
                                FileProvider = new PhysicalFileProvider(_contentRootPath),
                                RequestPath = ""
                            });

                            app.UseWebSockets(new WebSocketOptions
                            {
                                KeepAliveInterval = TimeSpan.FromSeconds(5)
                            });

                            app.Use(async (context, next) =>
                            {
                                if (context.Request.Path == "/ws/device-session")
                                {
                                    if (context.WebSockets.IsWebSocketRequest)
                                    {
                                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                        string sessionId = context.Request.Query["sessionId"].ToString();
                                        if (string.IsNullOrWhiteSpace(sessionId))
                                        {
                                            sessionId = "UNKNOWN";
                                        }

                                        await HandleSessionWebSocketAsync(sessionId, webSocket);
                                        return;
                                    }
                                    else
                                    {
                                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                        await context.Response.WriteAsync("WebSocket connection required");
                                        return;
                                    }
                                }

                                if (context.Request.Path == "/health")
                                {
                                    context.Response.ContentType = "text/plain";
                                    await context.Response.WriteAsync("OK");
                                    return;
                                }

                                await next();
                            });
                        });
                    });

                var host = builder.Build();
                await host.StartAsync(cancellationToken);
                _host = host;
                BoundPort = port;
                return;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                port = _preferredPort + attempt + 1;
            }
        }

        throw new InvalidOperationException($"Could not bind TestRunnerServer on ports {_preferredPort}-{port - 1}", lastEx);
    }

    private async Task HandleSessionWebSocketAsync(string sessionId, WebSocket webSocket)
    {
        _sockets[sessionId] = webSocket;
        
        DeviceConnected?.Invoke(this, new DeviceSessionEventArgs
        {
            SessionId = sessionId,
            Message = new DeviceSessionMessage
            {
                Type = "init",
                SessionId = sessionId,
                Message = "Connected"
            }
        });

        var buffer = new byte[1024 * 64];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                var json = Encoding.UTF8.GetString(ms.ToArray());
                if (string.IsNullOrWhiteSpace(json)) continue;

                DeviceSessionMessage? msg = null;
                try
                {
                    msg = JsonSerializer.Deserialize<DeviceSessionMessage>(json);
                }
                catch
                {
                    // Ignore malformed payloads
                }

                if (msg != null)
                {
                    msg.SessionId ??= sessionId;

                    if (msg.Type == "client_telemetry")
                    {
                        try
                        {
                            var telMsg = JsonSerializer.Deserialize<LogEventMessage>(json);
                            if (telMsg?.ClientTelemetry != null)
                            {
                                TelemetryReceived?.Invoke(this, new TelemetryEventArgs
                                {
                                    SessionId = sessionId,
                                    Telemetry = telMsg.ClientTelemetry
                                });
                            }
                        }
                        catch { }
                        continue;
                    }

                    if (msg.Type == "log_event")
                    {
                        try
                        {
                            var logMsg = JsonSerializer.Deserialize<LogEventMessage>(json);
                            if (logMsg?.LogEvent != null)
                            {
                                LogEventReceived?.Invoke(this, new LogMessageEventArgs
                                {
                                    SessionId = sessionId,
                                    LogEvent = logMsg.LogEvent
                                });
                            }
                        }
                        catch { }
                        continue;
                    }

                    if (msg.Type == "ping")
                    {
                        var pong = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new DeviceSessionMessage
                        {
                            Type = "pong",
                            SessionId = sessionId
                        }));
                        await webSocket.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Text, true, CancellationToken.None);
                    }

                    MessageReceived?.Invoke(this, new DeviceSessionEventArgs
                    {
                        SessionId = sessionId,
                        Message = msg
                    });

                    if (msg.Type == "suite_complete" && msg.Payload != null)
                    {
                        SuiteCompleted?.Invoke(this, new DeviceSessionEventArgs
                        {
                            SessionId = sessionId,
                            Message = msg
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebSocket error for session {sessionId}: {ex.Message}");
        }
        finally
        {
            _sockets.TryRemove(sessionId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
            _host = null;
        }
    }
}
