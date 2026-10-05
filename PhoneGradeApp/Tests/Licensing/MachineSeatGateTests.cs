using System;
using System.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core.Licensing;
using Xunit;
using static Tests.LicensingTestContext;

namespace Tests;

/// <summary>
/// Covers the machine-bound half of licensing: claiming a seat, keeping it across
/// restarts, giving it back, and telling the difference between "this key is
/// wrong" and "this key is fine but this computer is not one of the seats".
///
/// The seam is the HTTP handler and the fingerprint, nothing else. <see cref="TrialGate"/>
/// itself is never replaced or subclassed, so what these tests describe is the real
/// decision path a button in the panel takes.
///
/// The fault that matters most is a wasted seat. Activate is the one call that
/// creates something on the vendor's side, and if the app calls it when it already
/// holds a seat, or when it cannot write the seat down afterwards, the operator
/// loses a seat that only the Release seat button can get back.
/// </summary>
public class MachineSeatGateTests : IDisposable
{
    private readonly LicensingTestContext _context = new();

    public void Dispose() => _context.Dispose();

    // ---- claiming a seat ------------------------------------------------------------

    [Fact]
    public async Task ActivateLicenseAsync_ValidKey_ClaimsASeatAndWritesTheInstanceIdToBothLocations()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 4 });

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-NEW-1234");

        Assert.Equal(LicenseValidationResult.Valid, result);
        Assert.True(gate.IsPro);
        Assert.True(gate.HasSeat);

        // The seat id has to be on disk before the method returns: a crash between
        // the vendor's call and the write would leave a seat taken with nothing to
        // release it from.
        Assert.Equal("994", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal("994", _context.ReadBackupState()!.InstanceId);
        Assert.Equal("KEY-NEW-1234", _context.ReadSettingsState()!.LicenseKey);
        Assert.Equal(4, _context.ReadSettingsState()!.ScanCount); // activation never spends scans
    }

    [Fact]
    public async Task ActivateLicenseAsync_SendsTheFingerprintAsTheInstanceNameAndNeverTheHostname()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, fingerprint: TestFingerprint);

        await gate.ActivateLicenseAsync("KEY-NEW-1234");

        Assert.Equal(new[] { "pg-0123456789abcdef0123456789abcdef" }, server.ClaimedInstanceNames);
        Assert.DoesNotContain(Environment.MachineName, server.ClaimedInstanceNames);
        Assert.DoesNotContain(Environment.UserName, server.ClaimedInstanceNames);
    }

    [Fact]
    public async Task ActivateLicenseAsync_OnAMachineWithNoIdentity_DoesNotCallActivateAndKeepsTheFreeTier()
    {
        // An empty instance name would spend a seat that cannot be validated or
        // released afterwards, and the operator could get round the limit by
        // breaking the fingerprint read.
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, fingerprint: UnidentifiableMachine);

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-NEW-1234");

        Assert.Equal(LicenseValidationResult.Invalid, result);
        Assert.DoesNotContain(LemonSqueezyClient.ActivateEndpoint, server.Endpoints);
        Assert.False(gate.IsPro);
        Assert.False(gate.HasSeat);
        Assert.Equal("", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal("", _context.ReadSettingsState()!.LicenseKey);

        // The ten free scans must not care whether a machine id was readable.
        Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());
    }

    // ---- the seat limit, which is its own outcome -----------------------------------

    [Fact]
    public async Task ActivateLicenseAsync_WhenEverySeatIsTaken_SaysSoAndWritesNothing()
    {
        var server = new FakeLicenseServer
        {
            ResponseJson = FakeLicenseServer.ActiveJson,
            ActivateResponseJson = FakeLicenseServer.ActivationLimitJson
        };
        TrialGate gate = _context.CreateGate(server, new TrialState { ScanCount = 4 });

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-AT-LIMIT");

        // Not Invalid: the operator has to be told this computer is over the limit,
        // or they go looking for a typo in a key that is perfectly fine.
        Assert.Equal(LicenseValidationResult.ActivationLimitReached, result);
        Assert.False(gate.IsPro);
        Assert.False(gate.HasSeat);
        Assert.Equal("", _context.ReadSettingsState()!.LicenseKey);
        Assert.Equal("", _context.ReadBackupState()!.LicenseKey);
    }

    [Fact]
    public async Task ActivateLicenseAsync_AtTheLimit_LeavesAnAlreadyStoredKeyAndSeatAlone()
    {
        var server = new FakeLicenseServer { ActivateResponseJson = FakeLicenseServer.ActivationLimitJson };
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 4,
            LicenseKey = "KEY-ALREADY-HERE",
            InstanceId = "991",
            MachineFingerprint = "pg-some-other-machine"
        });

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-AT-LIMIT");

        Assert.Equal(LicenseValidationResult.ActivationLimitReached, result);
        Assert.Equal("KEY-ALREADY-HERE", gate.LicenseKey);
        Assert.True(gate.HasSeat);
        Assert.Equal("991", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal("991", _context.ReadBackupState()!.InstanceId);
    }

    // ---- idempotence, which is what stops seats leaking ------------------------------

    [Fact]
    public async Task ActivateLicenseAsync_WhenThisComputerAlreadyHoldsTheSeat_MakesNoActivateCall()
    {
        // The case that burns a seat if it is wrong: the operator re-pastes the key
        // they activated yesterday, on the same bench.
        var server = new FakeLicenseServer { ResponseJson = FakeLicenseServer.SeatHeldJson };
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            LicenseKey = "KEY-ALREADY-HERE",
            InstanceId = "992"
        });

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-ALREADY-HERE");

        Assert.Equal(LicenseValidationResult.Valid, result);
        Assert.True(gate.IsPro);
        Assert.DoesNotContain(LemonSqueezyClient.ActivateEndpoint, server.Endpoints);
        Assert.Equal(new[] { "992" }, server.NamedInstanceIds); // asked about the held seat
    }

    [Fact]
    public async Task ActivateLicenseAsync_RePastingAKeyActivatedOnAnotherBench_ReportsTheLimitInsteadOfTakingASeat()
    {
        // Validation of the bare key succeeds (the key is fine) but activate refuses,
        // because the plan's seats are taken elsewhere. The operator must not end up
        // with a seat burned on a computer that cannot use it.
        var server = new FakeLicenseServer
        {
            ResponseJson = FakeLicenseServer.ActiveJson,
            ActivateResponseJson = FakeLicenseServer.ActivationLimitJson
        };
        TrialGate gate = _context.CreateGate(server);

        LicenseValidationResult result = await gate.ActivateLicenseAsync("KEY-FROM-ANOTHER-BENCH");

        Assert.Equal(LicenseValidationResult.ActivationLimitReached, result);
        Assert.False(gate.HasSeat);
        Assert.Equal("", _context.ReadBackupState()!.InstanceId);
    }

    // ---- the startup path --------------------------------------------------------------

    [Fact]
    public async Task RefreshLicenseStatusAsync_WithASeatStored_ValidatesTheInstanceAndNotTheBareKey()
    {
        var server = new FakeLicenseServer { ResponseJson = FakeLicenseServer.SeatHeldJson };
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            LicenseKey = "KEY-HELD",
            InstanceId = "992"
        });

        Assert.True(await gate.RefreshLicenseStatusAsync());

        Assert.Equal(new[] { "992" }, server.NamedInstanceIds);
        Assert.Equal(3, gate.MachineCount);
        Assert.Equal(5, gate.MachineLimit);
    }

    [Fact]
    public async Task RefreshLicenseStatusAsync_WithAKeyButNoSeat_ReportsTheFreeTierAndTakesNoSeat()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState { LicenseKey = "KEY-BARE" });

        // A key with no seat reports what the key is and takes no seat on its own: the
        // operator pressed nothing here, and a seat that appears because the app
        // started is one the shop cannot see going.
        Assert.False(await gate.RefreshLicenseStatusAsync());
        Assert.Equal(new[] { LemonSqueezyClient.ValidateEndpoint }, server.Endpoints);
        Assert.Equal(new[] { "" }, server.NamedInstanceIds);
        Assert.False(gate.HasSeat);
        Assert.Equal("", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());
    }

    [Fact]
    public async Task RefreshLicenseStatusAsync_WhenTheSeatIsGone_DropsToTheFreeTierButKeepsTheHandle()
    {
        // The seat was released from another machine. Pro has to go, and the stored
        // instance id has to stay: it is what the Release seat button and a later
        // re-activation need, and dropping it here would quietly take another seat.
        var server = new FakeLicenseServer { ResponseJson = FakeLicenseServer.SeatNotHeldJson };
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 2,
            LicenseKey = "KEY-HELD",
            InstanceId = "992"
        });

        Assert.False(await gate.RefreshLicenseStatusAsync());

        Assert.False(gate.IsPro);
        Assert.True(gate.HasSeat);
        Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());
        Assert.Equal("992", _context.ReadSettingsState()!.InstanceId);
    }

    // ---- handing a seat back -----------------------------------------------------------

    [Fact]
    public async Task DeactivateLicenseAsync_ReleasesTheSeatAndPutsTheAppBackOnTheFreeTier()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            ScanCount = 6,
            LicenseKey = "KEY-TO-RELEASE",
            InstanceId = "994"
        });
        Assert.True(await gate.RefreshLicenseStatusAsync());

        LicenseDeactivationResponse response = await gate.DeactivateLicenseAsync();

        Assert.True(response.Deactivated);
        Assert.False(gate.IsPro);
        Assert.False(gate.HasSeat);
        Assert.Equal(0, gate.MachineCount);

        // Both locations must agree, or the next load merges a seat back in.
        Assert.Equal("", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal("", _context.ReadBackupState()!.InstanceId);
        Assert.Equal(6, _context.ReadSettingsState()!.ScanCount);   // the count is not the seat's
        Assert.Equal("KEY-TO-RELEASE", _context.ReadSettingsState()!.LicenseKey); // key kept for re-activation

        // The vendor was told which seat to give back.
        Dictionary<string, string> sent = server.Forms.Last();
        Assert.Equal(LemonSqueezyClient.DeactivateEndpoint, server.Endpoints.Last());
        Assert.Equal("KEY-TO-RELEASE", sent["license_key"]);
        Assert.Equal("994", sent["instance_id"]);
    }

    [Fact]
    public async Task DeactivateLicenseAsync_ForgetsTheSeatEvenWhenTheVendorNoLongerKnowsIt()
    {
        // The local half of the seat is the half the app controls. A seat the vendor
        // already dropped still has to stop being validated here, or the app keeps
        // believing it holds something it does not.
        var server = new FakeLicenseServer
        {
            DeactivateResponseJson = """{"deactivated":false,"error":"The license key is not activated."}"""
        };
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            LicenseKey = "KEY-ALREADY-GONE",
            InstanceId = "994"
        });

        LicenseDeactivationResponse response = await gate.DeactivateLicenseAsync();

        Assert.False(response.Deactivated);
        Assert.False(gate.HasSeat);
        Assert.False(gate.IsPro);
        Assert.Equal("", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal("", _context.ReadBackupState()!.InstanceId);
    }

    [Fact]
    public async Task DeactivateLicenseAsync_WithNoSeatStored_CallsTheVendorWithNothing()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server);

        LicenseDeactivationResponse response = await gate.DeactivateLicenseAsync();

        Assert.False(response.Deactivated);
        Assert.Equal(0, server.CallCount);
    }

    [Fact]
    public async Task DeactivateLicenseAsync_ThenAFreshScan_CountsAFreeScanInsteadOfGoingPro()
    {
        // The seat is gone and the cache was dropped, so the next scan asks the
        // vendor again and finds nothing holding.
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, new TrialState
        {
            LicenseKey = "KEY-TO-RELEASE",
            InstanceId = "994"
        });
        await gate.DeactivateLicenseAsync();

        server.ResponseJson = FakeLicenseServer.ActiveJson; // the key itself is still fine
        ScanAuthorization decision = await gate.EvaluateAsync();

        Assert.Equal(ScanAuthorization.AllowedTrial, decision);
        Assert.False(gate.IsPro);
        Assert.Equal(1, gate.ScanCount);
    }

    // ---- seats across a restart ---------------------------------------------------------

    [Fact]
    public async Task TheSeatSurvivesANewGateInstance_SoAProShopDoesNotReActivateEveryLaunch()
    {
        var server = new FakeLicenseServer();
        TrialGate first = _context.CreateGate(server);
        await first.ActivateLicenseAsync("KEY-KEEP-SEAT");

        // Simulated restart: a brand new gate over the same two files.
        TrialGate restarted = new(
            _context.CreateStore(),
            new LemonSqueezyClient(server),
            fingerprint: TestFingerprint);

        Assert.True(restarted.HasSeat);
        Assert.Equal("994", _context.ReadSettingsState()!.InstanceId);
        Assert.Equal("994", _context.ReadBackupState()!.InstanceId);

        int callsBeforeRefresh = server.CallCount;
        server.ResponseJson = FakeLicenseServer.SeatHeldJson; // the seat is on this machine

        Assert.True(await restarted.RefreshLicenseStatusAsync());

        // Only the calls this gate made, and it asked about the seat it found on
        // disk rather than the bare key: no second activate on every launch.
        Assert.Equal(new[] { "994" }, server.NamedInstanceIds.Skip(callsBeforeRefresh));
        Assert.DoesNotContain(LemonSqueezyClient.ActivateEndpoint, server.Endpoints.Skip(callsBeforeRefresh));
    }

    // ---- the free tier, which must not need any of this --------------------------------

    [Fact]
    public async Task FreeTierWithNoKeyAndNoFingerprint_StillAllowsTenScansAndBlocksTheEleventh()
    {
        var server = new FakeLicenseServer();
        TrialGate gate = _context.CreateGate(server, fingerprint: UnidentifiableMachine);

        for (int scan = 1; scan <= TrialGate.FreeScanLimit; scan++)
            Assert.Equal(ScanAuthorization.AllowedTrial, await gate.EvaluateAsync());

        Assert.Equal(ScanAuthorization.LimitReached, await gate.EvaluateAsync());
        Assert.Equal(TrialGate.FreeScanLimit, gate.ScanCount);
        Assert.Equal(TrialGate.FreeScanLimit, _context.ReadSettingsState()!.ScanCount);
        Assert.Equal(TrialGate.FreeScanLimit, _context.ReadBackupState()!.ScanCount);
        Assert.Equal(0, server.CallCount); // no key, nothing to ask
    }
}