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


public class MissingApiRequest
{
    public string? SessionId { get; set; }
    public string? MissingApi { get; set; }
    public string? UserAgent { get; set; }
    public string? OsVersion { get; set; }

    /// <summary>
    /// Why the API could not be used: <c>missing</c> (default) when the browser
    /// does not expose it at all, or <c>denied</c> when the capability is there
    /// but the operator refused the permission prompt. Only <c>missing</c> is a
    /// hardware defect worth capping the grade for; a denial is a choice and
    /// must not cost the device a grade.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>True when this report should count against the grade.</summary>
    public bool IsCapabilityGap =>
        !string.Equals(Reason, "denied", StringComparison.OrdinalIgnoreCase);
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

public class MissingApiEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required string MissingApi { get; init; }
}

/// <summary>
/// Raised when the phone reports a finished step, so the desktop can show where
/// the phone is without waiting for the whole suite.
///
/// Only fires when the step was actually taken. A result replayed out of the
/// phone's offline queue changes nothing, and raising the event for it would make
/// the desktop redraw the same numbers on every reconnect.
/// </summary>
public class PwaProgressEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required PwaProgressSnapshot Snapshot { get; init; }
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
    public static bool EnableVerboseNetworkLogging { get; set; } = false;

    /// <summary>
    /// How far the phone got, kept on the desktop so a reload can be picked up
    /// where it stopped and so the operator can watch progress from the PC.
    /// </summary>
    public PwaProgressStore ProgressStore { get; } = new();

    /// <summary>
    /// The device the desktop last paired with, for a page that arrived without
    /// a sessionId. Set whenever a QR code is issued.
    /// </summary>
    public string? ActiveSessionId { get; private set; }

    /// <summary>Records which device new QR codes are for.</summary>
    public void SetActiveSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        ActiveSessionId = SanitizeToken(sessionId, 128);
    }

    /// <summary>Reads a sessionId out of a request body without failing on junk.</summary>
    private static string ReadSessionId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            return doc.RootElement.TryGetProperty("sessionId", out var prop) ? prop.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public event EventHandler<DeviceSessionEventArgs>? DeviceConnected;
    public event EventHandler<DeviceSessionEventArgs>? MessageReceived;
    public event EventHandler<DeviceSessionEventArgs>? SuiteCompleted;
    public event EventHandler<LogMessageEventArgs>? LogEventReceived;
    public event EventHandler<TelemetryEventArgs>? TelemetryReceived;
    public event EventHandler<MissingApiEventArgs>? MissingApiReported;
    public event EventHandler<PwaProgressEventArgs>? ProgressChanged;

    public TestRunnerServer(int preferredPort = 5056, string? contentRootPath = null)
    {
        _preferredPort = preferredPort;
        // PhysicalFileProvider rejects relative paths, so normalise here instead of
        // letting the host fail at startup with "The path must be absolute".
        _contentRootPath = Path.GetFullPath(contentRootPath ?? ResolveWwwRootPath());
    }

    /// <summary>
    /// Strips control characters and bounds the length of a value that came off
    /// the wire before it is logged or stored as a component name. Anything a
    /// client sends is untrusted, and a newline in a log line can forge entries.
    /// </summary>
    /// <summary>
    /// Folds one phone message into the progress store. Both message shapes are
    /// accepted because older phones send the step fields on the message and the
    /// suite shape carries them inside the payload.
    /// </summary>
    private void RecordStepProgress(DeviceSessionMessage msg)
    {
        string sessionId = msg.SessionId ?? "UNKNOWN";
        int total = msg.Payload?.Tests.Count ?? 0;

        switch (msg.Type)
        {
            case "test_start":
                ProgressStore.RecordStart(sessionId, msg.TestId, msg.TestName, total);
                break;

            case "test_complete":
                if (string.IsNullOrWhiteSpace(msg.TestId)) break;

                var step = new PwaStepRecord
                {
                    TestId = msg.TestId!,
                    TestName = msg.TestName ?? "",
                    Status = msg.Status?.ToString()?.ToLowerInvariant() ?? "",
                    // Without a stamp the server has to invent one, and an invented
                    // stamp is always newer, so a replayed result would win.
                    ReportedAt = msg.ClientTimestamp ?? DateTimeOffset.UtcNow,
                };

                if (ProgressStore.RecordStep(sessionId, step, total))
                {
                    ProgressChanged?.Invoke(this, new PwaProgressEventArgs
                    {
                        SessionId = sessionId,
                        Snapshot = ProgressStore.Get(sessionId),
                    });
                }
                break;
        }
    }

    private void RecordFinishProgress(DeviceSessionMessage msg, InteractiveTestSuiteResult suite)
    {
        string sessionId = suite.SessionId ?? msg.SessionId ?? "UNKNOWN";

        foreach (var result in suite.Tests)
        {
            ProgressStore.RecordStep(sessionId, new PwaStepRecord
            {
                TestId = result.Id,
                TestName = result.Name,
                Status = result.Status.ToString().ToLowerInvariant(),
                ReportedAt = msg.ClientTimestamp ?? suite.CompletedAt ?? DateTimeOffset.UtcNow,
            }, suite.Tests.Count);
        }

        ProgressStore.RecordFinish(sessionId, msg.ClientTimestamp ?? suite.CompletedAt ?? DateTimeOffset.UtcNow);

        ProgressChanged?.Invoke(this, new PwaProgressEventArgs
        {
            SessionId = sessionId,
            Snapshot = ProgressStore.Get(sessionId),
        });
    }

    /// <summary>
    /// Strips control characters and bounds the length of a value that came off
    /// the wire before it is logged or stored as a component name. Anything a
    /// client sends is untrusted, and a newline in a log line can forge entries.
    /// </summary>
    private static string SanitizeToken(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var cleaned = new System.Text.StringBuilder(Math.Min(value.Length, maxLength));
        foreach (char c in value)
        {
            if (char.IsControl(c) && c != '\t') continue;
            if (cleaned.Length >= maxLength) break;
            cleaned.Append(c);
        }
        return cleaned.ToString().Trim();
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
                                RequestPath = "",
                                OnPrepareResponse = ctx =>
                                {
                                    // Aggressive no-cache for JS and CSS to prevent stale modules
                                    if (ctx.File.Name.EndsWith(".js") || ctx.File.Name.EndsWith(".css"))
                                    {
                                        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                                        ctx.Context.Response.Headers["Pragma"] = "no-cache";
                                        ctx.Context.Response.Headers["Expires"] = "0";
                                    }
                                }
                            });

                            app.UseWebSockets(new WebSocketOptions
                            {
                                KeepAliveInterval = TimeSpan.FromSeconds(5)
                            });
                            app.Use(async (context, next) =>
                            {
                                string path = context.Request.Path.Value ?? "";
                                
                                // Verbose network tracing
                                if (EnableVerboseNetworkLogging)
                                {
                                    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                                    SystemEventLogger.Trace(LogSource.WebSocket, $"[HTTP {context.Request.Method}] {path} from {clientIp}");
                                }

                                // Always reply 200 OK with CORS headers to OPTIONS preflight
                                if (context.Request.Method == "OPTIONS")
                                {
                                    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                                    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                                    context.Response.Headers["Access-Control-Allow-Headers"] = "*";
                                    context.Response.StatusCode = 200;
                                    await context.Response.CompleteAsync();
                                    return;
                                }

                                // 1. HTTP REST API endpoints (for iOS Safari PWA)
                                if (path.StartsWith("/api/pwa/", StringComparison.OrdinalIgnoreCase))
                                {
                                    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                                    context.Response.ContentType = "application/json";

                                    if (context.Request.Method == "GET" && path.Equals("/api/pwa/status", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string sid = context.Request.Query["sessionId"].ToString();
                                        var resp = new { status = "active", sessionId = sid, timestamp = DateTime.UtcNow };
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(resp));
                                        return;
                                    }

                                    // The phone asks where it got to before it starts or resumes.
                                    // An unknown session is an empty snapshot rather than a 404,
                                    // so the phone can treat "first run" and "nothing stored"
                                    // as the same case and start at the top.
                                    if (context.Request.Method == "GET" && path.Equals("/api/pwa/progress", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string sid = SanitizeToken(context.Request.Query["sessionId"].ToString(), 128);
                                        var snapshot = ProgressStore.Get(sid);
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(snapshot));
                                        return;
                                    }

                                    // A launch from the home screen lands on "/" with no
                                    // sessionId on it. The desktop knows which device is on
                                    // the bench, so the phone asks instead of guessing and
                                    // reporting its results against the wrong device.
                                    if (context.Request.Method == "GET" && path.Equals("/api/pwa/active-session", StringComparison.OrdinalIgnoreCase))
                                    {
                                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                                        {
                                            sessionId = ActiveSessionId ?? "UNKNOWN"
                                        }));
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/progress/reset", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");

                                        string sid = SanitizeToken(ReadSessionId(body), 128);
                                        ProgressStore.Reset(sid);
                                        await context.Response.WriteAsync("{\"ok\":true}");
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/handshake", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");
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
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");
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
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");

                                        // A body that will not parse must not escape the
                                        // middleware as a 500. The phone does not read the
                                        // response code, so an exception here would stop
                                        // every later step from being recorded as well.
                                        DeviceSessionMessage? msg = null;
                                        try
                                        {
                                            msg = JsonSerializer.Deserialize<DeviceSessionMessage>(body,
                                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                        }
                                        catch (JsonException)
                                        {
                                            await context.Response.WriteAsync("{\"ok\":false}");
                                            return;
                                        }

                                        if (msg != null)
                                        {
                                            RecordStepProgress(msg);

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
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");
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

                                            RecordFinishProgress(msg, suiteResult);

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

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/log-warning", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");

                                        // A malformed body must not escape the middleware as a 500:
                                        // the PWA does not check response.ok, so a 400 is just
                                        // dropped on the client, but an unhandled exception would
                                        // take the request pipeline down with it.
                                        MissingApiRequest? req = null;
                                        try
                                        {
                                            req = JsonSerializer.Deserialize<MissingApiRequest>(body,
                                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                        }
                                        catch (JsonException)
                                        {
                                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                            await context.Response.WriteAsync("{\"ok\":false}");
                                            return;
                                        }

                                        if (req != null && !string.IsNullOrWhiteSpace(req.MissingApi))
                                        {
                                            // The value is interpolated into a log line and into a
                                            // component name, so strip control characters (embedded
                                            // newlines could forge log entries) and bound the length.
                                            string missingApi = SanitizeToken(req.MissingApi, 120);
                                            string sessionId = SanitizeToken(req.SessionId ?? "", 128);

                                            if (missingApi.Length == 0)
                                            {
                                                await context.Response.WriteAsync("{\"ok\":true}");
                                                return;
                                            }

                                            string contextDetail = SanitizeToken(req.OsVersion ?? req.UserAgent ?? "", 200);
                                            bool capabilityGap = req.IsCapabilityGap;
                                            SystemEventLogger.Warning(LogSource.PwaClient,
                                                capabilityGap
                                                    ? $"[PWA] Missing API on client: {missingApi} (OS/UA: {contextDetail})"
                                                    : $"[PWA] Permission denied for {missingApi} (OS/UA: {contextDetail})",
                                                sessionId);

                                            if (!capabilityGap)
                                            {
                                                // The API exists; the operator turned it down. That is
                                                // worth a log line but must not become a failed check,
                                                // because a failed check caps the device at grade B.
                                                await context.Response.WriteAsync("{\"ok\":true}");
                                                return;
                                            }

                                            // Register the missing API as a failed component check for the session
                                            if (!string.IsNullOrWhiteSpace(sessionId) &&
                                                DeviceSessionManager.TryGetSession(sessionId, out var session) &&
                                                session.Data != null)
                                            {
                                                string checkName = $"{GradePolicy.MissingApiPrefix} {missingApi}";

                                                // The scan runs on every page load, so an unchanged
                                                // repeat must not stack duplicate failed checks.
                                                if (!session.Data.ComponentChecks.Any(c => c.Name == checkName))
                                                {
                                                    session.Data.ComponentChecks.Add(new PhoneGrade.Core.ComponentStatus
                                                    {
                                                        Name = checkName,
                                                        Status = PhoneGrade.Core.ComponentStatusType.Failed,
                                                        Description = $"Device is missing {missingApi} capability."
                                                    });
                                                }
                                            }

                                            // Let the view model apply the grade penalty for the current device.
                                            MissingApiReported?.Invoke(this, new MissingApiEventArgs
                                            {
                                                SessionId = sessionId.Length == 0 ? "UNKNOWN" : sessionId,
                                                MissingApi = missingApi
                                            });
                                        }
                                        await context.Response.WriteAsync("{\"ok\":true}");
                                        return;
                                    }

                                    if (context.Request.Method == "POST" && path.Equals("/api/pwa/log", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var reader = new StreamReader(context.Request.Body);
                                        string body = await reader.ReadToEndAsync();
                                        if (EnableVerboseNetworkLogging && !string.IsNullOrWhiteSpace(body))
                                            SystemEventLogger.Trace(LogSource.PwaClient, $"[PWA PAYLOAD] {path}: {body}");
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
