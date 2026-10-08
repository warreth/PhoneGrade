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
    private const string ActiveJson = """{"valid":true,"license_key":{"id":1,"status":"active","activation_usage":1},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
    private const string SeatsJson = """{"valid":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":3},"instance":{"id":992,"name":"pg-bench-2","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
    private const string ActivatedJson = """{"activated":true,"error":null,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":1},"instance":{"id":994,"name":"pg-bench-3","created_at":"2026-01-02T03:04:05.000000Z"},"meta":{"product_id":1422604}}""";
    private const string LimitReachedJson = """{"activated":false,"error":"This license key has reached the activation limit.","license_key":{"id":7,"status":"active","activation_limit":2,"activation_usage":2},"meta":{"product_id":1422604}}""";
    private const string DeactivatedBody = """{"deactivated":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":0},"meta":{"product_id":1422604}}""";
    private const string ExpiredJson = """{"valid":false,"license_key":{"id":1,"status":"expired"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
    private const string DeactivatedJson = """{"valid":false,"license_key":{"id":1,"status":"deactivated"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
    private const string DisabledJson = """{"valid":false,"license_key":{"id":1,"status":"disabled"},"meta":{"store_id":1,"product_id":1422604,"product_name":"PhoneGrade Pro"}}""";
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

    [Fact]
    public void ParseValidationResponse_LegacyProductId_IsAccepted()
    {
        // The store's product was recreated under a new id. A key sold before that
        // is still in a customer's hands and must keep unlocking the app.
        LicenseValidationResponse response = LemonSqueezyClient.ParseValidationResponse(
            """{"valid":true,"license_key":{"id":5,"status":"active"},"meta":{"product_id":1400200}}""");

        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.True(response.Valid);
        Assert.True(LemonSqueezyClient.IsPhoneGradeProduct(response.ProductId));
        Assert.False(LemonSqueezyClient.IsPhoneGradeProduct(9999999));
    }

    [Fact]
    public async Task ActivateAsync_RefusedWithABody_KeepsTheVendorWording()
    {
        // The live store refuses a key with every seat taken with a 400 and the
        // sentence that says so; the sentence is what the operator has to read,
        // and the state has to come out as the limit rather than as an invalid key.
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"activated":false,"error":"This license key has reached its activation limit.","license_key":{"id":7,"status":"active","activation_limit":2,"activation_usage":2},"meta":{"product_id":1422604}}""",
                System.Text.Encoding.UTF8, "application/json")
        });

        LicenseActivationResponse response = await client.ActivateAsync("KEY-LIMIT", "pg-bench");

        Assert.False(response.Activated);
        Assert.Equal(LicenseValidationResult.ActivationLimitReached, response.Result);
        Assert.Contains("activation limit", response.Error);
    }

    [Fact]
    public async Task ValidateAsync_ServerErrorWithAnHtmlBody_FallsBackToTheStatusLine()
    {
        // A proxy or an outage can answer with a page rather than with JSON; the
        // status line is the only thing left to say, and it must still be said.
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway</html>", System.Text.Encoding.UTF8, "text/html")
        });

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-ANY");

        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.Contains("HTTP 502", response.Error);
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
            """{"valid":false,"license_key":{"status":"EXPIRED"},"meta":{"product_id":1422604}}""");

        Assert.Equal(LicenseValidationResult.Expired, response.Result);
        Assert.Equal("expired", response.Status);
    }

    // ---- machine seats -------------------------------------------------------------

    [Fact]
    public async Task ValidateDetailedAsync_WithAnInstance_ReadsTheSeatCountersAndTheInstance()
    {
        // The counters are the vendor's, and they are what the panel shows. Reading
        // them here is what keeps a tier ladder out of the source: the app has no
        // table of what a plan allows, so 5 came from the API and not from here.
        using var client = CreateClient(_ => Json(SeatsJson));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-1", "992");

        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.Equal(5, response.ActivationLimit);
        Assert.Equal(3, response.ActivationUsage);
        Assert.Equal("992", response.InstanceId);
        Assert.Equal("pg-bench-2", response.InstanceName);
    }

    [Fact]
    public async Task ValidateDetailedAsync_WithoutAnInstance_ReadsTheBodyAsGivenAndNamesNoInstance()
    {
        // What actually distinguishes a bare-key validate is the request, not the
        // response: the API answers with "instance": null. Here the body carries an
        // instance and the parser reads it, which is why the gate's own tests pin
        // the endpoint sequence rather than relying on this field to be empty.
        using var client = CreateClient(_ => Json(SeatsJson));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-1");

        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.Equal("992", response.InstanceId);
        Assert.Equal(5, response.ActivationLimit); // the counters are still there
    }

    [Fact]
    public async Task ValidateDetailedAsync_WithANullInstanceInTheBody_LeavesTheSeatFieldsEmpty()
    {
        // This is the shape a bare-key validate really comes back in.
        using var client = CreateClient(_ => Json(
            """{"valid":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":3},"instance":null,"meta":{"product_id":1422604}}"""));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-1");

        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.Equal("", response.InstanceId);
        Assert.Equal("", response.InstanceName);
        Assert.Equal(5, response.ActivationLimit); // the counters survive
    }

    [Fact]
    public async Task ValidateDetailedAsync_WithAnInstance_NamesItOnTheWire()
    {
        // The whole point of the parameter: without it the answer says the key is
        // good and says nothing about this machine.
        var handler = new RecordingHandler(_ => Json(SeatsJson));
        using var client = new LemonSqueezyClient(handler);

        await client.ValidateDetailedAsync("  KEY-WITH-SPACE-1234  ", "  992  ");

        Dictionary<string, string> form = ParseForm(handler.LastRequestBody!);
        Assert.Equal("KEY-WITH-SPACE-1234", form["license_key"]);
        Assert.Equal("992", form["instance_id"]);
        Assert.Equal(new[] { "license_key", "instance_id" }, form.Keys);
    }

    [Fact]
    public async Task ValidateDetailedAsync_WithAWhitespaceInstanceId_SendsOnlyTheKey()
    {
        var handler = new RecordingHandler(_ => Json(ActiveJson));
        using var client = new LemonSqueezyClient(handler);

        await client.ValidateDetailedAsync("KEY-1", "   ");

        Assert.Equal(new[] { "license_key" }, ParseForm(handler.LastRequestBody!).Keys);
    }

    [Fact]
    public async Task ValidateDetailedAsync_WithoutSeatCounters_ReportsZeroRatherThanAGuess()
    {
        // Zero reads as "not told", which the panel shows as no seat line. Reading a
        // missing counter as 1 would draw "1 of 1" on a key the API said nothing about.
        using var client = CreateClient(_ => Json(
            """{"valid":true,"license_key":{"id":1,"status":"active","activation_usage":1},"instance":{"id":992,"name":"pg-bench-2"},"meta":{"product_id":1422604}}"""));

        LicenseValidationResponse response = await client.ValidateDetailedAsync("KEY-1", "992");

        Assert.Equal(0, response.ActivationLimit);
        Assert.Equal(1, response.ActivationUsage); // what the body did say is kept
    }

    [Fact]
    public async Task ActivateAsync_ClaimsASeatAndReturnsTheInstance()
    {
        var handler = new RecordingHandler(_ => Json(ActivatedJson));
        using var client = new LemonSqueezyClient(handler);

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-3");

        Assert.Equal(LicenseValidationResult.Valid, response.Result);
        Assert.True(response.Activated);
        Assert.Equal("994", response.InstanceId); // read from a JSON number
        Assert.Equal("pg-bench-3", response.InstanceName);
        Assert.Equal(5, response.ActivationLimit);
        Assert.Equal(1, response.ActivationUsage);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(LemonSqueezyClient.ActivateEndpoint, handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        Dictionary<string, string> form = ParseForm(handler.LastRequestBody!);
        Assert.Equal("KEY-1", form["license_key"]);
        Assert.Equal("pg-bench-3", form["instance_name"]);
        Assert.Equal(new[] { "license_key", "instance_name" }, form.Keys);
    }

    [Fact]
    public async Task ActivateAsync_WithNoSeatLeft_IsTheLimitOutcomeAndNotInvalid()
    {
        // The operator has to be told this computer is over the limit. Answering
        // Invalid would send them hunting for a typo in a key that is perfectly fine.
        using var client = CreateClient(_ => Json(LimitReachedJson));

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-9");

        Assert.Equal(LicenseValidationResult.ActivationLimitReached, response.Result);
        Assert.False(response.Activated);
        Assert.Equal("", response.InstanceId);
        Assert.Equal(2, response.ActivationLimit);
        Assert.Equal(2, response.ActivationUsage);
        Assert.Contains("activation limit", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActivateAsync_WithoutAnInstanceName_DoesNotSpendASeat()
    {
        // An empty instance name would take a seat that can never be validated,
        // released or explained afterwards.
        var handler = new RecordingHandler(_ => Json(ActivatedJson));
        using var client = new LemonSqueezyClient(handler);

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "   ");

        Assert.False(response.Activated);
        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ActivateAsync_ClaimsSuccessButReturnsNoInstance_CountsAsAFailure()
    {
        // A seat with no handle cannot be released, so the app must not believe it.
        // This is the response that would otherwise strand a seat forever.
        using var client = CreateClient(_ => Json(
            """{"activated":true,"license_key":{"id":7,"status":"active","activation_limit":5,"activation_usage":1},"meta":{"product_id":1422604}}"""));

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-3");

        Assert.False(response.Activated);
        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.Equal("", response.InstanceId);
    }

    [Fact]
    public async Task ActivateAsync_ForAnotherProduct_IsRefusedEvenThoughItLooksSuccessful()
    {
        using var client = CreateClient(_ => Json(
            """{"activated":true,"license_key":{"id":2,"status":"active"},"instance":{"id":9,"name":"pg-bench-3"},"meta":{"product_id":9999999}}"""));

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-3");

        Assert.False(response.Activated);
        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.Equal("", response.InstanceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""{"activated":false}""")]
    public async Task ActivateAsync_MalformedOrRefusingBody_IsInvalidNotAnException(string body)
    {
        using var client = CreateClient(_ => Json(body));

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-3");

        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
        Assert.False(response.Activated);
    }

    [Fact]
    public async Task ActivateAsync_TransportFailure_IsInvalid()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-3");

        Assert.False(response.Activated);
        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
    }

    [Fact]
    public async Task ActivateAsync_HttpServerError_IsInvalid()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        LicenseActivationResponse response = await client.ActivateAsync("KEY-1", "pg-bench-3");

        Assert.False(response.Activated);
        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
    }

    [Fact]
    public async Task DeactivateAsync_ReleasesTheSeatAndSendsTheInstanceId()
    {
        var handler = new RecordingHandler(_ => Json(DeactivatedBody));
        using var client = new LemonSqueezyClient(handler);

        LicenseDeactivationResponse response = await client.DeactivateAsync("  KEY-1  ", "  994  ");

        Assert.True(response.Deactivated);
        Assert.Equal(LicenseValidationResult.Valid, response.Result);

        Assert.Equal(LemonSqueezyClient.DeactivateEndpoint, handler.LastRequest!.RequestUri!.ToString());
        Dictionary<string, string> form = ParseForm(handler.LastRequestBody!);
        Assert.Equal("KEY-1", form["license_key"]);
        Assert.Equal("994", form["instance_id"]);
        Assert.Equal(new[] { "license_key", "instance_id" }, form.Keys);
    }

    [Fact]
    public async Task DeactivateAsync_WithoutAnInstanceId_DoesNotCallTheVendor()
    {
        var handler = new RecordingHandler(_ => Json(DeactivatedBody));
        using var client = new LemonSqueezyClient(handler);

        LicenseDeactivationResponse response = await client.DeactivateAsync("KEY-1", "  ");

        Assert.False(response.Deactivated);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DeactivateAsync_WithoutAKey_DoesNotCallTheVendor()
    {
        var handler = new RecordingHandler(_ => Json(DeactivatedBody));
        using var client = new LemonSqueezyClient(handler);

        LicenseDeactivationResponse response = await client.DeactivateAsync("  ", "994");

        Assert.False(response.Deactivated);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("""{"deactivated":false,"error":"The license key is not activated."}""")]
    public async Task DeactivateAsync_AnyRefusal_IsFalseWithoutThrowing(string body)
    {
        using var client = CreateClient(_ => Json(body));

        LicenseDeactivationResponse response = await client.DeactivateAsync("KEY-1", "994");

        Assert.False(response.Deactivated);
        Assert.Equal(LicenseValidationResult.Invalid, response.Result);
    }

    [Fact]
    public async Task DeactivateAsync_TransportFailure_IsFalseRatherThanThrowing()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        LicenseDeactivationResponse response = await client.DeactivateAsync("KEY-1", "994");

        Assert.False(response.Deactivated);
    }

    [Fact]
    public async Task ActivateAsync_CancelledToken_ThrowsRatherThanReportingAFailure()
    {
        // The other direction has to keep working: a caller that asked to stop must
        // really stop, not be told the seat was refused.
        var handler = new RecordingHandler(_ => throw new OperationCanceledException());
        using var client = new LemonSqueezyClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ActivateAsync("KEY-1", "pg-bench-3", cts.Token));
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
