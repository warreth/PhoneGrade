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
using Microsoft.Extensions.DependencyInjection;
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
                        webBuilder.ConfigureServices(services =>
                        {
                            services.AddCors(corsOptions =>
                            {
                                corsOptions.AddDefaultPolicy(policy =>
                                {
                                    policy.AllowAnyOrigin()
                                          .AllowAnyMethod()
                                          .AllowAnyHeader();
                                });
                            });
                        });
                        webBuilder.Configure(app =>
                        {
                            app.UseCors();
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
                                string path = context.Request.Path.Value ?? "";

                                // 1. HTTP REST API endpoints (for iOS Safari PWA)
                                if (path.StartsWith("/api/pwa/", StringComparison.OrdinalIgnoreCase))
                                {
                                    context.Response.ContentType = "application/json";

                                    if (context.Request.Method == "GET" && path.Equals("/api/pwa/status", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string sid = context.Request.Query["sessionId"].ToString();
                                        var resp = new { status = "active", sessionId = sid, timestamp = DateTime.UtcNow };
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(resp));
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/handshake", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                                        string sid = doc.RootElement.TryGetProperty("sessionId", out var sProp) ? sProp.GetString() ?? "UNKNOWN" : "UNKNOWN";

                                        DeviceConnected?.Invoke(this, new DeviceSessionEventArgs
                                        {
                                            SessionId = sid,
                                            Message = new DeviceSessionMessage { Type = "init", SessionId = sid, Message = "Connected via HTTP REST" }
                                        });

                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { ok = true, sessionId = sid, mode = "rest" }));
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/telemetry", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        var telMsg = JsonSerializer.Deserialize<LogEventMessage>(body);
                                        if (telMsg?.ClientTelemetry != null)
                                        {
                                            TelemetryReceived?.Invoke(this, new TelemetryEventArgs
                                            {
                                                SessionId = telMsg.SessionId ?? "UNKNOWN",
                                                Telemetry = telMsg.ClientTelemetry
                                            });
                                        }
                                        await context.Response.WriteAsync("{\"ok\":true}");
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/submit-step", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        var msg = JsonSerializer.Deserialize<DeviceSessionMessage>(body);
                                        if (msg != null)
                                        {
                                            MessageReceived?.Invoke(this, new DeviceSessionEventArgs
                                            {
                                                SessionId = msg.SessionId ?? "UNKNOWN",
                                                Message = msg
                                            });
                                        }
                                        await context.Response.WriteAsync("{\"ok\":true}");
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/submit", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                                        
                                        DeviceSessionMessage? msg = null;
                                        try { msg = JsonSerializer.Deserialize<DeviceSessionMessage>(body, opt); } catch { }

                                        InteractiveTestSuiteResult? suiteResult = msg?.Payload;
                                        if (suiteResult == null)
                                        {
                                            try { suiteResult = JsonSerializer.Deserialize<InteractiveTestSuiteResult>(body, opt); } catch { }
                                        }

                                        if (suiteResult != null)
                                        {
                                            msg ??= new DeviceSessionMessage
                                            {
                                                Type = "suite_complete",
                                                SessionId = suiteResult.SessionId,
                                                Payload = suiteResult
                                            };
                                            msg.Payload = suiteResult;

                                            SuiteCompleted?.Invoke(this, new DeviceSessionEventArgs
                                            {
                                                SessionId = suiteResult.SessionId ?? msg.SessionId ?? "UNKNOWN",
                                                Message = msg
                                            });
                                        }
                                        context.Response.ContentType = "application/json";
                                        await context.Response.WriteAsync("{\"ok\":true}");
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/log", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        var logMsg = JsonSerializer.Deserialize<LogEventMessage>(body);
                                        if (logMsg?.LogEvent != null)
                                        {
                                            LogEventReceived?.Invoke(this, new LogMessageEventArgs
                                            {
                                                SessionId = logMsg.SessionId ?? "UNKNOWN",
                                                LogEvent = logMsg.LogEvent
                                            });
                                        }
                                        await context.Response.WriteAsync("{\"ok\":true}");
                                        return;
                                    }
                                }

                                // 2. WebSocket endpoint (for backward compatibility and test suite)
                                if (path == "/ws/device-session")
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
                EnsureWindowsFirewallRule(BoundPort);
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

    /// <summary>Broadcasts a JSON-serializable message to all connected WebSocket sessions.</summary>
    public void BroadcastMessage(object message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        var buffer = new ArraySegment<byte>(bytes);

        foreach (var (sessionId, socket) in _sockets)
        {
            if (socket.State == WebSocketState.Open)
            {
                _ = socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }
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

    /// <summary>Configures Windows Firewall rule for the PWA server port if running on Windows.</summary>
    public static void EnsureWindowsFirewallRule(int port = 5055)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // First remove any existing rule with same name to avoid duplicates
            var delInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "advfirewall firewall delete rule name=\"PhoneGrade_PWA\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var delProc = System.Diagnostics.Process.Start(delInfo))
            {
                delProc?.WaitForExit(2000);
            }

            // Add the firewall rule matching the exact active port
            var addInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"PhoneGrade_PWA\" dir=in action=allow protocol=TCP localport={port}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var addProc = System.Diagnostics.Process.Start(addInfo);
            addProc?.WaitForExit(3000);
            SystemEventLogger.Info(LogSource.Desktop, $"Windows Firewall regel 'PhoneGrade_PWA' ingesteld voor actieve poort {port}.");
        }
        catch (Exception ex)
        {
            SystemEventLogger.Warning(LogSource.Desktop, $"Kon Windows Firewall regel niet automatisch instellen: {ex.Message}");
        }
    }

    private static System.Diagnostics.Process? _iproxyProcess;

    /// <summary>Sets up USB port forwarding via iproxy / usbmuxd in background if device is connected over USB.</summary>
    public static Task SetupUsbPortForwardingAsync(string udid, int localPort = 5055, int devicePort = 5055)
    {
        try
        {
            if (_iproxyProcess != null && !_iproxyProcess.HasExited)
            {
                try { _iproxyProcess.Kill(); } catch { }
            }

            // iproxy binds localPort and forwards to devicePort on the USB device
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "iproxy",
                Arguments = $"{localPort} {devicePort} -u {udid}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _iproxyProcess = System.Diagnostics.Process.Start(startInfo);
            SystemEventLogger.Info(LogSource.Desktop, $"USB reverse tethering / poortkoppeling actief voor {udid}:{devicePort} op lokale poort {localPort}.");
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.Desktop, $"USB poortkoppeling niet beschikbaar: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
