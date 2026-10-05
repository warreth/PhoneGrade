using PhoneGrade.Core;
using PhoneGrade.Core.Diagnostics;
using PhoneGrade.Core.SecurityServices;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// Runs the Android reader against what an actual handset answered.
///
/// The capture under <c>Tests/Fixtures/live</c> comes from a Honor X8b
/// (LLY-LX1EEA) on Android 14, build 8.0.0.366C431E205R2P3. Every expectation
/// here is read off that capture.
///
/// A good deal of this handset answers "no", and that is the point: it refuses
/// the battery counters, refuses the MAC files, refuses the IMEI and refuses the
/// secure settings. A reader that cannot tell a refusal from an absent value
/// either invents data or silently reports nothing, and both are graded on.
/// </summary>
public class AndroidLiveDeviceTests
{
    // The capture carries a substituted serial rather than the one the handset
    // printed. It keeps the shape adb reports, so the reader still has to resolve
    // it correctly, which is what the capture is there to prove.
    private const string Serial = "TEST0000000001A";

    private static string Answer(string command) =>
        LiveFixture.Read(LiveFixture.HonorX8b, LiveFixture.CaptureNameFor(command));

    /// <summary>The reader wired to the capture, so it runs the same code as the bench.</summary>
    private static async Task<AndroidDeviceFacts> ReadFactsAsync() =>
        await new AndroidDeviceReader(Serial, LiveFixture.AdbShellFromCapture(LiveFixture.HonorX8b))
            .ReadAsync();

    [Fact]
    public void ThePropertyDumpComesBackAsALookup()
    {
        var props = AndroidDeviceReader.ParseGetpropOutput(Answer("getprop"));

        Assert.True(props.Count > 1000, $"only {props.Count} properties were read from the dump");

        Assert.Equal("HONOR", props["ro.product.brand"]);
        Assert.Equal("LLY-LX1", props["ro.product.model"]);
        Assert.Equal("14", props["ro.build.version.release"]);
        Assert.Equal("34", props["ro.build.version.sdk"]);
        Assert.Equal(Serial, props["ro.serialno"]);
    }

    /// <summary>
    /// The handset is sold as "HONOR X8b" and its factory code is "LLY-LX1". The
    /// brand is already part of the marketing name, so the readable name is
    /// "Honor X8b" rather than the name written twice.
    /// </summary>
    [Fact]
    public async Task TheHandsetIsNamedTheWayItIsSold()
    {
        var facts = await ReadFactsAsync();

        Assert.Equal("HONOR X8b", facts.Model);
        Assert.Equal("HONOR", facts.Brand);
        Assert.Equal("Honor X8b", Mappers.MapAndroidDisplayModel(facts.Brand, facts.Model));
    }

    /// <summary>
    /// None of the retail properties this reader asks for exist on this handset,
    /// so storage has to come from the size of the data partition. The phone is a
    /// 256 GB model and reports 236630016 blocks of 1 KB.
    /// </summary>
    [Fact]
    public async Task TheStorageComesFromTheDataPartitionAndComesOutRight()
    {
        var facts = await ReadFactsAsync();

        Assert.Equal("", facts.Storage);
        Assert.Equal(236630016L * 1024, facts.DataBytes);

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Equal("256GB", data.Storage);
    }

    [Fact]
    public async Task TheOsVersionIsCarriedWithoutPretendingToBeIos()
    {
        var data = AndroidDeviceReader.ToDeviceData(await ReadFactsAsync());
        Assert.Equal("Android 14", data.IosVersion);
    }

    /// <summary>
    /// Android 14 no longer lets the shell read the IMEI, so this handset answers
    /// an empty parcel. The identifier has to fall back to the serial, and the
    /// serial is what identifies the phone on the bench.
    /// </summary>
    [Fact]
    public async Task TheImeiIsNotInventedWhenTheHandsetRefusesToSayIt()
    {
        Assert.Equal("Result: Parcel(\tfffffffc ffffffff 00000000  '............')",
            Answer("service call iphonesubinfo 1"));
        Assert.Equal("", Parsers.ParseAndroidImei(Answer("service call iphonesubinfo 1")));

        var facts = await ReadFactsAsync();
        Assert.Equal("", facts.Imei1);
        Assert.Equal("", facts.Imei2);

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Equal(Serial, data.Identifier);
        Assert.Equal(Serial, data.MotherboardSerialNumber);
    }

    /// <summary>
    /// The capacity counters under /sys are not readable by the shell on this
    /// build, so there is no condition percentage to report and the status code
    /// from dumpsys is what is left. That code is a fixed list from the platform,
    /// and it travels into the CSV, the report and the label, so it has to be a
    /// stable token that the shell can put into the operator's language.
    /// </summary>
    [Fact]
    public async Task TheBatteryStatusIsATokenAndNotAWordInOneLanguage()
    {
        var facts = await ReadFactsAsync();
        Assert.Equal("", facts.ChargeFull);
        Assert.Equal("", facts.ChargeFullDesign);
        Assert.Equal(0, Parsers.ParseAndroidBatteryCondition(facts.ChargeFull, facts.ChargeFullDesign));

        string status = Parsers.ParseAndroidBatteryStatus(facts.DumpsysBattery);
        Assert.Equal("Good", status);
        Assert.DoesNotContain("Goed", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Defect", status, StringComparison.OrdinalIgnoreCase);

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Equal("Good", data.BatteryHealth);
    }

    [Fact]
    public async Task TheBatteryFiguresThatAreReadableAreRead()
    {
        var data = AndroidDeviceReader.ToDeviceData(await ReadFactsAsync());

        Assert.Equal(100, data.BatteryLevel);
        Assert.Equal(4444, data.BatteryVoltage);

        // The battery warms up between captures, so the reading is taken from the
        // capture rather than pinned to a number that only held while recording.
        Assert.Equal(Parsers.ParseAndroidBatteryTemperature(Answer("dumpsys battery")), data.BatteryTemperature);
        Assert.InRange(data.BatteryTemperature, 200, 400);
    }

    /// <summary>
    /// A stock dumpsys carries no cycle count at all. Reporting the number zero is
    /// not the same as reporting "the handset did not say", and a zero on a report
    /// reads as a battery that has never been used.
    /// </summary>
    [Fact]
    public async Task ACycleCountTheHandsetNeverReportsIsNotShownAsZeroCycles()
    {
        var facts = await ReadFactsAsync();
        Assert.Null(Parsers.ParseAndroidBatteryCycleCount(facts.DumpsysBattery));

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Null(data.BatteryCycleCount);
    }

    /// <summary>
    /// Both the battery counters and the MAC file were refused by the kernel. The
    /// reader has to be able to say that, because an operator looking at two empty
    /// rows cannot tell a phone that withholds them from a phone that has none.
    /// </summary>
    [Fact]
    public void ARefusedReadIsDistinguishableFromAnAbsentValue()
    {
        Assert.False(LiveFixture.AdbCommandRan(LiveFixture.HonorX8b, LiveFixture.CaptureNameFor("cat /sys/class/net/wlan0/address 2>/dev/null")));
        Assert.False(LiveFixture.AdbCommandRan(LiveFixture.HonorX8b, LiveFixture.CaptureNameFor("settings get secure bluetooth_address 2>/dev/null")));

        // getprop ran, so its silence really is an absent value.
        Assert.True(LiveFixture.AdbCommandRan(LiveFixture.HonorX8b, "getprop"));
    }

    /// <summary>
    /// The shell is refused the Bluetooth address outright on this build. Whatever
    /// comes back must never be stored as if it were a MAC address.
    /// </summary>
    [Fact]
    public async Task ARefusedMacAddressIsNotStoredAsAMacAddress()
    {
        var facts = await ReadFactsAsync();

        Assert.Equal("", facts.BluetoothMacAddress);
        Assert.Equal("", facts.WifiMacAddress);
        Assert.False(AndroidDeviceReader.IsMacAddress(facts.BluetoothMacAddress));

        // The name of the read is remembered so the report can say it was refused.
        Assert.Contains("bluetooth_address", facts.Withheld);
        Assert.Contains("wlan0_address", facts.Withheld);
        Assert.Contains("charge_full", facts.Withheld);
        Assert.Contains("charge_full_design", facts.Withheld);

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Contains(data.WithheldReads, r => r == "charge_full");

        // getprop itself was allowed, so nothing from it counts as withheld.
        Assert.DoesNotContain("ro.product.brand", facts.Withheld);
    }

    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("null", false)]
    [InlineData("NULL", false)]
    [InlineData("", false)]
    [InlineData("Permission denied", false)]
    [InlineData("AA:BB:CC:DD:EE", false)]
    [InlineData("AA:BB:CC:DD:EE:FF:00", false)]
    [InlineData("ZZ:BB:CC:DD:EE:FF", false)]
    public void OnlyAnAddressIsAcceptedAsAMacAddress(string candidate, bool accepted)
    {
        Assert.Equal(accepted, AndroidDeviceReader.IsMacAddress(candidate));
    }

    /// <summary>
    /// A retail colour name exists nowhere on this handset, so the colour stays
    /// unset rather than being guessed at from the model.
    /// </summary>
    [Fact]
    public async Task AHandsetThatReportsNoColourIsNotGivenOne()
    {
        var facts = await ReadFactsAsync();
        Assert.Equal("", facts.Color);

        var data = AndroidDeviceReader.ToDeviceData(facts);
        Assert.Equal("NOCOLOR", data.Color);
        Assert.Equal("NOMEMORY", data.Memory);
    }

    /// <summary>
    /// The four properties this reader uses to judge whether the phone is intact.
    /// Three of them report a phone in factory state, and the fourth is not on
    /// this build at all.
    /// </summary>
    [Fact]
    public async Task AnUntamperedPhoneIsReportedAsUntampered()
    {
        var facts = await ReadFactsAsync();

        Assert.Equal("1", facts.FlashLocked);
        Assert.Equal("locked", facts.VbmetaDeviceState);
        Assert.Equal("green", facts.VerifiedBootState);
        Assert.Equal("", facts.WarrantyBit);
        Assert.EndsWith(":user/release-keys", facts.BuildFingerprint, StringComparison.Ordinal);

        var checks = AndroidIntegrityChecks.Build(facts);
        Assert.Equal(ComponentStatusType.Passed, StatusOf(checks, "Bootloader"));
        Assert.Equal(ComponentStatusType.Passed, StatusOf(checks, "Vbmeta"));
        Assert.Equal(ComponentStatusType.Passed, StatusOf(checks, "Systeemimage"));
        Assert.Equal(ComponentStatusType.Unknown, StatusOf(checks, "Warrantybit"));
    }

    /// <summary>
    /// A phone whose bootloader is open is a phone with non original parts, and
    /// that has to reach the operator as a finding. Today these verdicts only end
    /// up in the export, never in the diagnostics or the defect list.
    /// </summary>
    [Fact]
    public async Task ATamperedPhoneBecomesAFinding()
    {
        var tampered = (await ReadFactsAsync()) with
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

    /// <summary>
    /// The warranty bit is a single hexadecimal zero on a factory phone and
    /// anything else means parts were replaced. Samsung writes it as "0x0", so
    /// matching one spelling exactly calls a clean phone a repaired one.
    /// </summary>
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

    /// <summary>
    /// <c>ro.carrier</c> names the network the SIM in the phone belongs to, not
    /// whether the handset is locked to it. On Android 10 and later it is empty
    /// altogether, so a phone with a carrier SIM in it used to be reported locked.
    /// </summary>
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
        Assert.Null(modern.IsCarrierLocked);

        // With no card and no carrier the state is all the handset will say.
        var bare = await DetectCarrierAsync(gsmSimState: "ABSENT,ABSENT", carrier: "unknown");
        Assert.Equal("ABSENT,ABSENT", bare.SIMState);
        Assert.Null(bare.CarrierName);
        Assert.Null(bare.IsCarrierLocked);
    }

    [Fact]
    public async Task FrpIsReadFromTheHandsetInsteadOfNeverBeingAsked()
    {
        // This build answers "null": the key is not readable, which is not a lock.
        Assert.Equal("null", Answer("settings get secure secure_frp_mode"));

        var status = await FrpLockService.DetectAsync(Serial,
            command => Task.FromResult(Answer(command)));
        Assert.Equal(FrpLockService.FrpLockStatus.Unknown, status);

        var unlocked = await FrpLockService.DetectAsync(Serial,
            command => Task.FromResult("0"));
        Assert.Equal(FrpLockService.FrpLockStatus.Unlocked, unlocked);

        var locked = await FrpLockService.DetectAsync(Serial,
            command => Task.FromResult("1"));
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