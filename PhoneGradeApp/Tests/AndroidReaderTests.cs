using PhoneGrade.Core;
using PhoneGrade.Core.Diagnostics;
using PhoneGrade.Core.SecurityServices;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// The Android reader, driven with output shaped like the handsets it meets.
///
/// Everything here is synthetic: a property dump, a parcel, a dumpsys line.
/// Nothing carries a serial, an IMEI or an address from a real phone, and the
/// readers still have to get every one of them right, which is what the
/// handset-on-the-bench checks used to prove with recorded output.
/// </summary>
public class AndroidReaderTests
{
    /// <summary>A serial substituted for the one a real handset printed.</summary>
    private const string Serial = "TEST0000000001A";

    /// <summary>IMEI1 and IMEI2 for the slot-numbered calls, shaped like the shell prints them.</summary>
    private const string Imei1Parcel = """
        Result: Parcel(
        0x00000000: 00000000 0000000f 00350033 00320031 '........3.5.1.2.'
        0x00000010: 00340033 00360035 00380037 00300039 '3.4.5.6.7.8.9.0.'
        0x00000020: 00320031 00000037                   '1.2.7...        ')
        """;

    private const string Imei2Parcel = """
        Result: Parcel(
        0x00000000: 00000000 0000000f 00350033 00320031 '........3.5.1.2.'
        0x00000010: 00340033 00360035 00380037 00300039 '3.4.5.6.7.8.9.0.'
        0x00000020: 00330031 00000035                   '1.3.5...        ')
        """;

    /// <summary>A property dump that names the phone, its build and its serial.</summary>
    private const string HonorStyleGetprop = """
        [ro.config.marketing_name]: [HONOR X8b]
        [ro.product.brand]: [HONOR]
        [ro.product.model]: [LLY-LX1]
        [ro.product.product.model]: [magic]
        [ro.product.vendor.model]: [Bengal for arm64]
        [ro.build.fingerprint]: [HONOR/LLY-LX1EEA/HNLLY-Q:14/HONORLLY-L31/8.0.0.366C431E205R2P3:user/release-keys]
        [ro.build.version.release]: [14]
        [ro.serialno]: [TEST0000000001A]
        [ro.boot.flash.locked]: [1]
        [ro.boot.vbmeta.device_state]: [locked]
        [ro.boot.verifiedbootstate]: [green]
        """;

    /// <summary>A dumpsys battery with a level and a health code, and nothing else.</summary>
    private const string DumpsysBattery = """
        Current Battery Service state:
          status: 5
          health: 2
          level: 100
        """;

    private static AndroidDeviceReader Reader(Func<string, string> shell) =>
        new(Serial, command => Task.FromResult(shell(command)));

    /// <summary>The reader that also hears refusals, the way adb reports them.</summary>
    private static AndroidDeviceReader GuardedReader(Func<string, (string Stdout, bool Refused)> shell) =>
        new(Serial, command => Task.FromResult(shell(command)));

    [Fact]
    public async Task TheNameThePhoneIsSoldUnderIsPreferredOverTheFactoryCode()
    {
        var reader = Reader(command => command == "getprop" ? HonorStyleGetprop : "");
        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal("Honor X8b", data.Model);
        Assert.Equal("Android (Honor X8b)", data.ProductType);
        Assert.Equal("Android 14", data.IosVersion);
    }

    [Fact]
    public async Task TheImeiIsReadThroughTheShellsOwnPackage()
    {
        var reader = Reader(command => command switch
        {
            "service call iphonesubinfo 4 i32 0 s16 com.android.shell" => Imei1Parcel,
            "service call iphonesubinfo 4 i32 1 s16 com.android.shell" => Imei2Parcel,
            _ => "",
        });

        var facts = await reader.ReadAsync();
        Assert.Equal("351234567890127", facts.Imei1);
        Assert.Equal("351234567890135", facts.Imei2);

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Equal("351234567890127", data.Identifier);
        Assert.Equal("351234567890135", data.Imei2);
        Assert.Equal(Serial, data.MotherboardSerialNumber);
    }

    [Fact]
    public async Task ASlotThatRepeatsTheFirstImeiIsNotStoredTwice()
    {
        // A phone without a second card can answer the primary IMEI for the
        // second slot as well. Two IMEIs is a claim about the phone.
        var reader = Reader(command => command switch
        {
            "service call iphonesubinfo 4 i32 0 s16 com.android.shell" => Imei1Parcel,
            "service call iphonesubinfo 4 i32 1 s16 com.android.shell" => Imei1Parcel,
            _ => "",
        });

        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());
        Assert.Equal("", data.Imei2);
    }

    [Fact]
    public void AnErrorParcelIsARefusalAndNotAnImei()
    {
        const string refusal = "Result: Parcel(\tfffffffc ffffffff 00000000  '............')";

        Assert.Equal("", Parsers.ParseAndroidImei(refusal));
        Assert.True(Parsers.IsParcelError(refusal));
    }

    [Fact]
    public async Task AMacTheShellRefusesFallsBackToIpAndDumpsys()
    {
        var reader = GuardedReader(command => command switch
        {
            "cat /sys/class/net/wlan0/address 2>/dev/null" => ("cat: /sys/class/net/wlan0/address: Permission denied", true),
            "ip addr show wlan0" => ("36: wlan0: <BROADCAST,MULTICAST,UP> mtu 1500\n    link/ether aa:bb:cc:dd:ee:02 brd ff:ff:ff:ff:ff:ff\n", false),
            "settings get secure bluetooth_address 2>/dev/null" => ("SecurityException", true),
            "dumpsys bluetooth_manager" => ("Bluetooth Status\n  enabled: false\n  address: 00:11:22:33:44:55\n", false),
            _ => ("", false),
        });

        var facts = await reader.ReadAsync();

        Assert.Equal("AA:BB:CC:DD:EE:02", facts.WifiMacAddress);
        Assert.Equal("00:11:22:33:44:55", facts.BluetoothMacAddress);

        // The fallback answered, so the refused primaries are not held against
        // the handset. Nothing else was refused either.
        Assert.DoesNotContain("wlan0_address", facts.Withheld);
        Assert.DoesNotContain("bluetooth_address", facts.Withheld);
    }

    [Fact]
    public async Task ARefusedReadIsRememberedByName()
    {
        var reader = new AndroidDeviceReader(Serial, command =>
        {
            bool refused = command.Contains("charge_full", StringComparison.Ordinal)
                        || command.Contains("wlan0", StringComparison.Ordinal);
            return Task.FromResult((refused ? "" : "", refused));
        });

        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Contains("charge_full", data.WithheldReads);
        Assert.Contains("charge_full_design", data.WithheldReads);
        Assert.Contains("wlan0_address", data.WithheldReads);
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("null", false)]
    [InlineData("Permission denied", false)]
    [InlineData("AA:BB:CC:DD:EE", false)]
    [InlineData("AA:BB:CC:DD:EE:FF:00", false)]
    [InlineData("ZZ:BB:CC:DD:EE:FF", false)]
    public void OnlyAnAddressIsAcceptedAsAMacAddress(string candidate, bool accepted)
        => Assert.Equal(accepted, AndroidDeviceReader.IsMacAddress(candidate));

    [Fact]
    public async Task TheLearnedBatteryCapacityStandsInForTheRefusedCounter()
    {
        var reader = Reader(command => command switch
        {
            "dumpsys battery" => DumpsysBattery,
            "dumpsys batterystats" => "  Estimated battery capacity: 3500 mAh\n  Last learned battery capacity: 4180 mAh\n",
            _ => "",
        });

        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal(4180, data.BatteryCurrentCapacity);
        Assert.Equal(0, data.BatteryDesignCapacity);
        Assert.Equal("Good", data.BatteryHealth);
        Assert.Equal(100, data.BatteryLevel);
    }

    [Fact]
    public async Task TheSysfsCountersAreScaledToMilliampHours()
    {
        var reader = Reader(command => command switch
        {
            "dumpsys battery" => DumpsysBattery,
            "cat /sys/class/power_supply/battery/charge_full" => "4618000\n",
            "cat /sys/class/power_supply/battery/charge_full_design" => "5022000\n",
            _ => "",
        });

        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal(4618, data.BatteryCurrentCapacity);
        Assert.Equal(5022, data.BatteryDesignCapacity);
        Assert.Equal("92%", data.BatteryHealth);
    }

    [Fact]
    public async Task AnUntamperedPhoneIsReportedAsUntampered()
    {
        var reader = Reader(command => command == "getprop" ? HonorStyleGetprop : "");
        var facts = await reader.ReadAsync();

        Assert.Equal("1", facts.FlashLocked);
        Assert.Equal("locked", facts.VbmetaDeviceState);
        Assert.Equal("green", facts.VerifiedBootState);
        Assert.Equal("", facts.WarrantyBit);

        var checks = AndroidIntegrityChecks.Build(facts);
        Assert.Equal(ComponentStatusType.Passed, StatusOf(checks, "Bootloader"));
        Assert.Equal(ComponentStatusType.Passed, StatusOf(checks, "Vbmeta"));
        Assert.Equal(ComponentStatusType.Passed, StatusOf(checks, "Systeemimage"));
        Assert.Equal(ComponentStatusType.Unknown, StatusOf(checks, "Warrantybit"));
    }

    [Fact]
    public async Task ATamperedPhoneBecomesAFinding()
    {
        var reader = Reader(command => command == "getprop" ? HonorStyleGetprop : "");
        var tampered = (await reader.ReadAsync()) with
        {
            FlashLocked = "0",
            VbmetaDeviceState = "unlocked",
            VerifiedBootState = "orange",
        };

        var checks = AndroidIntegrityChecks.Build(tampered);
        Assert.Equal(ComponentStatusType.Failed, StatusOf(checks, "Bootloader"));
        Assert.Equal(ComponentStatusType.Failed, StatusOf(checks, "Vbmeta"));

        var data = AndroidDeviceReader.ToDeviceData(tampered);
        data.ComponentChecks = checks;

        var findings = DiagnosticService.ComponentFindings(checks);
        Assert.Contains(findings, f => f.Title.Contains("Bootloader", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Title.Contains("Vbmeta", StringComparison.Ordinal));
        Assert.All(findings, f => Assert.Equal(Severity.Error, f.Level));
    }

    [Theory]
    [InlineData("0x0", ComponentStatusType.Passed)]
    [InlineData("0x00", ComponentStatusType.Passed)]
    [InlineData("0", ComponentStatusType.Passed)]
    [InlineData("0x1", ComponentStatusType.Failed)]
    [InlineData("1", ComponentStatusType.Failed)]
    public void TheWarrantyBitIsReadInEverySpellingAFactoryPhoneUses(string reported, ComponentStatusType expected)
    {
        var checks = AndroidIntegrityChecks.Build(new AndroidDeviceFacts { WarrantyBit = reported });
        Assert.Equal(expected, StatusOf(checks, "Warrantybit"));
    }

    [Fact]
    public async Task ASimInThePhoneIsNotACarrierLock()
    {
        var withCard = await DetectCarrierAsync(gsmSimState: "READY", carrier: "Vodafone");
        Assert.Equal("Vodafone", withCard.CarrierName);
        Assert.Null(withCard.IsCarrierLocked);

        // Android 10 and later leave ro.carrier empty, and answer the current
        // key instead. That is a network name, not a lock either.
        var modern = await DetectCarrierAsync(gsmSimState: "READY,ABSENT", carrier: "", operatorAlpha: "Vodafone");
        Assert.Equal("Vodafone", modern.CarrierName);

        // With no card the operator property is a bare separator, which is not
        // a network and must not reach the report as one.
        var bare = await DetectCarrierAsync(gsmSimState: "ABSENT,ABSENT", carrier: "unknown", operatorAlpha: ",");
        Assert.Equal("ABSENT,ABSENT", bare.SIMState);
        Assert.Null(bare.CarrierName);
        Assert.Null(bare.IsCarrierLocked);
    }

    [Fact]
    public async Task FrpIsReadFromTheHandsetInsteadOfNeverBeingAsked()
    {
        var unknown = await FrpLockService.DetectAsync(Serial, _ => Task.FromResult("null"));
        Assert.Equal(FrpLockService.FrpLockStatus.Unknown, unknown);

        var unlocked = await FrpLockService.DetectAsync(Serial, _ => Task.FromResult("0"));
        Assert.Equal(FrpLockService.FrpLockStatus.Unlocked, unlocked);

        var locked = await FrpLockService.DetectAsync(Serial, _ => Task.FromResult("1"));
        Assert.Equal(FrpLockService.FrpLockStatus.Locked, locked);
    }

    private static ComponentStatusType StatusOf(IReadOnlyList<ComponentStatus> checks, string name) =>
        checks.First(c => c.Name == name).Status;

    private static Task<FrpLockService.CarrierLockStatus> DetectCarrierAsync(
        string gsmSimState, string carrier, string operatorAlpha = "") =>
        FrpLockService.DetectCarrierLockAsync(Serial, command =>
        {
            if (command.Contains("gsm.sim.state", StringComparison.Ordinal))
                return Task.FromResult(gsmSimState);
            if (command.Contains("gsm.operator.alpha", StringComparison.Ordinal))
                return Task.FromResult(operatorAlpha);
            if (command.Contains("ro.carrier", StringComparison.Ordinal))
                return Task.FromResult(carrier);
            return Task.FromResult("");
        });
}
