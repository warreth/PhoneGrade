using System;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.Core.Licensing;
using Xunit;
using static Tests.LicensingTestContext;

namespace Tests;

/// <summary>
/// Integration tests for the DeviceService scan initializer: the seam every scan
/// path funnels through. Covers the allow and block verdicts, the fail closed
/// behaviour when the gate itself is broken, and a full run through a real
/// TrialGate down to both storage files.
/// </summary>
public class DeviceServiceScanInitializerTests : IDisposable
{
    private readonly Func<Task<ScanAuthorization>>? _previousGate = DeviceService.ScanGate;

    public void Dispose() => DeviceService.ScanGate = _previousGate;

    [Fact]
    public async Task InitializeScanAsync_GateAllowsPro_ScansProceed()
    {
        DeviceService.ScanGate = () => Task.FromResult(ScanAuthorization.AllowedPro);

        DeviceService.ScanInitResult result = await DeviceService.InitializeScanAsync();

        Assert.Equal(DeviceService.ScanInitResult.Proceed, result);
    }

    [Fact]
    public async Task InitializeScanAsync_GateAllowsTrial_ScansProceed()
    {
        DeviceService.ScanGate = () => Task.FromResult(ScanAuthorization.AllowedTrial);

        Assert.Equal(DeviceService.ScanInitResult.Proceed, await DeviceService.InitializeScanAsync());
    }

    [Fact]
    public async Task InitializeScanAsync_GateReachesLimit_ScanIsBlocked()
    {
        DeviceService.ScanGate = () => Task.FromResult(ScanAuthorization.LimitReached);

        Assert.Equal(DeviceService.ScanInitResult.TrialLimitReached, await DeviceService.InitializeScanAsync());
    }

    [Fact]
    public async Task InitializeScanAsync_GateThrows_FailsClosedAndBlocks()
    {
        DeviceService.ScanGate = () => throw new InvalidOperationException("gate broken");

        Assert.Equal(DeviceService.ScanInitResult.TrialLimitReached, await DeviceService.InitializeScanAsync());
    }

    [Fact]
    public async Task InitializeScanAsync_ThroughRealGate_CountsAndPersistsTheFreeScan()
    {
        using var context = new LicensingTestContext();
        var server = new FakeLicenseServer();
        TrialGate gate = context.CreateGate(server); // fresh install: zero scans
        DeviceService.ScanGate = () => gate.EvaluateAsync();

        DeviceService.ScanInitResult result = await DeviceService.InitializeScanAsync();

        Assert.Equal(DeviceService.ScanInitResult.Proceed, result);
        Assert.Equal(1, gate.ScanCount);
        Assert.Equal(1, context.ReadSettingsState()!.ScanCount);
        Assert.Equal(1, context.ReadBackupState()!.ScanCount);
    }

    [Fact]
    public async Task InitializeScanAsync_ThroughRealGateAtLimit_BlocksWithoutTouchingFiles()
    {
        using var context = new LicensingTestContext();
        var server = new FakeLicenseServer();
        TrialGate gate = context.CreateGate(server, new TrialState { ScanCount = 10 });
        DeviceService.ScanGate = () => gate.EvaluateAsync();

        DeviceService.ScanInitResult result = await DeviceService.InitializeScanAsync();

        Assert.Equal(DeviceService.ScanInitResult.TrialLimitReached, result);
        Assert.Equal(10, context.ReadSettingsState()!.ScanCount);
        Assert.Equal(10, context.ReadBackupState()!.ScanCount);
        Assert.Equal(0, server.CallCount);
    }

    [Fact]
    public async Task InitializeScanAsync_ThroughRealGateWithValidLicenseOnAHeldSeat_IgnoresTheLimit()
    {
        // The instance id is part of what Pro means now: a key with no seat on this
        // machine is a key that was released, and it scans on the free tier.
        using var context = new LicensingTestContext();
        var server = new FakeLicenseServer();
        TrialGate gate = context.CreateGate(server, new TrialState
        {
            ScanCount = 10,
            LicenseKey = "KEY-PRO",
            InstanceId = "991"
        });
        DeviceService.ScanGate = () => gate.EvaluateAsync();

        DeviceService.ScanInitResult result = await DeviceService.InitializeScanAsync();

        Assert.Equal(DeviceService.ScanInitResult.Proceed, result);
        Assert.True(gate.IsPro);
        Assert.Equal(10, gate.ScanCount); // Pro scans never spend free scans
    }
}
