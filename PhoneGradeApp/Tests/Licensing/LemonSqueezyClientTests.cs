using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core.Licensing;
using Xunit;

namespace Tests;

/// <summary>
/// Covers the Lemon Squeezy validate client end to end with a fake transport:
/// response mapping for every status the API reports, product pinning so only
/// keys sold for PhoneGrade count, graceful collapse of transport and payload
/// failures into Invalid, and the exact shape of the POST (URL, method,
/// license_key and nothing the API does not document).
/// </summary>
public class LemonSqueezyClientTests
{
    private const string ActiveJson = """{"valid":true,"license_key":{"id":1,"status":"active","activation_usage":1},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
    private const string ExpiredJson = """{"valid":false,"license_key":{"id":1,"status":"expired"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
    private const string DeactivatedJson = """{"valid":false,"license_key":{"id":1,"status":"deactivated"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
    private const string DisabledJson = """{"valid":false,"license_key":{"id":1,"status":"disabled"},"meta":{"store_id":1,"product_id":1400200,"product_name":"PhoneGrade Pro"}}""";
    private const string UnknownKeyJson = """{"valid":false,"error":"This key is invalid."}""";
    private const string OtherProductJson = """{"valid":true,"license_key":{"id":2,"status":"active"},"meta":{"store_id":1,"product_id":9999999,"product_name":"Some Other Product"}}""";

    [Fact]
    public async Task ValidateAsync_ActiveLicense_ReturnsValid()
    {
        using var client = CreateClient(_ => Json(ActiveJson));

        LicenseValidationResult result = await client.ValidateAsync("KEY-ACTIVE-1234");

        Assert.Equal(LicenseValidationResult.Valid, result);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredLicense_ReturnsExpired()
    {
        using var client = CreateClient(_ => Json(ExpiredJson));

        LicenseValidationResult result = await client.ValidateAsync("KEY-EXPIRED-1234");

        Assert.Equal(LicenseValidationResult.Expired, result);
    }

    [Theory]
    [InlineData(DeactivatedJson)]
    [InlineData(DisabledJson)]
    public async Task ValidateAsync_DeactivatedOrDisabledLicense_ReturnsDeactivated(string json)
    {
        using var client = CreateClient(_ => Json(json));

        LicenseValidationResult result = await client.ValidateAsync("KEY-DEAD-1234");

        Assert.Equal(LicenseValidationResult.Deactivated, result);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKey_ReturnsInvalidAndKeepsErrorText()
    {
        using var client = CreateClient(_ => Json(UnknownKeyJson));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("NOPE");

        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.False(response.Valid);
        Assert.Equal("This key is invalid.", response.Error);
        Assert.Equal("", response.Status);
    }

    [Fact]
    public async Task ValidateAsync_ActiveLicense_ResponseKeepsRawFields()
    {
        using var client = CreateClient(_ => Json(ActiveJson));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-ACTIVE-1234");

        Assert.True(response.Valid);
        Assert.Equal("active", response.Status);
        Assert.Equal("", response.Error);
        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.Equal(LemonSqueezyClient.PhoneGradeProductId, response.ProductId);
    }

    // ---- product pinning -------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_KeyForAnotherProduct_IsRejectedDespiteBeingActive()
    {
        using var client = CreateClient(_ => Json(OtherProductJson));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-OTHER-PRODUCT");

        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.True(response.Valid); // the raw field is kept; only the decision changes
        Assert.Equal(9999999, response.ProductId);
    }

    [Fact]
    public async Task ValidateAsync_ActiveKeyWithoutMeta_IsRejected()
    {
        // A body that claims validity but never says which product it belongs to
        // cannot be trusted: no meta, no Pro.
        using var client = CreateClient(_ =>
            Json("""{"valid":true,"license_key":{"id":3,"status":"active"}}"""));

        LicenseValidationResult result = await client.ValidateAsync("KEY-NO-META");

        Assert.Equal(LicenseValidationResult.Invalid, result);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKeyForAnotherProduct_IsInvalidNotExpired()
    {
        // The status describes somebody else's key; the app must not act on it.
        using var client = CreateClient(_ =>
            Json("""{"valid":false,"license_key":{"id":4,"status":"expired"},"meta":{"product_id":9999999}}"""));

        LicenseValidationResult result = await client.ValidateAsync("KEY-OTHER-EXPIRED");

        Assert.Equal(LicenseValidationResult.Invalid, result);
    }

    [Fact]
    public void ParseValidationResponse_PinCanBeDisabledWithZero()
    {
        LicenseValidationResponse response = LemonSqueezyClient.ParseValidationResponse(
            OtherProductJson, expectedProductId: 0);

        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.Equal(9999999, response.ProductId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"license_key":{"status":"active"}}""")] // no valid field and no status to trust
    public async Task ValidateAsync_MalformedOrIncompleteBody_ReturnsInvalid(string body)
    {
        using var client = CreateClient(_ => Json(body));

        LicenseValidationResult result = await client.ValidateAsync("KEY-ANY");

        Assert.Equal(LicenseValidationResult.Invalid, result);
    }

    [Fact]
    public async Task ValidateAsync_HttpServerError_ReturnsInvalid()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        LicenseValidationResult result = await client.ValidateAsync("KEY-ANY");

        Assert.Equal(LicenseValidationResult.Invalid, result);
    }

    [Fact]
    public async Task ValidateAsync_TransportFailure_ReturnsInvalid()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        LicenseValidationResult result = await client.ValidateAsync("KEY-ANY");

        Assert.Equal(LicenseValidationResult.Invalid, result);
    }

    [Fact]
    public async Task ValidateAsync_EmptyKey_ReturnsInvalidWithoutSendingARequest()
    {
        var handler = new RecordingHandler(_ => Json(ActiveJson));
        using var client = new LemonSqueezyClient(handler);

        LicenseValidationResult result = await client.ValidateAsync("   ");

        Assert.Equal(LicenseValidationResult.Invalid, result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ValidateAsync_SendsPostWithOnlyTheDocumentedFields()
    {
        var handler = new RecordingHandler(_ => Json(ActiveJson));
        using var client = new LemonSqueezyClient(handler);

        await client.ValidateAsync("  KEY-WITH-SPACE-1234  ");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(LemonSqueezyClient.ValidateEndpoint, handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.LastRequest.Content!.Headers.ContentType!.MediaType);

        Dictionary<string, string> form = ParseForm(handler.LastRequestBody!);
        Assert.Equal("KEY-WITH-SPACE-1234", form["license_key"]); // trimmed before send

        // The validate endpoint documents license_key (plus an optional
        // instance_id for instances created by activate). instance_name is not
        // one of them, so nothing else may go on the wire.
        Assert.Equal(new[] { "license_key" }, form.Keys);
    }

    [Fact]
    public async Task ValidateAsync_CancelledToken_ThrowsInsteadOfReportingInvalid()
    {
        var handler = new RecordingHandler(_ =>
        {
            throw new OperationCanceledException();
        });
        using var client = new LemonSqueezyClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ValidateAsync("KEY-1", cts.Token));
    }

    [Fact]
    public void ParseValidationResponse_UppercaseStatus_IsNormalized()
    {
        LicenseValidationResponse response = LemonSqueezyClient.ParseValidationResponse(
            """{"valid":false,"license_key":{"status":"EXPIRED"},"meta":{"product_id":1400200}}""");

        Assert.Equal(LicenseValidationResult.Expired, response.Result);
        Assert.Equal("expired", response.Status);
    }

    // ---- helpers ----------------------------------------------------------------

    private static LemonSqueezyClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new RecordingHandler(responder));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> ParseForm(string body)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator < 0) continue;
            string name = Decode(pair[..separator]);
            string value = Decode(pair[(separator + 1)..]);
            form[name] = value;
        }
        return form;
    }

    private static string Decode(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }
}
