using System;
using System.Threading.Tasks;
using PhoneGrade.Core.Licensing;
using Xunit;
using static Tests.LicensingTestContext;

namespace Tests;

/// <summary>
/// Covers the scan decision matrix: free scans count and persist to both
/// locations, an active license never counts, the limit blocks, and validation
/// failures fail closed into the free tier rules. Cache behaviour is checked
/// through the fake transport's call count.
/// </summary>
public class TrialGateTests : IDisposable
{
    private readonly LicensingTestContext _context = new();

    public void Dispose() => _context.Dispose();

    // ---- free tier -------------------------------------------------------------

    [Fact]
    public async Task EvaluateAsync_FirstFreeScan_AllowsAndPersistsToBothLocations()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server);

        ScanAuthorization decision = await gate.EvaluateAsync();

        Assert.Equal(ScanAuthorization.AllowedTrial, decision);
        Assert.Equal(1, gate.ScanCount);
        Assert.False(gate.IsPro);
        Assert.Equal(1, _context.ReadSettingsState()!.ScanCount); // written immediately
        Assert.Equal(1, _context.ReadBackupState()!.ScanCount);
        Assert.Equal(0, server.CallCount); // no key, no API traffic
    }

    [Fact]
    public async Task EvaluateAsync_NinthScanAllowed_TenthScanAllowed_AndBothPersisted()
    {
        TrialGate gate = _context.CreateGate(new FakeLicenseServer(), new TrialState { ScanCount = 8 });

        Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());
        Assert.Equal(9, gate.ScanCount);

        Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());
        Assert.Equal(10, gate.ScanCount);
        Assert.Equal(10, _context.ReadSettingsState()!.ScanCount);
        Assert.Equal(10, _context.ReadBackupState()!.ScanCount);
    }

    [Fact]
    public async Task EvaluateAsync_AtLimit_BlocksWithoutIncrementingOrCallingTheApi()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 10 });

        ScanAuthorization first = await gate.EvaluateAsync();
        ScanAuthorization second = await gate.EvaluateAsync();

        Assert.Equal(ScanAuthorization.LimitReached, first);
        Assert.Equal(ScanAuthorization.LimitReached, second);
        Assert.Equal(10, gate.ScanCount);
        Assert.Equal(10, _context.ReadSettingsState()!.ScanCount);
        Assert.Equal(10, _context.ReadBackupState()!.ScanCount);
        Assert.Equal(0, server.CallCount);
    }

    // ---- pro tier --------------------------------------------------------------

    /// <summary>
    /// Pro is a seat on this machine, not a key that is valid somewhere. Every Pro
    /// case below therefore seeds an instance id as well as a key: a key with no
    /// seat is a key whose seat was released, or one this machine never claimed,
    /// and neither scans for free.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ValidLicenseOnAHeldSeat_AllowsWithoutTouchingTheCount()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 9,
            LicenseKey = "KEY-PRO",
            InstanceId = "991"
        });

        ScanAuthorization decision = await gate.EvaluateAsync();

        Assert.Equal(ScanAuthorization.AllowedPro, decision);
        Assert.True(gate.IsPro);
        Assert.Equal(9, gate.ScanCount);
        Assert.Equal(9, _context.ReadSettingsState()!.ScanCount); // not incremented
        Assert.Equal(9, _context.ReadBackupState()!.ScanCount);
        Assert.Equal(1, server.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_ValidLicenseOnAHeldSeatAtLimit_AllowsUnlimitedScans()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 10,
            LicenseKey = "KEY-PRO",
            InstanceId = "991"
        });

        Assert.Equal(ScanAuthorization.AllowedPro, await gate.EvaluateAsync());
        Assert.Equal(ScanAuthorization.AllowedPro, await gate.EvaluateAsync());
        Assert.Equal(10, gate.ScanCount);
        Assert.Equal(1, server.CallCount); // second scan came from the cache
    }

    // ---- invalid licenses fall back to the free tier rules ---------------------

    [Theory]
    [InlineData(FakeLicenseServer.ExpiredJson)]
    [InlineData(FakeLicenseServer.DeactivatedJson)]
    [InlineData(FakeLicenseServer.InvalidKeyJson)]
    public async Task EvaluateAsync_UnusableLicenseAtLimit_Blocks(string response)
    {
        var server = new FakeLicenseServer { ResponseJson = response };
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 10, LicenseKey = "KEY-BAD" });

        Assert.Equal(ScanAuthorization.LimitReached, await gate.EvaluateAsync());
        Assert.False(gate.IsPro);
        Assert.Equal(10, gate.ScanCount);
    }

    [Fact]
    public async Task EvaluateAsync_UnusableLicenseUnderLimit_ConsumesAFreeScan()
    {
        var server = new FakeLicenseServer { ResponseJson = FakeLicenseServer.ExpiredJson };
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 9, LicenseKey = "KEY-EXPIRED" });

        Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());
        Assert.False(gate.IsPro);
        Assert.Equal(10, gate.ScanCount);
    }

    [Fact]
    public async Task EvaluateAsync_ApiUnreachable_FailsClosedIntoFreeTierRules()
    {
        var server = new FakeLicenseServer { FailWithNetworkError = true };

        TrialGate atLimit = _context.CreateGate(server, new TrialState { ScanCount = 10, LicenseKey = "KEY-PRO" });
        Assert.Equal(ScanAuthorization.LimitReached, await atLimit.EvaluateAsync());
        Assert.False(atLimit.IsPro); // offline never grants Pro

        TrialGate underLimit = _context.CreateGate(server, new TrialState { ScanCount = 3, LicenseKey = "KEY-PRO" });
        Assert.Equal(ScanAuthorization.AllowedTrial, await underLimit.EvaluateAsync());
        Assert.Equal(4, underLimit.ScanCount); // but the trial keeps working
    }

    [Fact]
    public async Task EvaluateAsync_KeyForAnotherProductAtLimit_Blocks()
    {
        // A healthy, active key that belongs to a different product on the same
        // endpoint must not open the Pro tier.
        var server = new FakeLicenseServer { ResponseJson = FakeLicenseServer.OtherProductJson };
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 10, LicenseKey = "KEY-OTHER-PRODUCT" });

        ScanAuthorization decision = await gate.EvaluateAsync();

        Assert.Equal(ScanAuthorization.LimitReached, decision);
        Assert.False(gate.IsPro);
        Assert.Equal(10, gate.ScanCount);
    }

    // ---- validation cache ------------------------------------------------------

    [Fact]
    public async Task EvaluateAsync_SecondScanWithinCacheWindow_DoesNotCallTheApiAgain()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 1,
            LicenseKey = "KEY-PRO",
            InstanceId = "991"
        });

        await gate.EvaluateAsync();
        await gate.EvaluateAsync();
        await gate.EvaluateAsync();

        Assert.Equal(1, server.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_PositiveCacheExpires_Revalidates()
    {
        var server = new FakeLicenseServer();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 1,
            LicenseKey = "KEY-PRO",
            InstanceId = "991"
        }, clock);

        await gate.EvaluateAsync();
        Assert.Equal(1, server.CallCount);

        clock.Advance(TrialGate.PositiveCacheTtl + TimeSpan.FromMinutes(1));
        await gate.EvaluateAsync();

        Assert.Equal(2, server.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_NegativeCacheExpires_RetriesWithinAMinute()
    {
        var server = new FakeLicenseServer { FailWithNetworkError = true };
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 1,
            LicenseKey = "KEY-PRO",
            InstanceId = "991"
        }, clock);

        await gate.EvaluateAsync(); // fails, counted as no license
        Assert.Equal(1, server.CallCount);

        clock.Advance(TrialGate.NegativeCacheTtl + TimeSpan.FromSeconds(1));
        await gate.EvaluateAsync();

        Assert.Equal(2, server.CallCount); // network is worth retrying soon
    }

    // ---- activation ------------------------------------------------------------

    [Fact]
    public async Task ActivateLicenseAsync_ValidKey_PersistsItToBothLocationsAndSetsPro()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 4 });

        LicenseValidationResult result = await gate.ActivateLicenseAsync("  KEY-NEW-1234  ");

        Assert.Equal(LicenseValidationResult.Valid, result);
        Assert.True(gate.IsPro);
        Assert.Equal("KEY-NEW-1234", gate.LicenseKey); // trimmed
        Assert.Equal(4, gate.ScanCount);               // activation never spends scans
        Assert.Equal("KEY-NEW-1234", _context.ReadSettingsState()!.LicenseKey);
        Assert.Equal("KEY-NEW-1234", _context.ReadBackupState()!.LicenseKey);
    }

    [Fact]
    public async Task ActivateLicenseAsync_WrongKey_KeepsStoredKeyAndProStatus()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 4,
            LicenseKey = "KEY-GOOD",
            InstanceId = "991"
        });
        Assert.True(await gate.RefreshLicenseStatusAsync()); // stored key and seat are Pro
        server.ResponseJson = FakeLicenseServer.InvalidKeyJson;

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-WRONG");

        Assert.Equal(LicenseValidationResult.Invalid, result);
        Assert.Equal("KEY-GOOD", gate.LicenseKey);
        Assert.True(gate.IsPro); // the stored key was not invalidated by a typo
        Assert.Equal("KEY-GOOD", _context.ReadSettingsState()!.LicenseKey);
    }

    [Fact]
    public async Task ActivateLicenseAsync_StoredKeyTurnsInvalid_DropsProFlag()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 4,
            LicenseKey = "KEY-GOOD",
            InstanceId = "991"
        });
        Assert.True(await gate.RefreshLicenseStatusAsync());

        server.ResponseJson = FakeLicenseServer.ExpiredJson; // vendor expires it later
        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-GOOD");

        Assert.Equal(LicenseValidationResult.Expired, result);
        Assert.False(gate.IsPro);
        Assert.Equal("KEY-GOOD", gate.LicenseKey); // key kept for the retry message
    }

    [Fact]
    public async Task ActivateLicenseAsync_EmptyKey_ReturnsInvalidWithoutCallingTheApi()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server);

        Assert.Equal(LicenseValidationResult.Invalid, await gate.ActivateLicenseAsync("   "));
        Assert.Equal(0, server.CallCount);
        Assert.Equal("", gate.LicenseKey);
    }

    [Fact]
    public async Task RefreshLicenseStatusAsync_NoStoredKey_IsProWithoutCallingTheApi()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server);

        Assert.False(await gate.RefreshLicenseStatusAsync());
        Assert.False(gate.IsPro);
        Assert.Equal(0, server.CallCount);
    }

    // ---- persistence across restarts -------------------------------------------

    [Fact]
    public async Task StateSurvivesANewGateInstance_ScansDoNotResetOnRestart()
    {
        var server = new FakeLicenseServer();
        TrialGate first = _context.CreateGate(server);
        await first.EvaluateAsync();
        await first.EvaluateAsync();
        await first.ActivateLicenseAsync("KEY-KEPT");
        int used = first.ScanCount;

        // Simulated restart: a brand new gate over the same two files.
        TrialGate restarted = new(_context.CreateStore(), new LemonSqueezyClient(server));

        Assert.Equal(used, restarted.ScanCount);
        Assert.Equal("KEY-KEPT", restarted.LicenseKey);
    }
}
