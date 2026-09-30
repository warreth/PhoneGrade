using System.Net.Http;
using System.Text.Json;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// Validates license keys against the Lemon Squeezy licensing API.
///
/// The endpoint is shared by every Lemon Squeezy seller: one POST to
/// <c>/v1/licenses/validate</c> with the key identifies the store, order and
/// product that key was sold for, and the answer carries that context in
/// <c>meta</c>. What separates a PhoneGrade key from any other key on the same
/// endpoint is <see cref="PhoneGradeProductId"/>, so a key that is valid but was
/// sold for a different product resolves to <see cref="LicenseValidationResult.Invalid"/>.
/// The request sends only the fields the API documents (<c>license_key</c>);
/// machine binding is not something the endpoint does, it happens locally where
/// the encrypted trial state is keyed on MachineName plus UserName.
///
/// Every failure mode - non-2xx status, malformed JSON, timeout, refused
/// connection, wrong product - maps to <see cref="LicenseValidationResult.Invalid"/>.
/// The source is published, so the client cannot be the trust root anyway; it only
/// has to fail closed on the trivial bypasses (no network, edited response, wrong
/// key) without ever surfacing an exception into the scan flow.
/// </summary>
public sealed class LemonSqueezyClient : IDisposable
{
    /// <summary>The validate endpoint: POST, form-encoded body.</summary>
    public const string ValidateEndpoint = "https://api.lemonsqueezy.com/v1/licenses/validate";

    /// <summary>
    /// The Lemon Squeezy product id this build accepts keys for. Only a key sold
    /// for this product unlocks Pro; a valid key from any other product on the
    /// same endpoint is rejected. Zero disables the pinning, which is only useful
    /// while the store is still being set up.
    /// </summary>
    public const long PhoneGradeProductId = 1400200;

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

    /// <summary>Validates <paramref name="licenseKey"/> against the Lemon Squeezy endpoint.</summary>
    public async Task<LicenseValidationResult> ValidateAsync(string licenseKey, CancellationToken cancellationToken = default) =>
        (await ValidateDetailedAsync(licenseKey, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>Validates and returns the full response, including the product the key was sold for.</summary>
    public async Task<LicenseValidationResponse> ValidateDetailedAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);

        try
        {
            // Only fields the validate endpoint documents: instance_name is not
            // one of them (instances come from activate), so it is not sent.
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = licenseKey.Trim()
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
    ///
    /// <paramref name="expectedProductId"/> is checked last, so a key that is
    /// active and healthy but was sold for a different product comes out as
    /// <see cref="LicenseValidationResult.Invalid"/> instead of being trusted for
    /// its status alone.
    /// </summary>
    public static LicenseValidationResponse ParseValidationResponse(string json, long expectedProductId = PhoneGradeProductId)
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
            long productId = ReadProductId(root);

            LicenseValidationResult result = status switch
            {
                "expired" => LicenseValidationResult.Expired,
                "deactivated" or "disabled" => LicenseValidationResult.Deactivated,
                _ when valid => LicenseValidationResult.Valid,
                _ => LicenseValidationResult.Invalid
            };

            if (expectedProductId != 0 && productId != expectedProductId)
                result = LicenseValidationResult.Invalid;

            return new LicenseValidationResponse(valid, error, status, result, productId);
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

    /// <summary>Reads meta.product_id: the product the key was sold for. Zero when the response does not say.</summary>
    private static long ReadProductId(JsonElement root)
    {
        if (root.TryGetProperty("meta", out JsonElement meta) &&
            meta.ValueKind == JsonValueKind.Object &&
            meta.TryGetProperty("product_id", out JsonElement productId) &&
            productId.ValueKind == JsonValueKind.Number &&
            productId.TryGetInt64(out long value))
        {
            return value;
        }
        return 0;
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
