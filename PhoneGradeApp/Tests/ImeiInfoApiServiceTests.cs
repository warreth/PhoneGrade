using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core.SecurityServices;
using Xunit;
using static PhoneGrade.Core.SecurityServices.ImeiInfoApiService;

namespace Tests;

/// <summary>
/// Covers the imei.info BYOK service: published pricing, the cost calculator,
/// local validation, and every HTTP state the v5 gateway can answer with.
///
/// The JSON payloads mirror what the official imei.info SDKs use in their own
/// test fixtures (github.com/imei-info): a search history envelope with a
/// status and a flat snake_case result block, plus the structured error bodies
/// for 401/402/422. Nothing here touches the network; a stub handler stands in
/// for the gateway and records what was requested.
/// </summary>
public class ImeiInfoApiServiceTests
{
    private const string AppleImei = "353541326469521";   // official sandbox: iPhone 12 Pro Max, CLEAN
    private const string BlacklistedImei = "355030794352540"; // official sandbox: Pixel 8 Pro, BLACKLISTED

    private readonly string _apiKey = "test-" + Guid.NewGuid().ToString("N");

    public ImeiInfoApiServiceTests()
    {
        ResetServiceCache();
    }

    // ------------------------------------------------------------------
    // Pricing and calculator
    // ------------------------------------------------------------------

    [Fact]
    public void CheckPrices_AreThePublishedRates()
    {
        Assert.Equal(0.360m, CheckPrices[ImeiCheckType.AppleCarrierLockFmi]);
        Assert.Equal(0.200m, CheckPrices[ImeiCheckType.BlacklistSimple]);
        Assert.Equal(0.420m, CheckPrices[ImeiCheckType.BlacklistPremium]);
        Assert.Equal(0.600m, CheckPrices[ImeiCheckType.SamsungInfoKnox]);
    }

    [Fact]
    public void CheckNamesAndDescriptions_CoverEveryCheckType()
    {
        foreach (ImeiCheckType checkType in Enum.GetValues<ImeiCheckType>())
        {
            Assert.True(CheckNames.ContainsKey(checkType));
            Assert.True(CheckDescriptions.ContainsKey(checkType));
            Assert.False(string.IsNullOrWhiteSpace(CheckNames[checkType]));
            Assert.False(string.IsNullOrWhiteSpace(CheckDescriptions[checkType]));
            // The price sits in CheckPrices and in the settings rows, the
            // description stays a plain sentence about what comes back.
            Assert.StartsWith("Returns", CheckDescriptions[checkType]);
        }
    }

    [Fact]
    public void CalculateCost_SumsTheSelectedChecks()
    {
        decimal total = CalculateCost(Enum.GetValues<ImeiCheckType>());

        Assert.Equal(0.360m + 0.200m + 0.420m + 0.600m, total);
        Assert.Equal(1.580m, total);
    }

    [Fact]
    public void CalculateCost_NoChecks_IsZero()
    {
        Assert.Equal(0m, CalculateCost(Array.Empty<ImeiCheckType>()));
    }

    [Fact]
    public void CalculateEstimatedCost_AppliesChecksToTheirDeviceGroup()
    {
        // 10 Apple and 20 Android devices with every check enabled:
        //   Apple carrier/FMI   0.360 x 10 Apple
        //   Blacklist simple    0.200 x 30 devices
        //   Blacklist premium   0.420 x 30 devices
        //   Samsung/Knox        0.600 x 20 Android
        decimal total = CalculateEstimatedCost(10, 20, Enum.GetValues<ImeiCheckType>());

        Assert.Equal(3.60m + 6.00m + 12.60m + 12.00m, total);
        Assert.Equal(34.20m, total);
    }

    [Fact]
    public void CalculateEstimatedCost_AppleOnlyCheck_IgnoresAndroidDevices()
    {
        decimal total = CalculateEstimatedCost(10, 20, new[] { ImeiCheckType.AppleCarrierLockFmi });

        Assert.Equal(3.60m, total);
    }

    [Fact]
    public void CalculateEstimatedCost_SamsungOnlyCheck_IgnoresAppleDevices()
    {
        decimal total = CalculateEstimatedCost(10, 20, new[] { ImeiCheckType.SamsungInfoKnox });

        Assert.Equal(12.00m, total);
    }

    [Fact]
    public void CalculateEstimatedCost_NoDevices_IsZero()
    {
        decimal total = CalculateEstimatedCost(0, 0, Enum.GetValues<ImeiCheckType>());

        Assert.Equal(0m, total);
    }

    // ------------------------------------------------------------------
    // Local validation, no HTTP involved
    // ------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_WithoutKey_FailsBeforeAnyRequest()
    {
        var stub = new StubHandler();
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistPremium, "", client);

        Assert.False(result.Success);
        Assert.Equal("API key not configured", result.ErrorMessage);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task CheckAsync_WithTooShortImei_FailsWithInvalidImei()
    {
        var stub = new StubHandler();
        using var client = ClientFor(stub);

        var result = await CheckAsync("12345", ImeiCheckType.BlacklistSimple, _apiKey, client);

        Assert.False(result.Success);
        Assert.Equal("Invalid IMEI", result.ErrorMessage);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task GetBalanceAsync_WithoutKey_Fails()
    {
        var stub = new StubHandler();
        using var client = ClientFor(stub);

        var (success, balance, error) = await GetBalanceAsync("", client);

        Assert.False(success);
        Assert.Equal(0m, balance);
        Assert.Equal("API key not configured", error);
        Assert.Empty(stub.Requests);
    }

    // ------------------------------------------------------------------
    // Service discovery
    // ------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_ResolvesServiceIdAndCallsTheSyncEndpoint()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Done(AppleCleanResult(), service: "APPLE: Carrier & Lock Status & FMI")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, client);

        Assert.True(result.Success);
        Assert.Equal(101, result.ServiceId);

        var checkRequest = Assert.Single(stub.Requests, r => r.Path.Contains("/api-sync/check/"));
        Assert.Contains("/api-sync/check/101/", checkRequest.Path);
        Assert.Contains($"API_KEY={_apiKey}", checkRequest.Query);
        Assert.Contains($"imei={AppleImei}", checkRequest.Query);
        Assert.Equal($"Bearer {_apiKey}", checkRequest.Authorization);
    }

    [Fact]
    public async Task CheckAsync_ResolvesEachServiceToItsOwnId()
    {
        var stub = new StubHandler { Services = ServicesJson(), Check = (_, _) => Done(AppleCleanResult()) };
        using var client = ClientFor(stub);

        foreach (var (checkType, expectedId) in new[]
        {
            (ImeiCheckType.AppleCarrierLockFmi, 101),
            (ImeiCheckType.BlacklistSimple, 102),
            (ImeiCheckType.BlacklistPremium, 103),
            (ImeiCheckType.SamsungInfoKnox, 104)
        })
        {
            int id = await ResolveServiceIdAsync(checkType, _apiKey, client);
            Assert.Equal(expectedId, id);
        }
    }

    [Fact]
    public async Task ResolveServiceId_IsCachedAfterTheFirstLookup()
    {
        var stub = new StubHandler { Services = ServicesJson(), Check = (_, _) => Done(AppleCleanResult()) };
        using var client = ClientFor(stub);

        await ResolveServiceIdAsync(ImeiCheckType.BlacklistPremium, _apiKey, client);
        await ResolveServiceIdAsync(ImeiCheckType.BlacklistPremium, _apiKey, client);

        Assert.Single(stub.Requests, r => r.Path.Contains("/service/services/"));
    }

    [Fact]
    public async Task ResolveServiceId_MatchesNamesRegardlessOfCaseAndPunctuation()
    {
        var stub = new StubHandler
        {
            Services = """
            { "results": [ { "id": 555, "name": "apple carrier lock status fmi" } ] }
            """,
            Check = (_, _) => Done(AppleCleanResult())
        };
        using var client = ClientFor(stub);

        int id = await ResolveServiceIdAsync(ImeiCheckType.AppleCarrierLockFmi, _apiKey, client);

        Assert.Equal(555, id);
    }

    [Fact]
    public async Task CheckAsync_WhenTheServiceIsMissing_FailsWithServiceNotFound()
    {
        var stub = new StubHandler
        {
            Services = """{ "results": [ { "id": 9, "name": "Something Else Entirely" } ] }""",
            Check = (_, _) => Done(AppleCleanResult())
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.SamsungInfoKnox, _apiKey, client);

        Assert.False(result.Success);
        Assert.StartsWith("Service not found:", result.ErrorMessage);
        Assert.DoesNotContain(stub.Requests, r => r.Path.Contains("/api-sync/check/"));
    }

    // ------------------------------------------------------------------
    // Successful checks
    // ------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_SandboxAppleResponse_ParsesCleanDevice()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Done(AppleCleanResult(), service: "APPLE: Carrier & Lock Status & FMI")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, client);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("Done", result.Status);
        Assert.Equal(0.360m, result.Price);

        Assert.Equal(false, result.IsBlacklisted);
        Assert.Equal("Unlocked", result.SimLockStatus);
        Assert.Equal("T-Mobile Polska", result.CarrierName);
        Assert.Equal("Apple", result.Manufacturer);
        Assert.Equal("iPhone 12 Pro Max", result.ModelName);
        Assert.NotNull(result.RawData);
    }

    [Fact]
    public async Task CheckAsync_BlacklistedResponse_ParsesBlacklistDetails()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Done(BlacklistedResult(), service: "BLACKLIST: Premium Check")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(BlacklistedImei, ImeiCheckType.BlacklistPremium, _apiKey, client);

        Assert.True(result.Success);
        Assert.Equal(true, result.IsBlacklisted);
        Assert.Equal("Lost / Stolen", result.BlacklistReason);
        Assert.Equal("T-Mobile USA", result.BlacklistedBy);
        Assert.Equal(new DateTime(2025, 11, 2), result.BlacklistedOn);
        Assert.Equal("United States", result.BlacklistedCountry);
        Assert.Equal("Locked", result.SimLockStatus);
        Assert.Equal("Google", result.Manufacturer);
        Assert.Equal("Pixel 8 Pro", result.ModelName);
    }

    [Fact]
    public async Task CheckAsync_SamsungResponse_ParsesKnoxAndWarranty()
    {
        string samsungResult = """
        {
          "imei": "350545260771498",
          "brand": "Samsung",
          "model": "Galaxy S24 Ultra",
          "blacklist_status": "CLEAN",
          "model_number": "SM-S928B",
          "warranty_status": "Active until 2026-08-14",
          "knox_status": "WARRANTY VOID (0x1)"
        }
        """;

        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Done(samsungResult, service: "GENERIC: Samsung Info Check & Knox Info")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync("350545260771498", ImeiCheckType.SamsungInfoKnox, _apiKey, client);

        Assert.True(result.Success);
        Assert.Equal(false, result.IsBlacklisted);
        Assert.Equal("Samsung", result.Manufacturer);
        Assert.Equal("Galaxy S24 Ultra", result.ModelName);
        Assert.Equal("SM-S928B", result.ModelNumber);
        Assert.Equal("WARRANTY VOID (0x1)", result.KnoxStatus);
        Assert.Equal("Active until 2026-08-14", result.WarrantyStatus);
    }

    [Fact]
    public async Task CheckAsync_FmiStatus_IsReadFromTheResultBlock()
    {
        // The FMI field name differs between the Apple services, so the parser
        // accepts every alias imei.info is known to use.
        foreach (string field in new[] { "fmi_status", "find_my_iphone", "icloud_status" })
        {
            ResetServiceCache();
            string appleResult = $$"""
            { "brand": "Apple", "model": "iPhone 13", "{{field}}": "OFF" }
            """;

            var stub = new StubHandler
            {
                Services = ServicesJson(),
                Check = (_, _) => Done(appleResult)
            };
            using var client = ClientFor(stub);

            var result = await CheckAsync(AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, client);

            Assert.True(result.Success, $"{field}: {result.ErrorMessage}");
            Assert.Equal("OFF", result.FmiStatus);
        }
    }

    [Fact]
    public async Task CheckAsync_PendingResponse_PollsSearchHistoryUntilDone()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Json(HttpStatusCode.Accepted, """
            { "message": "Search is in progress", "history_id": 42, "ulid": "01J0TEST00000000000000042" }
            """),
            History = (_, _) => Done(AppleCleanResult(), id: 42)
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, client);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Done", result.Status);
        Assert.Equal(42, result.RequestId);
        Assert.Contains(stub.Requests, r => r.Path.Contains("/api/search_history/42/"));
    }

    [Fact]
    public async Task CheckAsync_WhenTheGatewayRejectsTheCheck_FailsWithTheStatus()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Json(HttpStatusCode.OK, """
            { "id": 7, "status": "Rejected", "service_id": 102, "imei": "353541326469521" }
            """)
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistSimple, _apiKey, client);

        Assert.False(result.Success);
        Assert.Contains("Rejected", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckMultipleAsync_ReturnsOneResultPerCheck()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Done(BlacklistedResult())
        };
        using var client = ClientFor(stub);

        var results = await CheckMultipleAsync(
            BlacklistedImei,
            new[] { ImeiCheckType.BlacklistSimple, ImeiCheckType.BlacklistPremium, ImeiCheckType.SamsungInfoKnox },
            _apiKey,
            client);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(3, results.Select(r => r.CheckType).Distinct().Count());
        Assert.Single(stub.Requests, r => r.Path.Contains("/service/services/"));
    }

    // ------------------------------------------------------------------
    // Failure states
    // ------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_WithoutCredits_ReportsTheBalanceMessage()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Json(HttpStatusCode.PaymentRequired, """
            {
              "error": "Payment Required",
              "code": "insufficient_credits",
              "message": "Your API balance is $0.00. Please recharge your account in the developer dashboard at dash.imei.info before executing queries."
            }
            """)
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistPremium, _apiKey, client);

        Assert.False(result.Success);
        Assert.Contains("API balance", result.ErrorMessage);
        Assert.Contains("dash.imei.info", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WithAnInvalidToken_ReportsTheDetailEnvelope()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            // Verified live: the api-sync path answers 200 with this body.
            Check = (_, _) => Json(HttpStatusCode.OK, """{ "detail": "Token is invalid." }""")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistSimple, _apiKey, client);

        Assert.False(result.Success);
        Assert.Equal("Token is invalid.", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WithAnInvalidChecksum_ReportsTheValidationMessage()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Json(HttpStatusCode.UnprocessableEntity, $$"""
            {
              "error": "Unprocessable Entity",
              "code": "invalid_luhn_checksum",
              "message": "Luhn checksum for IMEI '{{AppleImei}}' is invalid. Make sure the IMEI is 15 digits long and has a valid check digit."
            }
            """)
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, client);

        Assert.False(result.Success);
        Assert.Contains("Luhn checksum", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WhenRateLimited_FallsBackToTheStatusCode()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Json(HttpStatusCode.TooManyRequests, "")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistSimple, _apiKey, client);

        Assert.False(result.Success);
        Assert.Equal("Rate limit exceeded, try again shortly", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WhenTheServiceIsUnknown_FallsBackToTheStatusCode()
    {
        var stub = new StubHandler
        {
            Services = ServicesJson(),
            Check = (_, _) => Json(HttpStatusCode.NotFound, "")
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistPremium, _apiKey, client);

        Assert.False(result.Success);
        Assert.Equal("Service not found", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WhenTheServiceListRejectsTheKey_FailsWithTheAuthError()
    {
        var stub = new StubHandler
        {
            ServicesStatus = HttpStatusCode.Forbidden,
            Services = "",
            Check = (_, _) => Done(AppleCleanResult())
        };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistSimple, _apiKey, client);

        Assert.False(result.Success);
        Assert.Equal("Authentication credentials were not provided", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckAsync_WhenTheNetworkIsDown_ReportsANetworkError()
    {
        var stub = new StubHandler { Thrown = new HttpRequestException("No such host is known") };
        using var client = ClientFor(stub);

        var result = await CheckAsync(AppleImei, ImeiCheckType.BlacklistSimple, _apiKey, client);

        Assert.False(result.Success);
        Assert.StartsWith("Network error:", result.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // Balance
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetBalanceAsync_ParsesAStringBalance()
    {
        var stub = new StubHandler { Balance = """{ "id": 7, "key": "abc", "balance": "12.34" }""" };
        using var client = ClientFor(stub);

        var (success, balance, error) = await GetBalanceAsync(_apiKey, client);

        Assert.True(success);
        Assert.Equal(12.34m, balance);
        Assert.Null(error);
        Assert.Contains(stub.Requests, r => r.Path.Contains("/account/account/"));
    }

    [Fact]
    public async Task GetBalanceAsync_ParsesANumericBalance()
    {
        var stub = new StubHandler { Balance = """{ "balance": 5.5 }""" };
        using var client = ClientFor(stub);

        var (success, balance, _) = await GetBalanceAsync(_apiKey, client);

        Assert.True(success);
        Assert.Equal(5.5m, balance);
    }

    [Fact]
    public async Task GetBalanceAsync_WithoutCredits_FailsWithTheApiMessage()
    {
        var stub = new StubHandler
        {
            BalanceStatus = HttpStatusCode.PaymentRequired,
            Balance = """
            {
              "error": "Payment Required",
              "code": "insufficient_credits",
              "message": "Your API balance is $0.00. Please recharge your account in the developer dashboard at dash.imei.info before executing queries."
            }
            """
        };
        using var client = ClientFor(stub);

        var (success, balance, error) = await GetBalanceAsync(_apiKey, client);

        Assert.False(success);
        Assert.Equal(0m, balance);
        Assert.Contains("API balance", error);
    }

    [Fact]
    public async Task GetBalanceAsync_WithAnInvalidToken_FailsWithTheDetailEnvelope()
    {
        var stub = new StubHandler { Balance = """{ "detail": "Token is invalid." }""" };
        using var client = ClientFor(stub);

        var (success, _, error) = await GetBalanceAsync(_apiKey, client);

        Assert.False(success);
        Assert.Equal("Token is invalid.", error);
    }

    [Fact]
    public async Task GetBalanceAsync_WhenTheBodyCarriesNoBalance_Fails()
    {
        var stub = new StubHandler { Balance = """{ "id": 7, "key": "abc" }""" };
        using var client = ClientFor(stub);

        var (success, _, error) = await GetBalanceAsync(_apiKey, client);

        Assert.False(success);
        Assert.Equal("Could not parse balance from response", error);
    }

    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    /// <summary>The service list the gateway exposes, with our four checks.</summary>
    private static string ServicesJson() => """
    {
      "count": 4,
      "next": null,
      "results": [
        { "id": 101, "name": "APPLE: Carrier & Lock Status & FMI", "slug": "apple-carrier-lock-fmi", "price": "0.360" },
        { "id": 102, "name": "BLACKLIST: Simple Check", "slug": "blacklist-simple-check", "price": "0.200" },
        { "id": 103, "name": "BLACKLIST: Premium Check", "slug": "blacklist-premium-check", "price": "0.420" },
        { "id": 104, "name": "GENERIC: Samsung Info Check & Knox Info", "slug": "samsung-info-knox", "price": "0.600" }
      ]
    }
    """;

    /// <summary>Sandbox result for a clean, unlocked Apple device.</summary>
    private static string AppleCleanResult() => $$"""
    {
      "imei": "{{AppleImei}}",
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

    /// <summary>Sandbox result for a blacklisted, carrier locked device.</summary>
    private static string BlacklistedResult() => """
    {
      "imei": "355030794352540",
      "brand": "Google",
      "model": "Pixel 8 Pro",
      "tac": "35503079",
      "blacklist_status": "BLACKLISTED",
      "carrier_lock": true,
      "blacklist_reason": "Lost / Stolen",
      "blacklisted_by": "T-Mobile USA",
      "blacklisted_on": "2025-11-02",
      "blacklisted_country": "United States",
      "purchase_country": "United States"
    }
    """;

    /// <summary>Wraps a result block in the search history envelope.</summary>
    private static HttpResponseMessage Done(
        string resultJson,
        int id = 1,
        string status = "Done",
        string service = "BLACKLIST: Premium Check") =>
        Json(HttpStatusCode.OK, $$"""
        {
          "id": {{id}},
          "ulid": "01J0000000000000000000000",
          "status": "{{status}}",
          "service": "{{service}}",
          "created_at": "2026-10-03T10:00:00Z",
          "imei": "{{AppleImei}}",
          "token_request_price": "0.360",
          "result": {{resultJson}},
          "is_custom_result": false
        }
        """);

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpClient ClientFor(StubHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri(ImeiInfoApiService.BaseUrl) };

    /// <summary>
    /// Stands in for the gateway. The check endpoint accepts a responder so a
    /// test can vary its answer per IMEI, everything else is a fixed payload.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? Services { get; init; }
        public HttpStatusCode ServicesStatus { get; init; } = HttpStatusCode.OK;
        public string? Balance { get; init; }
        public HttpStatusCode BalanceStatus { get; init; } = HttpStatusCode.OK;
        public Func<string, string, HttpResponseMessage>? Check { get; init; }
        public Func<string, string, HttpResponseMessage>? History { get; init; }
        public Exception? Thrown { get; init; }

        public List<RecordedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Thrown is not null)
                throw Thrown;

            var uri = request.RequestUri!;
            Requests.Add(new RecordedRequest(
                uri.AbsolutePath,
                uri.Query,
                request.Headers.Authorization?.ToString() ?? ""));

            string path = uri.AbsolutePath;

            if (path.Contains("/service/services/"))
                return Task.FromResult(Json(ServicesStatus, Services ?? ""));

            if (path.Contains("/account/account/"))
                return Task.FromResult(Json(BalanceStatus, Balance ?? ""));

            if (path.Contains("/api/search_history/"))
                return Task.FromResult(History?.Invoke(path, uri.Query) ?? Json(HttpStatusCode.NotFound, ""));

            if (path.Contains("/api-sync/check/") || path.Contains("/api/check/"))
                return Task.FromResult(Check?.Invoke(path, uri.Query) ?? Json(HttpStatusCode.NotFound, ""));

            return Task.FromResult(Json(HttpStatusCode.NotFound, ""));
        }
    }

    private sealed record RecordedRequest(string Path, string Query, string Authorization);
}
