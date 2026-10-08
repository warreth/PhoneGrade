using System.Net.Http;
using System.Text.Json;

namespace PhoneGrade.Core.Licensing;

/// <summary>
/// Talks to the Lemon Squeezy licensing API: validates keys, turns a key into a
/// seat on a machine, and hands a seat back.
///
/// The three endpoints are shared by every Lemon Squeezy seller: a POST to
/// <c>/v1/licenses/validate</c> identifies the store, order and product that a
/// key was sold for, and the answer carries that context in <c>meta</c>. What
/// separates a PhoneGrade key from any other key on the same endpoint is
/// <see cref="PhoneGradeProductId"/>, so a key that is valid but was sold for a
/// different product resolves to <see cref="LicenseValidationResult.Invalid"/>.
///
/// Machine binding is what activate exists for. Validate answers whether a key is
/// good; activate claims one of the key's <c>activation_limit</c> seats for an
/// <c>instance_name</c> and hands back an <c>instance</c> that can then be named
/// on every later validate. So the client is deliberately the place where the
/// fingerprint is sent: the app sends a fingerprint, never a hostname, and the
/// vendor decides how many fingerprints a key is allowed.
///
/// The counters in a response are the vendor's, not ours, so nothing here compares
/// activation_usage against a number baked into the binary.
///
/// Every failure mode - non-2xx status, malformed JSON, timeout, refused
/// connection, wrong product - maps to a non-exception result. The app is open
/// source, so the client cannot be the trust root anyway; it only has to fail
/// closed on the trivial bypasses (no network, edited response, wrong key) without
/// ever surfacing an exception into the scan flow.
/// </summary>
public sealed class LemonSqueezyClient : IDisposable
{
    /// <summary>The validate endpoint: POST, form-encoded body.</summary>
    public const string ValidateEndpoint = "https://api.lemonsqueezy.com/v1/licenses/validate";

    /// <summary>The activate endpoint: claims one of a key's seats for an instance.</summary>
    public const string ActivateEndpoint = "https://api.lemonsqueezy.com/v1/licenses/activate";

    /// <summary>The deactivate endpoint: hands a seat back so another machine can take it.</summary>
    public const string DeactivateEndpoint = "https://api.lemonsqueezy.com/v1/licenses/deactivate";

    /// <summary>
    /// The Lemon Squeezy product id this build accepts keys for. Only a key sold
    /// for this product unlocks Pro; a valid key from any other product on the
    /// same endpoint is rejected. Zero disables the pinning, which is only useful
    /// while the store is still being set up.
    /// </summary>
    public const long PhoneGradeProductId = 1422604;

    /// <summary>
    /// The product id the store used before it was recreated. Keys sold then are
    /// still in customers' hands, and a store migration must not turn them into
    /// invalid keys, so a response for either product is a PhoneGrade key.
    /// </summary>
    public const long PhoneGradeLegacyProductId = 1400200;

    /// <summary>True when a response's product id is one this build accepts.</summary>
    public static bool IsPhoneGradeProduct(long productId) =>
        productId == PhoneGradeProductId || productId == PhoneGradeLegacyProductId;

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
        (await ValidateDetailedAsync(licenseKey, null, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>
    /// Validates the bare key, with no instance named. Kept as its own method
    /// because asking about the key alone is a real and different question, and a
    /// call site that means it should say so rather than pass null.
    /// </summary>
    public Task<LicenseValidationResponse> ValidateDetailedAsync(
        string licenseKey,
        CancellationToken cancellationToken = default) =>
        ValidateDetailedAsync(licenseKey, null, cancellationToken);

    /// <summary>
    /// Validates and returns the full response, including the product the key was
    /// sold for and the seat counters.
    ///
    /// <paramref name="instanceId"/> is what turns the question from "is this key
    /// good" into "is this machine one of the seats on this key". Without it the API
    /// answers with <c>"instance": null</c> and a valid key, which says nothing
    /// about the machine the app happens to run on.
    /// </summary>
    public async Task<LicenseValidationResponse> ValidateDetailedAsync(
        string licenseKey,
        string? instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);

        var form = new Dictionary<string, string> { ["license_key"] = licenseKey.Trim() };
        if (!string.IsNullOrWhiteSpace(instanceId))
            form["instance_id"] = instanceId.Trim();

        return await PostAsync(
            ValidateEndpoint, form, body => ParseValidationResponse(body), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Claims one of the key's seats for <paramref name="instanceName"/> and
    /// returns the instance that was created.
    ///
    /// This is the call that makes the plan's machine limit mean something, and it
    /// is also the call that must not happen by accident: a key pasted in twice,
    /// or on a second machine that already reached the limit, would burn a seat
    /// each time. The gate only calls this when there is no stored instance.
    /// </summary>
    public async Task<LicenseActivationResponse> ActivateAsync(
        string licenseKey,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(instanceName))
            return FailedActivation("");

        var form = new Dictionary<string, string>
        {
            ["license_key"] = licenseKey.Trim(),
            ["instance_name"] = instanceName.Trim()
        };

        return await PostAsync(
            ActivateEndpoint, form, body => ParseActivationResponse(body), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the seat <paramref name="instanceId"/> holds, so the next machine
    /// to activate the same key can take it. This is what a shop needs after
    /// replacing a bench, and it is why a seat is not a permanent cost.
    /// </summary>
    public async Task<LicenseDeactivationResponse> DeactivateAsync(
        string licenseKey,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey) || string.IsNullOrWhiteSpace(instanceId))
            return new LicenseDeactivationResponse(false, "", LicenseValidationResult.Invalid);

        var form = new Dictionary<string, string>
        {
            ["license_key"] = licenseKey.Trim(),
            ["instance_id"] = instanceId.Trim()
        };

        return await PostAsync(
            DeactivateEndpoint, form, body => ParseDeactivationResponse(body), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One POST, form-encoded, and one parser. All three endpoints fail the same
    /// way, so the transport handling is written once: a non-2xx status and a
    /// malformed body are both a failed call rather than an exception into the scan
    /// flow, and a cancelled token stays cancelled so a caller that asked to stop
    /// really stops.
    /// </summary>
    private async Task<TResponse> PostAsync<TResponse>(
        string endpoint,
        Dictionary<string, string> form,
        Func<string, TResponse> parse,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // The vendor's own refusal body is kept where there is one: a key
                // that has run out of seats is refused with a 400 and the sentence
                // that says so, and replacing the body with the status line turned
                // a "no seats left" into an unreadable "HTTP 400". A body that is
                // not JSON still falls back to the status line.
                string refusal = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string trimmed = refusal.TrimStart();
                if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                    return parse(refusal);

                return parse("{\"error\":\"HTTP " + (int)response.StatusCode + "\"}");
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller cancelled on purpose; a timeout below still lands in the failure result
        }
        catch (Exception)
        {
            return parse("{}");
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
            int limit = ReadCounter(root, "activation_limit");
            int usage = ReadCounter(root, "activation_usage");
            (string instanceId, string instanceName) = ReadInstance(root);

            LicenseValidationResult result = status switch
            {
                "expired" => LicenseValidationResult.Expired,
                "deactivated" or "disabled" => LicenseValidationResult.Deactivated,
                _ when valid => LicenseValidationResult.Valid,
                _ => LicenseValidationResult.Invalid
            };

            if (expectedProductId != 0 && productId != expectedProductId && !IsPhoneGradeProduct(productId))
                result = LicenseValidationResult.Invalid;

            return new LicenseValidationResponse(
                valid, error, status, result, productId, limit, usage, instanceId, instanceName);
        }
        catch (Exception)
        {
            // Malformed JSON is a failed validation, never an exception into the scan flow.
            return new LicenseValidationResponse(false, "", "", LicenseValidationResult.Invalid);
        }
    }

    /// <summary>
    /// Maps an activate body to <see cref="LicenseActivationResponse"/>.
    ///
    /// The API reports a full seat list as a 200 with <c>activated: false</c> and a
    /// sentence in <c>error</c>, which is why the boolean alone cannot be trusted
    /// as the decision: an instance that came back without an id is not a seat
    /// anybody can later release, so an activation that claims success without an
    /// instance id is treated as a failure.
    /// </summary>
    public static LicenseActivationResponse ParseActivationResponse(string json, long expectedProductId = PhoneGradeProductId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return FailedActivation("");

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return FailedActivation("");

            bool activated = root.TryGetProperty("activated", out JsonElement activatedElement) &&
                             activatedElement.ValueKind == JsonValueKind.True;
            string error = ReadString(root, "error");
            string status = ReadStatus(root);
            long productId = ReadProductId(root);
            int limit = ReadCounter(root, "activation_limit");
            int usage = ReadCounter(root, "activation_usage");
            (string instanceId, string instanceName) = ReadInstance(root);

            if (expectedProductId != 0 && productId != expectedProductId && !IsPhoneGradeProduct(productId))
                return FailedActivation(error);

            LicenseValidationResult result;
            if (activated && instanceId.Length == 0)
            {
                // A seat with no handle cannot be released, and the Release seat
                // button has nothing to send. Trusting this would strand a seat on
                // the vendor's side with nothing in the app that knows about it,
                // which is the one outcome this whole feature exists to prevent.
                return FailedActivation(error);
            }

            if (activated)
            {
                result = LicenseValidationResult.Valid;
            }
            else if (IsLimitReached(error))
            {
                // Its own state, not Invalid: the key is real, this machine is
                // simply not allowed a seat right now.
                result = LicenseValidationResult.ActivationLimitReached;
            }
            else
            {
                result = status switch
                {
                    "expired" => LicenseValidationResult.Expired,
                    "deactivated" or "disabled" => LicenseValidationResult.Deactivated,
                    _ => LicenseValidationResult.Invalid
                };
            }

            return new LicenseActivationResponse(
                activated, error, instanceId, instanceName, result, limit, usage, productId);
        }
        catch (Exception)
        {
            return FailedActivation("");
        }
    }

    /// <summary>Maps a deactivate body to <see cref="LicenseDeactivationResponse"/>.</summary>
    public static LicenseDeactivationResponse ParseDeactivationResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new LicenseDeactivationResponse(false, "", LicenseValidationResult.Invalid);

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new LicenseDeactivationResponse(false, "", LicenseValidationResult.Invalid);

            bool deactivated = root.TryGetProperty("deactivated", out JsonElement element) &&
                               element.ValueKind == JsonValueKind.True;
            string error = ReadString(root, "error");

            return new LicenseDeactivationResponse(
                deactivated,
                error,
                deactivated ? LicenseValidationResult.Valid : LicenseValidationResult.Invalid);
        }
        catch (Exception)
        {
            return new LicenseDeactivationResponse(false, "", LicenseValidationResult.Invalid);
        }
    }

    /// <summary>
    /// The API's sentence for a full seat list. Matched on the words rather than
    /// on the whole string because the vendor is free to change the wording and
    /// the meaning is what decides the message.
    /// </summary>
    private static bool IsLimitReached(string error) =>
        error.Contains("activation limit", StringComparison.OrdinalIgnoreCase);

    private static LicenseActivationResponse FailedActivation(string error) =>
        new(false, error, "", "", LicenseValidationResult.Invalid, 0, 0, 0);

    private static string ReadStatus(JsonElement root)
    {
        if (root.TryGetProperty("license_key", out JsonElement key) &&
            key.ValueKind == JsonValueKind.Object)
        {
            return ReadString(key, "status").Trim().ToLowerInvariant();
        }
        return "";
    }

    /// <summary>
    /// Reads one of the vendor's activation counters off <c>license_key</c>.
    /// Zero when it is absent, which reads as "not told" rather than as a real
    /// number: the panel hides the seat line when the limit is zero instead of
    /// claiming a plan allows zero machines.
    /// </summary>
    private static int ReadCounter(JsonElement root, string property) =>
        root.TryGetProperty("license_key", out JsonElement key) &&
        key.ValueKind == JsonValueKind.Object &&
        key.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int count)
            ? count
            : 0;

    /// <summary>
    /// Reads the instance a validate or activate answer carries. Both parts are
    /// empty when the body has <c>"instance": null</c>, which is what validating a
    /// bare key looks like.
    ///
    /// The id is a number in every Lemon Squeezy body, while the name is a string,
    /// so it is read as either and normalised to text here. Treating a numeric id
    /// as "not told" would quietly turn a successful activation into a failure,
    /// which is the worst possible place for that bug: the seat is really taken.
    /// </summary>
    private static (string Id, string Name) ReadInstance(JsonElement root)
    {
        if (root.TryGetProperty("instance", out JsonElement instance) &&
            instance.ValueKind == JsonValueKind.Object)
        {
            return (ReadIdentifier(instance, "id"), ReadString(instance, "name"));
        }
        return ("", "");
    }

    /// <summary>Reads a field that arrives as a JSON number or as a JSON string.</summary>
    private static string ReadIdentifier(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out JsonElement element)) return "";
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            _ => ""
        };
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
