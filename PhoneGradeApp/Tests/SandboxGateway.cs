using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Tests;

/// <summary>
/// The sandbox numbers imei.info publishes for integration testing, served over
/// a real socket instead of a stubbed message handler.
///
/// The three IMEIs and the answers for them are the ones in imei.info's own SDK
/// test fixtures (github.com/imei-info, imei-check-api-python/tests/conftest.py):
/// two clean devices, one blacklisted one, and HTTP 402 for every other IMEI.
/// The service list mirrors what dash.imei.info answers today, down to the
/// service ids, so name matching is exercised against the real names.
///
/// It is a plain HTTP/1.1 server on the loopback interface: the service under
/// test builds its request exactly as it would against the gateway, and the
/// server records what actually arrived, headers and status code included.
/// </summary>
internal sealed class SandboxGateway : IDisposable
{
    /// <summary>Sandbox number for an Apple iPhone 12 Pro Max, status CLEAN.</summary>
    public const string AppleImei = "353541326469521";

    /// <summary>Sandbox number for a Samsung Galaxy S24 Ultra, status CLEAN.</summary>
    public const string SamsungImei = "350545260771498";

    /// <summary>Sandbox number for a Google Pixel 8 Pro, status BLACKLISTED.</summary>
    public const string BlacklistedImei = "355030794352540";

    /// <summary>A well formed IMEI that is not a sandbox number, so it hits the 402 branch.</summary>
    public const string ControlImei = "358742091234567";

    private static readonly Encoding Wire = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<RecordedRequest> _requests = new();
    private readonly Task _loop;

    public SandboxGateway()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Port the sandbox answers on; chosen by the OS, never hard-coded.</summary>
    public int Port { get; }

    /// <summary>Base address the service under test points its HttpClient at.</summary>
    public Uri BaseAddress => new($"http://127.0.0.1:{Port}/");

    /// <summary>Every request that reached the sandbox, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    /// <summary>A client that talks to this sandbox the way it talks to the gateway.</summary>
    public HttpClient CreateClient() => new()
    {
        BaseAddress = BaseAddress,
        Timeout = TimeSpan.FromSeconds(15)
    };

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The listener is already stopped; nothing left to wait for.
        }
        _stop.Dispose();
    }

    // ------------------------------------------------------------------
    // Server
    // ------------------------------------------------------------------

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (_stop.IsCancellationRequested
                                       || ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                await HandleAsync(client).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The caller hung up between requests; the sandbox keeps serving.
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                client.Close();
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        client.NoDelay = true;
        var stream = client.GetStream();

        string head = await ReadHeadAsync(stream).ConfigureAwait(false);
        if (head.Length == 0)
            return;

        string[] lines = head.Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 2)
        {
            await SendAsync(stream, 400, """{"detail":"Malformed request."}""").ConfigureAwait(false);
            return;
        }

        string target = requestLine[1];
        int questionMark = target.IndexOf('?');
        string path = questionMark < 0 ? target : target[..questionMark];
        string query = questionMark < 0 ? "" : target[(questionMark + 1)..];
        var parameters = ParseQuery(query);

        string authorization = "";
        foreach (string line in lines)
        {
            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
            {
                authorization = line["Authorization:".Length..].Trim();
                break;
            }
        }

        (int status, string body) = Route(path, parameters, authorization);

        Record(new RecordedRequest(path, query, authorization, status));
        await SendAsync(stream, status, body).ConfigureAwait(false);
    }

    private (int Status, string Body) Route(string path, Dictionary<string, string> parameters, string authorization)
    {
        if (path.Contains("/service/services/", StringComparison.Ordinal))
            return (200, CatalogJson);

        if (path.Contains("/account/account/", StringComparison.Ordinal))
            return (200, AccountJson);

        bool isCheck = path.Contains("/api-sync/check/", StringComparison.Ordinal)
            || path.Contains("/api/check/", StringComparison.Ordinal);

        if (!isCheck)
            return (404, """{"detail":"Not found."}""");

        // Same refusal the real gateway gives when the key does not come through:
        // HTTP 200 on the api-sync path, HTTP 401 on the queue path.
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parameters.GetValueOrDefault("API_KEY")))
        {
            int code = path.Contains("/api-sync/check/", StringComparison.Ordinal) ? 200 : 401;
            return (code, """{"detail":"Token is invalid."}""");
        }

        string imei = parameters.GetValueOrDefault("imei") ?? "";
        return imei switch
        {
            AppleImei => (200, AppleJson),
            SamsungImei => (200, SamsungJson),
            BlacklistedImei => (200, BlacklistedJson),
            _ => (402, PaymentRequiredJson)
        };
    }

    private void Record(RecordedRequest request)
    {
        lock (_requests)
            _requests.Add(request);
    }

    private static async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        var collected = new MemoryStream();

        while (collected.Length < 16 * 1024)
        {
            int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
                break;

            collected.Write(buffer, 0, read);
            string soFar = Wire.GetString(collected.ToArray());
            if (soFar.Contains("\r\n\r\n", StringComparison.Ordinal))
                return soFar;
        }

        return Wire.GetString(collected.ToArray());
    }

    private static async Task SendAsync(NetworkStream stream, int status, string json)
    {
        byte[] body = Wire.GetBytes(json);
        string head =
            $"HTTP/1.1 {status} {Reason(status)}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        byte[] header = Wire.GetBytes(head);
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        402 => "Payment Required",
        404 => "Not Found",
        _ => "Error"
    };

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string name = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? "" : pair[(equals + 1)..];
            parameters[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value);
        }
        return parameters;
    }

    // ------------------------------------------------------------------
    // Payloads
    // ------------------------------------------------------------------

    /// <summary>The checks this app offers, with the ids dash.imei.info gives them.</summary>
    private const string CatalogJson = """
    {
      "count": 4,
      "next": null,
      "results": [
        { "id": 2,  "name": "APPLE: Carrier & Lock Status & FMI", "slug": "apple-carrier-fmi", "price": 0.36 },
        { "id": 27, "name": "BLACKLIST: Simple Check", "slug": "blacklist-simple", "price": 0.2 },
        { "id": 28, "name": "BLACKLIST: Premium Check", "slug": "blacklist-premium", "price": 0.42 },
        { "id": 76, "name": "GENERIC: Samsung Info Check & Knox Info", "slug": "samsung-knox", "price": 0.6 }
      ]
    }
    """;

    private const string AccountJson = """
    { "username": "sandbox", "email": "sandbox@imei.info", "balance": 0.0, "is_active": true, "pricing_level": "L2" }
    """;

    private const string AppleJson = """
    {
      "imei": "353541326469521",
      "brand": "Apple",
      "model": "iPhone 12 Pro Max",
      "tac": "35354132",
      "blacklist_status": "CLEAN",
      "carrier_lock": false,
      "original_carrier": "T-Mobile Polska",
      "purchase_country": "Poland",
      "specifications": { "cpu": "Apple A14 Bionic", "ram_gb": 6, "storage_gb": 128, "screen_size": "6.7 inches" }
    }
    """;

    private const string SamsungJson = """
    {
      "imei": "350545260771498",
      "brand": "Samsung",
      "model": "Galaxy S24 Ultra",
      "tac": "35054526",
      "blacklist_status": "CLEAN",
      "carrier_lock": false,
      "original_carrier": "Orange Polska",
      "purchase_country": "Poland",
      "specifications": { "cpu": "Snapdragon 8 Gen 3", "ram_gb": 12, "storage_gb": 256, "screen_size": "6.8 inches" }
    }
    """;

    private const string BlacklistedJson = """
    {
      "imei": "355030794352540",
      "brand": "Google",
      "model": "Pixel 8 Pro",
      "tac": "35503079",
      "blacklist_status": "BLACKLISTED",
      "carrier_lock": true,
      "original_carrier": "T-Mobile USA",
      "purchase_country": "United States",
      "specifications": { "cpu": "Google Tensor G3", "ram_gb": 12, "storage_gb": 128, "screen_size": "6.7 inches" }
    }
    """;

    private const string PaymentRequiredJson = """
    {
      "error": "Payment Required",
      "code": "insufficient_credits",
      "message": "Your API balance is $0.00. Please recharge your account in the developer dashboard at dash.imei.info before executing queries."
    }
    """;

    /// <summary>One request as the sandbox saw it.</summary>
    public sealed record RecordedRequest(string Path, string Query, string Authorization, int StatusCode);
}
