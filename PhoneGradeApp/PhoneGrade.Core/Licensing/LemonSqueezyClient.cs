using System.Net.Http;
using System.Text.Json;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// Validates license keys against the Lemon Squeezy licensing API.
///
/// The endpoint expects a form-encoded POST with <c>license_key</c> and
/// <c>instance_name</c>. The instance name is <see cref="Environment.MachineName"/>,
/// so a key activated here is pinned to this machine in the vendor's records and
/// the same key does not silently serve a second install.
///
/// Every failure mode - non-2xx status, malformed JSON, timeout, refused
/// connection - maps to <see cref="LicenseValidationResult.Invalid"/>. The app is
/// published source, so the client cannot be the trust root anyway; it only has to
/// fail closed on the trivial bypasses (no network, edited response, wrong key)
/// without ever surfacing an exception into the scan flow.
/// </summary>
public sealed class LemonSqueezyClient : IDisposable
{
    /// <summary>The validate endpoint: POST, form-encoded body.</summary>
    public const string ValidateEndpoint = "https://api.lemonsqueezy.com/v1/licenses/validate";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>Production client with the default endpoint and a short timeout.</summary>
    public LemonSqueezyClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, ownsHttpClient: true)
    {
    }

    /// <summary>Test seam: routes the POST through a fake handler instead of the network.</summary>
    public LemonSqueezyClient(HttpMessageHandler handler)
        : this(new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))), ownsHttpClient: true)
    {
    }

    /// <summary>Injects a shared client. The client is not disposed unless <paramref name="ownsHttpClient"/> is set.</summary>
    public LemonSqueezyClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    /// <summary>Validates <paramref name="licenseKey"/> against this machine (<see cref="Environment.MachineName"/>).</summary>
    public async Task<LicenseValidationResult> ValidateAsync(string licenseKey, CancellationToken cancellationToken = default) =>
        (await ValidateDetailedAsync(licenseKey, Environment.MachineName, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>Validates with an explicit instance name (used by tests and by any future node registration).</summary>
    public async Task<LicenseValidationResponse> ValidateDetailedAsync(string licenseKey, string instanceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = licenseKey.Trim(),
                ["instance_name"] = instanceName ?? ""
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, ValidateEndpoint) { Content = content };
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new LicenseValidationResponse(false, $"HTTP {(int)response.StatusCode}", "", LicenseValidationResult.Invalid);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseValidationResponse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller cancelled on purpose; a timeout below still lands in Invalid
        }
        catch (Exception)
        {
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);
        }
    }

    /// <summary>
    /// Maps a validate response body to <see cref="LicenseValidationResponse"/>.
    /// The status string is read before <c>valid</c> because an expired or
    /// deactivated key reports <c>valid: false</c> as well, and the status is
    /// the only field that says which of the two it is.
    /// </summary>
    public static LicenseValidationResponse ParseValidationResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);

            bool valid = root.TryGetProperty("valid", out JsonElement validElement) &&
                         validElement.ValueKind == JsonValueKind.True;
            string error = ReadString(root, "error");
            string status = ReadStatus(root);

            LicenseValidationResult result = status switch
            {
                "expired" => LicenseValidationResult.Expired,
                "deactivated" or "disabled" => LicenseValidationResult.Deactivated,
                _ when valid => LicenseValidationResult.Valid,
                _ => LicenseValidationResult.Invalid
            };

            return new LicenseValidationResponse(valid, error, status, result);
        }
        catch (Exception)
        {
            // Malformed JSON is a failed validation, never an exception into the scan flow.
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);
        }
    }

    private static string ReadStatus(JsonElement root)
    {
        if (root.TryGetProperty("license_key", out JsonElement key) &&
            key.ValueKind == JsonValueKind.Object)
        {
            return ReadString(key, "status").Trim().ToLowerInvariant();
        }
        return "";
    }

    private static string ReadString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? ""
            : "";

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
