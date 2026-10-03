using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PhoneGrade.Core.SecurityServices;
using Xunit;
using static PhoneGrade.Core.SecurityServices.ImeiInfoApiService;

namespace Tests;

/// <summary>
/// Runs the service against the sandbox imei.info publishes for integration
/// testing: three IMEIs with a fixed answer and HTTP 402 for every other one.
///
/// The sandbox listens on a loopback socket rather than behind a message
/// handler, so the request leaves this process, goes over the wire and comes
/// back with the status code the sandbox chose. What it received is recorded
/// and asserted as well, which is the half a stubbed handler cannot show.
/// </summary>
public class ImeiInfoSandboxTests : IDisposable
{
    private readonly SandboxGateway _gateway = new();
    private readonly HttpClient _client;
    private readonly string _apiKey = "sandbox-" + Guid.NewGuid().ToString("N");

    public ImeiInfoSandboxTests()
    {
        ResetServiceCache();
        _client = _gateway.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _gateway.Dispose();
        ResetServiceCache();
    }

    // ------------------------------------------------------------------
    // The three sandbox numbers
    // ------------------------------------------------------------------

    [Fact]
    public async Task TheAppleSandboxNumber_AnswersWithAnIphone12ProMax()
    {
        var result = await CheckAsync(SandboxGateway.AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, _client);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Apple", result.Manufacturer);
        Assert.Equal("iPhone 12 Pro Max", result.ModelName);
        Assert.Equal(false, result.IsBlacklisted);
        Assert.Equal("Unlocked", result.SimLockStatus);
        Assert.Equal("T-Mobile Polska", result.CarrierName);
        Assert.Equal(0.360m, result.Price);
        Assert.Equal("Apple A14 Bionic", Specification(result, "cpu"));
        Assert.Equal(128, SpecificationNumber(result, "storage_gb"));
    }

    [Fact]
    public async Task TheSamsungSandboxNumber_AnswersWithAGalaxyS24Ultra()
    {
        var result = await CheckAsync(SandboxGateway.SamsungImei, ImeiCheckType.SamsungInfoKnox, _apiKey, _client);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Samsung", result.Manufacturer);
        Assert.Equal("Galaxy S24 Ultra", result.ModelName);
        Assert.Equal(false, result.IsBlacklisted);
        Assert.Equal("Orange Polska", result.CarrierName);
        Assert.Equal(0.600m, result.Price);
        Assert.Equal("Snapdragon 8 Gen 3", Specification(result, "cpu"));
        Assert.Equal(256, SpecificationNumber(result, "storage_gb"));
    }

    [Fact]
    public async Task TheBlacklistedSandboxNumber_AnswersWithABlacklistedPixel8Pro()
    {
        var result = await CheckAsync(SandboxGateway.BlacklistedImei, ImeiCheckType.BlacklistSimple, _apiKey, _client);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(true, result.IsBlacklisted);
        Assert.Equal("Google", result.Manufacturer);
        Assert.Equal("Pixel 8 Pro", result.ModelName);
        Assert.Equal("T-Mobile USA", result.CarrierName);
        Assert.Equal("Locked", result.SimLockStatus);
        Assert.Equal(0.200m, result.Price);
    }

    // ------------------------------------------------------------------
    // Everything else
    // ------------------------------------------------------------------

    [Fact]
    public async Task AnyOtherImei_IsRefusedWithThePaymentRequiredAnswer()
    {
        // Both are well formed IMEIs that are not on the sandbox list, so both
        // have to reach the sandbox before the refusal means anything.
        foreach (string imei in new[] { SandboxGateway.ControlImei, "111111111111111" })
        {
            ResetServiceCache();

            var result = await CheckAsync(imei, ImeiCheckType.BlacklistSimple, _apiKey, _client);

            Assert.False(result.Success, $"{imei} was checked without credit");
            Assert.Contains("API balance", result.ErrorMessage);
            Assert.Contains("dash.imei.info", result.ErrorMessage);
            Assert.Null(result.RawData);

            var sent = Assert.Single(_gateway.Requests, r => r.Query.Contains($"imei={imei}"));
            Assert.Equal(402, sent.StatusCode);
        }
    }

    // ------------------------------------------------------------------
    // What the sandbox is asked
    // ------------------------------------------------------------------

    [Fact]
    public async Task EveryCheckResolvesToItsOwnService()
    {
        var ids = new Dictionary<ImeiCheckType, int>();
        foreach (ImeiCheckType checkType in Enum.GetValues<ImeiCheckType>())
            ids[checkType] = await ResolveServiceIdAsync(checkType, _apiKey, _client);

        Assert.Equal(2, ids[ImeiCheckType.AppleCarrierLockFmi]);
        Assert.Equal(27, ids[ImeiCheckType.BlacklistSimple]);
        Assert.Equal(28, ids[ImeiCheckType.BlacklistPremium]);
        Assert.Equal(76, ids[ImeiCheckType.SamsungInfoKnox]);
        Assert.Equal(4, ids.Values.Distinct().Count());
    }

    [Fact]
    public async Task EveryRequestCarriesTheKeyAndTheCheckGoesToTheResolvedService()
    {
        var result = await CheckAsync(SandboxGateway.AppleImei, ImeiCheckType.AppleCarrierLockFmi, _apiKey, _client);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.ServiceId);

        // One listing to resolve the service, one check, nothing else: the
        // answer is final, so there is no history to poll.
        var requests = _gateway.Requests;
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal($"Bearer {_apiKey}", r.Authorization));
        Assert.Contains(requests, r => r.Path.Contains("/api/service/services/"));

        var check = Assert.Single(requests, r => r.Path.Contains("/api-sync/check/"));
        Assert.Contains("/api-sync/check/2/", check.Path);
        Assert.Contains($"API_KEY={_apiKey}", check.Query);
        Assert.Contains($"imei={SandboxGateway.AppleImei}", check.Query);
        Assert.Equal(200, check.StatusCode);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>One string out of the specifications block the sandbox returned.</summary>
    private static string? Specification(ImeiCheckResult result, string name) =>
        TrySpecifications(result, out var specs)
        && specs.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>One number out of the specifications block the sandbox returned.</summary>
    private static int? SpecificationNumber(ImeiCheckResult result, string name) =>
        TrySpecifications(result, out var specs)
        && specs.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int number)
            ? number
            : null;

    private static bool TrySpecifications(ImeiCheckResult result, out JsonElement specs)
    {
        specs = default;
        if (result.RawData is not JsonElement data)
            return false;

        return data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("specifications", out specs)
            && specs.ValueKind == JsonValueKind.Object;
    }
}
