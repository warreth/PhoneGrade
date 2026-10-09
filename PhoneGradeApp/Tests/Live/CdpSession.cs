using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PhoneGrade.Tests;

/// <summary>
/// A small Chrome DevTools Protocol client over one websocket.
///
/// The live phone harness needs to look at the page and press things the way a
/// person would, and CDP is the only route into the browser on the handset. It
/// is kept deliberately small: send a method, wait for the matching id, and
/// keep the console and exception events so a run can say what the page
/// complained about.
/// </summary>
internal sealed class CdpSession : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _events = [];
    private readonly object _eventsLock = new();
    private int _nextId;
    private Task? _receiveLoop;

    private CdpSession(ClientWebSocket socket) => _socket = socket;

    /// <summary>Console warnings, errors and uncaught exceptions seen so far.</summary>
    public IReadOnlyList<string> Events
    {
        get { lock (_eventsLock) return _events.ToArray(); }
    }

    public static async Task<CdpSession> ConnectAsync(string webSocketUrl, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(new Uri(webSocketUrl), ct);

        var session = new CdpSession(socket);
        session._receiveLoop = Task.Run(session.ReceiveLoopAsync);
        return session;
    }

    /// <summary>Sends one method and returns its result object.</summary>
    public async Task<JsonElement> SendAsync(string method, object? parameters = null, CancellationToken ct = default)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var message = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
        if (parameters is not null) message["params"] = parameters;

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);

        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        return await completion.Task;
    }

    /// <summary>
    /// Evaluates an expression in the page and returns the value, or null when
    /// the page threw, the value is not serializable, or the runner is not up
    /// yet. Errors are what the wait loops expect while the page boots.
    /// </summary>
    public async Task<JsonElement?> TryEvaluateAsync(string expression, bool awaitPromise = true)
    {
        try
        {
            var response = await SendAsync("Runtime.evaluate", new Dictionary<string, object?>
            {
                ["expression"] = expression,
                ["returnByValue"] = true,
                ["awaitPromise"] = awaitPromise,
                ["userGesture"] = true,
            });

            if (response.TryGetProperty("exceptionDetails", out _)) return null;
            if (!response.TryGetProperty("result", out JsonElement remote)) return null;
            if (remote.TryGetProperty("subtype", out JsonElement subtype) && subtype.GetString() == "null") return null;

            // Runtime.evaluate answers with a RemoteObject; the value asked for
            // is the one inside it, and only returnByValue makes it a copy.
            if (!remote.TryGetProperty("value", out JsonElement value)) return null;
            return value.Clone();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Evaluates an expression and returns it as a string, or "" when there is none.</summary>
    public async Task<string> EvaluateStringAsync(string expression, bool awaitPromise = true)
    {
        JsonElement? value = await TryEvaluateAsync(expression, awaitPromise);
        return value is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";
    }

    /// <summary>Evaluates an expression and returns it as a boolean.</summary>
    public async Task<bool> EvaluateBoolAsync(string expression, bool awaitPromise = true)
    {
        JsonElement? value = await TryEvaluateAsync(expression, awaitPromise);
        return value is { ValueKind: JsonValueKind.True };
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();

        try
        {
            while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(message.ToArray()));
                message.SetLength(0);
            }
        }
        catch (OperationCanceledException)
        {
            // The session is being disposed.
        }
        catch (WebSocketException)
        {
            // The browser went away; the pending calls time out on their own.
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int id))
            {
                if (_pending.TryRemove(id, out var completion))
                {
                    if (root.TryGetProperty("error", out JsonElement error))
                        completion.TrySetException(new InvalidOperationException(error.Clone().ToString()));
                    else if (root.TryGetProperty("result", out JsonElement result))
                        completion.TrySetResult(result.Clone());
                    else
                        completion.TrySetResult(default);
                }
                return;
            }

            if (!root.TryGetProperty("method", out JsonElement methodElement)) return;
            string method = methodElement.GetString() ?? "";

            switch (method)
            {
                case "Runtime.consoleAPICalled":
                    RecordConsole(root);
                    break;
                case "Runtime.exceptionThrown":
                    Record($"exception: {root.GetProperty("params").GetProperty("exceptionDetails").GetProperty("text").GetString()}");
                    break;
                case "Log.entryAdded":
                    RecordLogEntry(root);
                    break;
            }
        }
        catch (JsonException)
        {
            // A malformed frame is not worth failing a run over.
        }
    }

    private void RecordConsole(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        string type = parameters.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() ?? "" : "";
        if (type is not ("error" or "warning" or "assert")) return;

        var text = new StringBuilder();
        if (parameters.TryGetProperty("args", out JsonElement args))
        {
            foreach (JsonElement arg in args.EnumerateArray())
            {
                if (arg.TryGetProperty("value", out JsonElement value))
                    text.Append(value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString());
                else if (arg.TryGetProperty("description", out JsonElement description))
                    text.Append(description.GetString());
                text.Append(' ');
            }
        }

        Record($"{type}: {text.ToString().Trim()}");
    }

    private void RecordLogEntry(JsonElement root)
    {
        var entry = root.GetProperty("params").GetProperty("entry");
        string level = entry.TryGetProperty("level", out JsonElement levelElement) ? levelElement.GetString() ?? "" : "";
        if (level is not ("error" or "warning")) return;

        string text = entry.TryGetProperty("text", out JsonElement textElement) ? textElement.GetString() ?? "" : "";
        Record($"{level}: {text}");
    }

    private void Record(string line)
    {
        lock (_eventsLock)
        {
            if (_events.Count >= 400) return;
            _events.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed */ }

        try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
        catch { /* already gone */ }

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* the loop ends with the socket */ }
        }

        _socket.Dispose();
        _cts.Dispose();
    }

    private bool _disposed;
}
