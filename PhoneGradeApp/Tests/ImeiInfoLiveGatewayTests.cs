using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core.SecurityServices;
using Xunit;
using static PhoneGrade.Core.SecurityServices.ImeiInfoApiService;

namespace Tests;

/// <summary>
/// Which account state a live fact is written for.
///
/// imei.info refuses a check before it runs one when the account cannot pay
/// for it, so the two states answer different questions and neither can be
/// asserted from the other.
/// </summary>
public enum LiveAccount
{
    /// <summary>The fact holds whatever the balance happens to be.</summary>
    AnyBalance,

    /// <summary>The fact needs an account that can pay for a check.</summary>
    WithCredit,

    /// <summary>The fact needs an account that cannot pay for a check.</summary>
    WithoutCredit
}

/// <summary>
/// A fact that talks to the real gateway at dash.imei.info.
///
/// It reports itself as skipped, with the reason, when no key was supplied or
/// when the account is in a state the assertions were not written for. A test
/// that quietly returned instead would look the same as a test that passed.
/// </summary>
public sealed class ImeiInfoLiveFactAttribute : FactAttribute
{
    public ImeiInfoLiveFactAttribute(LiveAccount account = LiveAccount.AnyBalance)
    {
        string? key = ImeiInfoLiveKey.TryRead();
        if (key is null)
        {
            Skip = "No imei.info key: set " + ImeiInfoLiveKey.EnvironmentVariable
                + " or write one to " + string.Join("/", ImeiInfoLiveKey.FilePath) + ".";
            return;
        }

        if (account == LiveAccount.AnyBalance)
            return;

        var (ok, balance, error) = ImeiInfoLiveKey.Balance(key);
        if (!ok)
        {
            Skip = "dash.imei.info did not answer the balance query: " + error;
            return;
        }

        if (account == LiveAccount.WithCredit && balance <= 0)
            Skip = $"The imei.info account holds {balance:F2} USD, so every check is refused before it runs.";
        else if (account == LiveAccount.WithoutCredit && balance > 0)
            Skip = $"The imei.info account holds {balance:F2} USD, so nothing is refused.";
    }
}

/// <summary>
/// The same service against the live gateway: service discovery, the account,
/// and the answers the published sandbox IMEIs get.
///
/// The key comes from the environment or from a gitignored file, never from
/// this file. Three of these facts need no credit; the one that runs actual
/// checks says so in its skip reason while the account sits at 0.00.
/// </summary>
public class ImeiInfoLiveGatewayTests
{
    // The four numbers imei.info publishes for integration testing.
    private const string AppleImei = "353541326469521";
    private const string SamsungImei = "350545260771498";
    private const string BlacklistedImei = "355030794352540";

    /// <summary>A well formed IMEI that is not one of the published numbers.</summary>
    private const string ControlImei = "358742091234567";

    private static string Key => ImeiInfoLiveKey.TryRead() ?? "";

    [ImeiInfoLiveFact]
    public async Task TheGateway_ListsEveryCheckThisAppOffers()
    {
        var ids = new Dictionary<ImeiCheckType, int>();
        foreach (ImeiCheckType checkType in Enum.GetValues<ImeiCheckType>())
            ids[checkType] = await ResolveServiceIdAsync(checkType, Key);

        foreach (var pair in ids)
            Assert.True(pair.Value > 0, $"{pair.Key} did not resolve against the live service list");

        Assert.Equal(4, ids.Values.Distinct().Count());
    }

    [ImeiInfoLiveFact]
    public async Task TheGateway_ReportsTheAccountBalance()
    {
        var (success, balance, error) = await GetBalanceAsync(Key);

        Assert.True(success, error);
        Assert.True(balance >= 0, $"the gateway reported a balance of {balance}");
    }

    /// <summary>
    /// On an account that cannot pay, every IMEI is refused, including the
    /// published sandbox numbers and a plain control number. That is the state
    /// the SDK documentation describes as credit exhaustion, and the answer
    /// must come back as the gateway's own words rather than as a check that
    /// still looks like it is running.
    /// </summary>
    [ImeiInfoLiveFact(LiveAccount.WithoutCredit)]
    public async Task ACheckOnAnAccountWithoutCredit_IsRefusedWithTheGatewayWording()
    {
        foreach (string imei in new[] { AppleImei, SamsungImei, BlacklistedImei, ControlImei })
        {
            var result = await CheckAsync(imei, ImeiCheckType.BlacklistSimple, Key);

            Assert.False(result.Success, $"{imei} came back with a result on an account that cannot pay for it");
            Assert.Equal("Request is too expensive.", result.ErrorMessage);
            Assert.NotNull(result.RawData);
        }
    }

    /// <summary>
    /// The published sandbox numbers, once the account can pay for a check.
    /// imei.info publishes them as free to test with; if that ever changes,
    /// this run costs the price of the three checks below.
    ///
    /// The fourth documented rule, HTTP 402 for any other IMEI, needs an
    /// account with no credit to show itself, which is what the fact above
    /// covers; on a funded account a plain IMEI is a paid check instead.
    /// </summary>
    [ImeiInfoLiveFact(LiveAccount.WithCredit)]
    public async Task TheSandboxNumbers_AnswerWithTheDocumentedDevices()
    {
        var apple = await CheckAsync(AppleImei, ImeiCheckType.AppleCarrierLockFmi, Key);
        Assert.True(apple.Success, apple.ErrorMessage);
        Assert.Equal("Apple", apple.Manufacturer);
        Assert.Contains("iPhone 12 Pro Max", apple.ModelName ?? "");
        Assert.Equal(false, apple.IsBlacklisted);

        var samsung = await CheckAsync(SamsungImei, ImeiCheckType.SamsungInfoKnox, Key);
        Assert.True(samsung.Success, samsung.ErrorMessage);
        Assert.Equal("Samsung", samsung.Manufacturer);
        Assert.Contains("Galaxy S24 Ultra", samsung.ModelName ?? "");
        Assert.Equal(false, samsung.IsBlacklisted);

        var blacklisted = await CheckAsync(BlacklistedImei, ImeiCheckType.BlacklistSimple, Key);
        Assert.True(blacklisted.Success, blacklisted.ErrorMessage);
        Assert.Equal(true, blacklisted.IsBlacklisted);
        Assert.Equal("Google", blacklisted.Manufacturer);
        Assert.Contains("Pixel 8 Pro", blacklisted.ModelName ?? "");
    }
}
