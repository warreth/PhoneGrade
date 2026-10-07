using Xunit;
using PhoneGrade.Core;
using PhoneGrade.Core.Diagnostics;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

// ============ Logic tests: parsers, mappers, panic rules, labels, settings ============

public class ParsersTests
{
    [Fact]
    public void BatteryHealth_ComputesFromAppleRaw()
    {
        string plist = "<key>DesignCapacity</key><integer>3227</integer><key>AppleRawMaxCapacity</key><integer>2900</integer>";
        Assert.Equal("90", Parsers.ParseBatteryHealth(plist));
    }

    [Fact]
    public void BatteryHealth_CapsAt100()
    {
        string plist = "<key>DesignCapacity</key><integer>100</integer><key>AppleRawMaxCapacity</key><integer>150</integer>";
        Assert.Equal("100", Parsers.ParseBatteryHealth(plist));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<key>DesignCapacity</key><integer>0</integer>")]
    [InlineData("<key>DesignCapacity</key><integer>3227</integer>")] // no current value
    public void BatteryHealth_InvalidInput_ReturnsNOBATT(string plist)
        => Assert.Equal("NOBATT", Parsers.ParseBatteryHealth(plist));

    [Fact]
    public void BatteryHealth_FallsBackToMaxCapacity()
    {
        string plist = "<key>DesignCapacity</key><integer>200</integer><key>MaxCapacity</key><integer>100</integer>";
        Assert.Equal("50", Parsers.ParseBatteryHealth(plist));
    }

    [Fact]
    public void BatteryHealth_FallsBackToNominalCharge()
    {
        string plist = "<key>DesignCapacity</key><integer>200</integer><key>NominalChargeCapacity</key><integer>180</integer>";
        Assert.Equal("90", Parsers.ParseBatteryHealth(plist));
    }

    [Fact]
    public void AndroidBatteryCondition_ComputesFromCapacityCounters()
    {
        // 4400 of 5000 microAh left, the shape a Pixel reports
        Assert.Equal(88, Parsers.ParseAndroidBatteryCondition("4400000", "5000000"));
    }

    [Fact]
    public void AndroidBatteryCondition_IsNotTheChargeLevel()
    {
        // The bug this guards: a phone sitting at 20% charge was reported as
        // having 20% condition, which reads as a nearly dead battery.
        string dumpsys = "  level: 20\n  health: 2\n";
        Assert.Equal(20, Parsers.ParseAndroidChargeLevel(dumpsys));
        Assert.Equal(88, Parsers.ParseAndroidBatteryCondition("4400000", "5000000"));
    }

    /// <summary>Verbatim `dumpsys battery` from a Pixel 8 Pro, note "Capacity level: 2" at the end.</summary>
    private const string Pixel8ProDumpsys = """
        Current Battery Service state:
          AC powered: false
          USB powered: true
          Wireless powered: false
          Dock powered: false
          Max charging current: 500000
         Time when the latest updated value of the Max charging current was sent via battery changed broadcast: +14h58m53s789ms
          Max charging voltage: 5000000
          Charge counter: 1082000
          status: 2
          health: 2
          present: true
          level: 22
          scale: 100
          voltage: 3764
         Time when the latest updated value of the voltage was sent via battery changed broadcast: +14h59m21s407ms
         The last voltage value sent via the battery changed broadcast: 3775
          temperature: 280
          technology: Li-ion
          Charging state: 4
          Charging policy: 2
          Capacity level: 2
        """;

    [Fact]
    public void AndroidBattery_RealPixel8ProOutput()
    {
        // The counters this handset actually reports.
        Assert.Equal(92, Parsers.ParseAndroidBatteryCondition("4618000", "5022000"));
        Assert.Equal(22, Parsers.ParseAndroidChargeLevel(Pixel8ProDumpsys));
        Assert.Equal("Good", Parsers.ParseAndroidBatteryStatus(Pixel8ProDumpsys));
    }

    [Fact]
    public void AndroidChargeLevel_IgnoresTheCapacityLevelLine()
    {
        // "Capacity level: 2" sits after "level: 22" here. A pattern that is not
        // anchored to the line start reads whichever comes first in the output,
        // which on some builds is the capacity bucket rather than the charge.
        string reordered = "  Capacity level: 2\n  level: 22\n  health: 2\n";
        Assert.Equal(22, Parsers.ParseAndroidChargeLevel(reordered));
    }

    /// <summary>
    /// Verbatim `getprop` lines a Pixel 8 Pro reports, cut down to the ones the
    /// collector reads. The brackets and the spacing are the device's own.
    /// </summary>
    private const string Pixel8ProGetprop = """
        [ro.boot.hardware]: [husky]
        [ro.boot.hardware.color]: [WHT]
        [ro.boot.hardware.coo]: [CN]
        [ro.boot.hardware.cpu.pagesize]: [4096]
        [ro.boot.hardware.ddr]: [12GiB,Micron,LPDDR5,ff07]
        [ro.boot.hardware.devcfg]: [G950-10158-02]
        [ro.boot.hardware.pcbcfg]: [G650-09345-06]
        [ro.boot.hardware.platform]: [zuma]
        [ro.boot.hardware.sku]: [GC3VE]
        [ro.boot.hardware.ufs]: [128GB,Samsung]
        [ro.boot.product.hardware.sku]: [GC3VE]
        [ro.boot.flash.locked]: [1]
        [ro.boot.vbmeta.device_state]: [locked]
        [ro.boot.verifiedbootstate]: [yellow]
        [ro.build.fingerprint]: [google/husky/husky:17/CP3A.260905.009/2026092501:user/release-keys]
        [ro.product.brand]: [google]
        [ro.product.model]: [Pixel 8 Pro]
        [ro.build.version.release]: [17]
        [ro.serialno]: [38091FDJG00EMF]
        [ro.boot.warranty_bit]: []
        [persist.sys.sf.color_saturation]: [1.0]
        [ro.surface_flinger.has_wide_color_display]: [true]
        """;

    /// <summary>Simulated `service call iphonesubinfo 1` output for IMEI 356938035643809.</summary>
    private const string Pixel8ProImei1 = """
        Result: Parcel(
          0x00000000: 00000000 0000000f 00330035 00360039 00330038 00300033 00350036 00340033
          0x00000020: 00380030 00390000 00000000
        )
        """;

    /// <summary>Simulated `service call iphonesubinfo 2` output for IMEI2 (eSIM) 356938035643810.</summary>
    private const string Pixel8ProImei2 = """
        Result: Parcel(
          0x00000000: 00000000 0000000f 00330035 00360039 00330038 00300033 00350036 00340033
          0x00000020: 00380031 00300000 00000000
        )
        """;

    /// <summary>Verbatim `df -k /data` from the same handset, per-user mount and all.</summary>
    private const string Pixel8ProDf = """
        Filesystem       1K-blocks     Used Available Use% Mounted on
        /dev/block/dm-30 114982996 94844540  20007384  83% /data/user/0
        """;

    /// <summary>
    /// Verbatim `getprop` lines from the Honor X8b on the bench, cut down to the
    /// ones identity depends on. The handset reports three spellings of its model
    /// and only ro.config.marketing_name carries the name it is sold under.
    /// </summary>
    private const string HonorX8bGetprop = """
        [ro.config.marketing_name]: [HONOR X8b]
        [ro.product.brand]: [HONOR]
        [ro.product.model]: [LLY-LX1]
        [ro.product.product.brand]: [Honor]
        [ro.product.product.model]: [magic]
        [ro.product.vendor.model]: [Bengal for arm64]
        [ro.build.fingerprint]: [HONOR/LLY-LX1EEA/HNLLY-Q:14/HONORLLY-L31/8.0.0.366C431E205R2P3:user/release-keys]
        [ro.build.version.release]: [14]
        [ro.serialno]: [AAUF6R3C19003414]
        [ro.boot.flash.locked]: [1]
        [ro.boot.verifiedbootstate]: [green]
        """;

    [Fact]
    public void GetpropOutput_ReadsTheBracketedDump()
    {
        var props = AndroidDeviceReader.ParseGetpropOutput(Pixel8ProGetprop);

        Assert.Equal("WHT", props["ro.boot.hardware.color"]);
        Assert.Equal("128GB,Samsung", props["ro.boot.hardware.ufs"]);
        Assert.Equal("12GiB,Micron,LPDDR5,ff07", props["ro.boot.hardware.ddr"]);
        Assert.Equal("Pixel 8 Pro", props["ro.product.model"]);
        Assert.Equal("google", props["ro.product.brand"]);
        Assert.Equal("1", props["ro.boot.flash.locked"]);
        Assert.Equal("locked", props["ro.boot.vbmeta.device_state"]);
        Assert.Equal("yellow", props["ro.boot.verifiedbootstate"]);
        Assert.Equal("17", props["ro.build.version.release"]);
        Assert.Equal("38091FDJG00EMF", props["ro.serialno"]);

        // A property the device does not set reads as an empty value, not as
        // missing: the bracket pair is still there, the content is not.
        Assert.Equal("", props["ro.boot.warranty_bit"]);
    }

    [Fact]
    public void GetpropOutput_IgnoresLinesThatAreNotProperties()
    {
        // Boot banners and adb warnings arrive in the same stream. A line without
        // the bracket pair must not become a property with a garbage key.
        var props = AndroidDeviceReader.ParseGetpropOutput(
            "adb: warning: device offline\n[ro.product.model]: [Pixel 8 Pro]\nrandom text\n");

        Assert.Single(props);
        Assert.Equal("Pixel 8 Pro", props["ro.product.model"]);
    }

    [Fact]
    public void GetpropOutput_EmptyInputGivesAnEmptyLookup()
    {
        Assert.Empty(AndroidDeviceReader.ParseGetpropOutput(null));
        Assert.Empty(AndroidDeviceReader.ParseGetpropOutput(""));
        Assert.Empty(AndroidDeviceReader.ParseGetpropOutput("   \n  \n"));
    }

    [Fact]
    public void AndroidDataBytes_UsesTheDataMountNotTheHeader()
    {
        // 114982996 blocks of 1K is what this handset reports for /data.
        Assert.Equal(114982996L * 1024, Parsers.ParseAndroidDataBytes(Pixel8ProDf));
    }

    [Fact]
    public void AndroidDataBytes_SkipsOtherMountsAndTheHeader()
    {
        string table = """
            Filesystem     1K-blocks     Used Available Use% Mounted on
            /dev/block/dm-15 2097152 1048576  1048576  50% /system
            /dev/block/dm-30 114982996 94844540  20007384  83% /data
            """;

        // The /system row is far smaller, so picking the first row instead of the
        // /data one would bucket a 128GB phone as 32GB.
        Assert.Equal(114982996L * 1024, Parsers.ParseAndroidDataBytes(table));
    }

    [Fact]
    public void AndroidDataBytes_ReadsThePlainDataMountToo()
    {
        // The same figure on a device without per-user encryption mounts at /data
        // itself, and both spellings have to give the same capacity.
        string plain = """
            Filesystem     1K-blocks     Used Available Use% Mounted on
            /dev/block/dm-30 114982996 94844540  20007384  83% /data
            """;
        Assert.Equal(114982996L * 1024, Parsers.ParseAndroidDataBytes(plain));
    }

    [Fact]
    public void AndroidDataBytes_UnreadableOutputIsZero()
    {
        Assert.Equal(0, Parsers.ParseAndroidDataBytes(null));
        Assert.Equal(0, Parsers.ParseAndroidDataBytes(""));
        Assert.Equal(0, Parsers.ParseAndroidDataBytes("df: /data: Permission denied"));
        Assert.Equal(0, Parsers.ParseAndroidDataBytes(
            "Filesystem     1K-blocks     Used Available Use% Mounted on\n"));
    }

    [Fact]
    public async Task AndroidCollector_ReadsARealPixel8Pro()
    {
        // Drives the real collector against the output this handset actually
        // produces, so the mapping is proved end to end and not just per helper.
        var reader = new AndroidDeviceReader("38091FDJG00EMF", command => Task.FromResult((ShellFor(command), false)));
        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal("Google Pixel 8 Pro", data.Model);
        Assert.Equal("Android (Google Pixel 8 Pro)", data.ProductType);
        Assert.Equal("356938035643809", data.Identifier); // IMEI1 is now the primary identifier
        Assert.Equal("356938035643810", data.Imei2);

        // The three values that used to come out as placeholders.
        Assert.Equal(ColorKeys.White, data.Color);
        Assert.Equal("128GB", data.Storage);
        Assert.Equal("12GB", data.Memory);

        // Battery: the condition and the charge level are different numbers.
        Assert.Equal("92%", data.BatteryHealth);
        Assert.Equal(22, data.BatteryLevel);

        // Extended battery metrics from dumpsys
        Assert.Null(data.BatteryCycleCount);   // this handset's dumpsys carries no cycle count

        // Network MAC addresses (not in sample output, should be empty)
        Assert.Equal("", data.WifiMacAddress);
        Assert.Equal("", data.BluetoothMacAddress);

        // Nothing was refused here, so nothing is reported as withheld.
        Assert.Empty(data.WithheldReads);
    }

    /// <summary>
    /// A read the handset refuses is named, so a report can tell a phone that
    /// withholds a value apart from a phone that has none. Without this the two
    /// are the same empty field.
    /// </summary>
    [Fact]
    public async Task AndroidCollector_NamesTheReadsTheHandsetRefused()
    {
        var reader = new AndroidDeviceReader("TEST0000000001A", command =>
        {
            bool refused = command.Contains("charge_full", StringComparison.Ordinal)
                        || command.Contains("wlan0", StringComparison.Ordinal);
            return Task.FromResult((refused ? "" : ShellFor(command), refused));
        });

        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Contains("charge_full", data.WithheldReads);
        Assert.Contains("charge_full_design", data.WithheldReads);
        Assert.Contains("wlan0_address", data.WithheldReads);
        Assert.DoesNotContain("bluetooth_address", data.WithheldReads);
    }

    /// <summary>
    /// A refused read that leaks its refusal onto stdout must not end up in the
    /// field. Some builds print the error there rather than on stderr.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("Permission denied")]
    [InlineData("cat: /sys/class/net/wlan0/address: Permission denied")]
    public void ARefusalIsNeverAcceptedAsAMacAddress(string whatTheShellPrinted)
        => Assert.False(AndroidDeviceReader.IsMacAddress(whatTheShellPrinted));

    /// <summary>Replays the captured output for whichever command the reader asks for.</summary>
    private static string ShellFor(string command) => command switch
    {
        "getprop" => Pixel8ProGetprop,
        "df -k /data" => Pixel8ProDf,
        "dumpsys battery" => Pixel8ProDumpsys,
        "cat /sys/class/power_supply/battery/charge_full" => "4618000\n",
        "cat /sys/class/power_supply/battery/charge_full_design" => "5022000\n",
        "service call iphonesubinfo 1" => Pixel8ProImei1,
        "service call iphonesubinfo 2" => Pixel8ProImei2,
        _ => "",
    };

    [Fact]
    public async Task AndroidCollector_SurvivesADesktopThatAnswersNothing()
    {
        // A device that only answers getprop, which is the realistic minimum.
        var reader = new AndroidDeviceReader("emulator-5554", command => Task.FromResult(
            command == "getprop" ? "[ro.product.brand]: [google]\n[ro.product.model]: [Pixel 2]\n" : ""));
        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal("Google Pixel 2", data.Model);
        Assert.Equal("NOCOLOR", data.Color);
        Assert.Equal("NOSTORAGE", data.Storage);
        Assert.Equal("NOMEMORY", data.Memory);
        Assert.Equal("NOBATT", data.BatteryHealth);
    }

    [Fact]
    public async Task AndroidCollector_UsesTheAdbSerialWhenThePropertyIsEmpty()
    {
        // Some builds leave ro.serialno empty. The adb serial identifies the device
        // in the list and on the label, so it has to win over a blank property.
        var reader = new AndroidDeviceReader("R5CT30XXXXX", command => Task.FromResult(
            command == "getprop" ? "[ro.product.model]: [Galaxy S23]\n[ro.product.brand]: [samsung]\n[ro.serialno]: []\n" : ""));
        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal("R5CT30XXXXX", data.Identifier);
        Assert.Equal("R5CT30XXXXX", data.MotherboardSerialNumber);
    }

    [Fact]
    public async Task AndroidCollector_KeepsGoingWhenOneCommandFails()
    {
        // A read that throws must not cost the other values: a device that refuses
        // the battery sysfs nodes should still report its colour and storage.
        var reader = new AndroidDeviceReader("38091FDJG00EMF", command => command switch
        {
            "getprop" => Task.FromResult(Pixel8ProGetprop),
            "df -k /data" => Task.FromResult(Pixel8ProDf),
            "dumpsys battery" => throw new InvalidOperationException("closed"),
            _ => Task.FromResult(""),
        });

        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal(ColorKeys.White, data.Color);
        Assert.Equal("128GB", data.Storage);
        Assert.Equal("NOBATT", data.BatteryHealth);
    }

    [Fact]
    public async Task AndroidCollector_ShowsTheNameAHonorX8bIsSoldUnder()
    {
        // The factory code identifies the handset, but the bench knows the phone
        // as the name in ro.config.marketing_name. Driven end to end, so the
        // reader and the display mapper are proved together and not per helper.
        var reader = new AndroidDeviceReader("AAUF6R3C19003414", command =>
            Task.FromResult(command == "getprop" ? HonorX8bGetprop : ""));
        var data = AndroidDeviceReader.ToDeviceData(await reader.ReadAsync());

        Assert.Equal("Honor X8b", data.Model);
        Assert.Equal("Android (Honor X8b)", data.ProductType);
        Assert.Equal("AAUF6R3C19003414", data.Identifier);
        Assert.Equal("Android 14", data.IosVersion);
    }

    [Fact]
    public void PickModel_TakesTheNameThePhoneIsSoldUnder()
    {
        var props = AndroidDeviceReader.ParseGetpropOutput(HonorX8bGetprop);

        Assert.Equal("HONOR X8b", AndroidDeviceReader.PickModel(props));
    }

    [Fact]
    public void PickModel_TakesTheMarketNameBrandsReportElsewhere()
    {
        // Xiaomi and its brands do not set ro.config.marketing_name; they carry
        // the shop name on their own key next to the factory code.
        var props = AndroidDeviceReader.ParseGetpropOutput("""
            [ro.product.marketname]: [Redmi Note 12 Pro]
            [ro.product.model]: [2209116AG]
            """);

        Assert.Equal("Redmi Note 12 Pro", AndroidDeviceReader.PickModel(props));
    }

    [Fact]
    public void PickModel_SkipsAMarketingNameThatIsEmpty()
    {
        var props = AndroidDeviceReader.ParseGetpropOutput("""
            [ro.config.marketing_name]: []
            [ro.product.model]: [CPH2449]
            """);

        Assert.Equal("CPH2449", AndroidDeviceReader.PickModel(props));
    }

    [Fact]
    public void PickModel_KeepsTheFactoryCodeWhenNothingElseIsReported()
    {
        var props = AndroidDeviceReader.ParseGetpropOutput(Pixel8ProGetprop);

        Assert.Equal("Pixel 8 Pro", AndroidDeviceReader.PickModel(props));
    }

    [Fact]
    public void PickModel_DoesNotReadThePartitionSpellingsOfTheModel()
    {
        // The same handset answers "magic" and "Bengal for arm64" as its model on
        // the other partitions. Those are build names, not names a bench would use.
        var props = AndroidDeviceReader.ParseGetpropOutput("""
            [ro.product.product.model]: [magic]
            [ro.product.vendor.model]: [Bengal for arm64]
            [ro.product.model]: [LLY-LX1]
            """);

        Assert.Equal("LLY-LX1", AndroidDeviceReader.PickModel(props));
    }

    [Fact]
    public void PickModel_AnEmptyDumpGivesAnEmptyName()
        => Assert.Equal("", AndroidDeviceReader.PickModel(AndroidDeviceReader.ParseGetpropOutput("")));

    [Fact]
    public async Task GetPropsAsync_ParsesTheWholeDumpInOneRead()
    {
        // The device list reads brand and name from one dump instead of one adb
        // process per value, so it must land in the same lookup the parser builds.
        var reader = new AndroidDeviceReader("AAUF6R3C19003414", command =>
            Task.FromResult(command == "getprop" ? HonorX8bGetprop : "unrelated\n"));

        var props = await reader.GetPropsAsync();

        Assert.Equal("HONOR X8b", props["ro.config.marketing_name"]);
        Assert.Equal("LLY-LX1", props["ro.product.model"]);
        Assert.Equal("HONOR", props["ro.product.brand"]);
    }

    [Theory]
    [InlineData("4400000", "5000000", 88)]
    [InlineData("5000000", "5000000", 100)]
    [InlineData("5000000", "4400000", 100)] // replaced battery, clamped
    [InlineData("0", "5000000", 0)] // unreadable
    [InlineData("4400000", "0", 0)]
    [InlineData("", "", 0)]
    [InlineData("cat: no such file", "5000000", 0)]
    [InlineData("4400000", "cat: no such file", 0)]
    [InlineData(null, null, 0)]
    public void AndroidBatteryCondition_HandlesUnreadableCounters(string? full, string? design, int expected)
        => Assert.Equal(expected, Parsers.ParseAndroidBatteryCondition(full, design));

    [Fact]
    public void AndroidChargeLevel_ReadsLevel()
    {
        string dumpsys = "  AC powered: false\n  USB powered: true\n  level: 20\n  scale: 100\n";
        Assert.Equal(20, Parsers.ParseAndroidChargeLevel(dumpsys));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("  scale: 100\n", 0)]
    [InlineData("  level: 150\n", 100)] // clamped
    [InlineData("  level: -5\n", 0)]
    public void AndroidChargeLevel_UnreadableGivesZero(string dumpsys, int expected)
        => Assert.Equal(expected, Parsers.ParseAndroidChargeLevel(dumpsys));

    /// <summary>
    /// The status codes are named by the platform, and the names are data: they
    /// travel into the CSV, the report and the label. They are therefore tokens
    /// rather than words in one language, and the battery converter is what turns
    /// them into wording for the operator.
    /// </summary>
    [Theory]
    [InlineData(1, "Unknown")]
    [InlineData(2, "Good")]
    [InlineData(3, "Overheated")]
    [InlineData(4, "Defective")]
    [InlineData(5, "Overvoltage")]
    [InlineData(6, "StorageFault")]
    [InlineData(7, "TooCold")]
    public void AndroidBatteryStatus_MapsTheStatusCodeToAToken(int code, string expected)
    {
        string dumpsys = $"  status: 2\n  health: {code}\n  level: 55\n";
        Assert.Equal(expected, Parsers.ParseAndroidBatteryStatus(dumpsys));
    }

    /// <summary>
    /// Nothing readable at all stays unreadable, so the warning that the battery
    /// figures cannot be read still fires.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("no battery service")]
    public void AndroidBatteryStatus_UnreadableOutputIsNOBatt(string dumpsys)
        => Assert.Equal("NOBATT", Parsers.ParseAndroidBatteryStatus(dumpsys));

    /// <summary>
    /// A status code this build cannot name is carried as unknown rather than as
    /// unreadable. Newer platforms added codes 8 and up; calling a healthy battery
    /// unreadable would raise a false battery warning on a sound phone.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(99)]
    [InlineData(255)]
    public void AndroidBatteryStatus_CodeThisBuildCannotNameIsUnknown(int code)
        => Assert.Equal("Unknown", Parsers.ParseAndroidBatteryStatus($"  health: {code}\n"));

    [Fact]
    public void Identifier_PrefersValidImei()
        => Assert.Equal("356938035643809", Parsers.ParseIdentifier("356938035643809", "F2LX9"));

    [Theory]
    [InlineData("NO OUTPUT")]
    [InlineData("")]
    [InlineData("ERROR: no device")]
    [InlineData("123")] // too short
    public void Identifier_NoImei_FallsBackToSerial(string imei)
        => Assert.Equal("F2LX9ABC", Parsers.ParseIdentifier(imei, "F2LX9ABC"));

    [Fact]
    public void Identifier_BothEmpty_ReturnsNOID()
        => Assert.Equal("NOID", Parsers.ParseIdentifier("", ""));

    [Fact]
    public void KeyValue_ExtractsValue()
        => Assert.Equal("255501272064", Parsers.KeyValue("TotalDiskCapacity: 255501272064\nOther: x", "TotalDiskCapacity"));

    [Fact]
    public void KeyValue_MissingKey_ReturnsNull()
        => Assert.Null(Parsers.KeyValue("nothing here", "TotalDiskCapacity"));

    [Fact]
    public void PlistString_ExtractsStringValue()
    {
        string plist = "<key>Serial</key><string>F1734892AA</string><key>Other</key><string>XYZ</string>";
        Assert.Equal("F1734892AA", Parsers.PlistString(plist, "Serial"));
    }

    [Fact]
    public void PlistData_ExtractsDataContent()
    {
        string plist = "<key>NvramData</key><data>MDEyMzQ1Njc4OQ==</data>";
        Assert.Equal("MDEyMzQ1Njc4OQ==", Parsers.PlistData(plist, "NvramData"));
    }

    [Fact]
    public void ParseKeyValues_ParsesBothColonAndEquals()
    {
        string output = "OriginalBatterySerialNumber: F8Y1234567\nDisplaySerialNumber = DTM98765432\nInvalidLine";
        var dict = Parsers.ParseKeyValues(output);
        Assert.Equal("F8Y1234567", dict["OriginalBatterySerialNumber"]);
        Assert.Equal("DTM98765432", dict["DisplaySerialNumber"]);
        Assert.False(dict.ContainsKey("InvalidLine"));
    }

    [Fact]
    public void CleanSerial_DecodesHexAscii()
    {
        // "F8Y51234ABCD" in hex is 463859353132333441424344
        string hex = "463859353132333441424344";
        Assert.Equal("F8Y51234ABCD", Parsers.CleanSerial(hex));
    }

    [Fact]
    public void CleanSerial_DecodesBase64()
    {
        // "F8Y51234ABCD" in base64 is RjhZNTEyMzRBQkNE
        string b64 = "RjhZNTEyMzRBQkNE";
        Assert.Equal("F8Y51234ABCD", Parsers.CleanSerial(b64));
    }

    [Fact]
    public void CleanSerial_PreservesPlainSerial()
    {
        Assert.Equal("F8Y51234ABCD", Parsers.CleanSerial("  F8Y51234ABCD  "));
    }

    [Theory]
    [InlineData("F8Y1234", "F8Y1234", ComponentStatusType.Match)]
    [InlineData("f8y1234", "F8Y1234", ComponentStatusType.Match)] // Case-insensitive
    [InlineData("F8Y1234", "F8Y9999", ComponentStatusType.Mismatch)]
    [InlineData("[MASKED]", "F8Y1234", ComponentStatusType.Untrusted)]
    [InlineData("F8Y1234", "UNAVAILABLE", ComponentStatusType.Untrusted)]
    [InlineData("MASKED", "F8Y1234", ComponentStatusType.Untrusted)]
    [InlineData("F8Y1234", "PROTECTED", ComponentStatusType.Untrusted)]
    [InlineData("NOT_PAIRED", "F8Y1234", ComponentStatusType.Untrusted)]
    [InlineData("", "F8Y1234", ComponentStatusType.Unknown)]
    [InlineData("F8Y1234", null, ComponentStatusType.Unknown)]
    [InlineData(null, null, ComponentStatusType.Unknown)]
    public void VerifyComponent_EvaluatesCorrectly(string? live, string? factory, ComponentStatusType expected)
    {
        Assert.Equal(expected, Parsers.VerifyComponent(live, factory));
    }

    [Fact]
    public void BatteryMetrics_ExtendedParsingFromPlist()
    {
        string plist = """
            <dict>
                <key>CycleCount</key><integer>321</integer>
                <key>DesignCapacity</key><integer>3227</integer>
                <key>AppleRawMaxCapacity</key><integer>2980</integer>
                <key>BatterySerialNumber</key><string>F8Y8324ABC1</string>
            </dict>
            """;

        Assert.Equal(321, Parsers.PlistInt(plist, "CycleCount"));
        Assert.Equal(3227, Parsers.PlistInt(plist, "DesignCapacity"));
        Assert.Equal(2980, Parsers.PlistInt(plist, "AppleRawMaxCapacity"));
        Assert.Equal("F8Y8324ABC1", Parsers.PlistString(plist, "BatterySerialNumber"));
    }

    [Fact]
    public void ParseAndroidImei_ExtractsFromParcelHexDump()
    {
        // Use the constant with correct hex dump for IMEI 356938035643809
        string imei = Parsers.ParseAndroidImei(Pixel8ProImei1);
        Assert.Equal("356938035643809", imei);
    }

    [Fact]
    public void ParseAndroidImei_HandlesSecondSim()
    {
        // Use the constant with correct hex dump for IMEI2 356938035643810
        string imei = Parsers.ParseAndroidImei(Pixel8ProImei2);
        Assert.Equal("356938035643810", imei);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Result: Parcel()")]
    [InlineData("ERROR: service not found")]
    [InlineData("No such service")]
    public void ParseAndroidImei_ReturnsEmptyForInvalidInput(string input)
        => Assert.Equal("", Parsers.ParseAndroidImei(input));

    [Theory]
    [InlineData("  cycle count: 450\n", 450)]
    [InlineData("  charge_cycle: 123\n", 123)]
    [InlineData("  battery_cycle: 999\n", 999)]
    [InlineData("  cycle: 7\n", 7)]
    public void ParseAndroidBatteryCycleCount_ReadsVariousFieldNames(string dumpsys, int expected)
        => Assert.Equal(expected, Parsers.ParseAndroidBatteryCycleCount(dumpsys));

    /// <summary>
    /// A stock dumpsys carries no cycle count. Zero would say the battery has
    /// never been used, which is a claim about a number the handset never sent,
    /// so nothing at all is the honest answer.
    /// </summary>
    [Fact]
    public void ParseAndroidBatteryCycleCount_IsNullWhenTheHandsetReportsNone()
    {
        string dumpsys = "  level: 50\n  health: 2\n  voltage: 3800\n";
        Assert.Null(Parsers.ParseAndroidBatteryCycleCount(dumpsys));
        Assert.Null(Parsers.ParseAndroidBatteryCycleCount(""));
        Assert.Null(Parsers.ParseAndroidBatteryCycleCount(null));
    }

    /// <summary>
    /// A zero the handset really did report stays a zero. Only the absence of the
    /// field is turned into nothing.
    /// </summary>
    [Fact]
    public void ParseAndroidBatteryCycleCount_KeepsARealZeroAsZero()
    {
        Assert.Equal(0, Parsers.ParseAndroidBatteryCycleCount("  cycle count: 0\n"));
    }
}

public class MappersTests
{
    [Theory]
    [InlineData("iPhone14,2", "13Pro")]
    [InlineData("iPhone16,2", "15ProMax")]
    [InlineData("iPad14,3", "iPadAir11")]
    public void MapModel_KnownTypes(string raw, string expected)
        => Assert.Equal(expected, Mappers.MapModel(raw));

    [Fact]
    public void MapModel_UnknownFallsBackToRaw()
        => Assert.Equal("iPhone99,9", Mappers.MapModel("iPhone99,9"));

    [Fact]
    public void MapModel_EmptyReturnsOnbekend()
        => Assert.Equal("Onbekend", Mappers.MapModel("  "));

    [Fact]
    public void DisplayModel_AndroidKeepsItsOwnName()
    {
        // A Pixel was shown as "iPhone Google Pixel 8 Pro" because the formatter
        // prefixed anything that was not already an Apple name.
        Assert.Equal("Google Pixel 8 Pro",
            Mappers.FormatDisplayModel("Google Pixel 8 Pro", "Android (Google Pixel 8 Pro)"));
    }

    [Fact]
    public void DisplayModel_AndroidWithoutBrandKeepsItsOwnName()
        => Assert.Equal("SM-G991B", Mappers.FormatDisplayModel("SM-G991B", "Android (SM-G991B)"));

    [Fact]
    public void DisplayModel_AppleStillGetsThePrefix()
    {
        Assert.Equal("iPhone 8", Mappers.FormatDisplayModel("8", "iPhone10,1"));
        Assert.Equal("iPad Air 11", Mappers.FormatDisplayModel("Air 11", "iPad14,3"));
    }

    [Fact]
    public void DisplayModel_AppleNameIsNotDoublePrefixed()
        => Assert.Equal("iPhone 13 Pro", Mappers.FormatDisplayModel("iPhone 13 Pro", "iPhone14,5"));

    [Theory]
    [InlineData("google", "Pixel 8 Pro", "Google Pixel 8 Pro")]
    [InlineData("samsung", "SM-G991B", "Samsung SM-G991B")]
    [InlineData("Google", "Pixel 8 Pro", "Google Pixel 8 Pro")]
    [InlineData("oneplus", "CPH2449", "Oneplus CPH2449")] // plain title case, no table entry
    [InlineData("lg", "LM-G850", "LG LM-G850")] // acronym
    [InlineData("zte", "ZTE Axon", "ZTE Axon")] // brand already repeated, acronym kept
    [InlineData("nokia", "Nokia 8.1", "Nokia 8.1")] // already repeats the brand
    [InlineData("motorola", "motorola one vision", "Motorola one vision")]
    [InlineData("motorola", "MOTOROLA one", "Motorola one")]
    [InlineData("nokia", "nokia", "Nokia")] // the model is nothing but the brand
    [InlineData("motorola mobility", "edge 30", "Motorola Mobility edge 30")]
    [InlineData("motorola", "moto g84", "Motorola moto g84")]
    [InlineData("HONOR", "HONOR X8b", "Honor X8b")] // the marketing name repeats the brand
    [InlineData("", "Pixel 8 Pro", "Pixel 8 Pro")]
    [InlineData("google", "", "Google")]
    [InlineData("google", "   ", "Google")]
    [InlineData("  ", "  ", "")]
    public void AndroidDisplayModel_WritesTheBrandProperly(string? brand, string? model, string expected)
        => Assert.Equal(expected, Mappers.MapAndroidDisplayModel(brand, model));

    [Theory]
    [InlineData("google", "Google")]
    [InlineData("samsung", "Samsung")]
    [InlineData("  xiaomi  ", "Xiaomi")]
    [InlineData("motorola mobility", "Motorola Mobility")]
    [InlineData("LG", "LG")]
    [InlineData("zte", "ZTE")]
    [InlineData("hTC", "HTC")] // mixed input, still an acronym
    [InlineData("", "")]
    [InlineData(null, "")]
    public void AndroidBrand_TitleCasesWithAcronymsApart(string? brand, string expected)
        => Assert.Equal(expected, Mappers.MapAndroidBrand(brand));

    [Fact]
    public void AndroidBrand_DoesNotNeedATableEntryPerBrand()
    {
        // A brand nobody has ever heard of still comes out capitalised, which is
        // what a lookup table could not guarantee.
        Assert.Equal("Fairphone", Mappers.MapAndroidBrand("fairphone"));
        Assert.Equal("Wiko", Mappers.MapAndroidBrand("wiko"));
    }

    [Theory]
    [InlineData("WHT", ColorKeys.White)]        // the code a Pixel 8 Pro actually reports
    [InlineData("BLK", ColorKeys.Black)]
    [InlineData("wht", ColorKeys.White)]        // case does not matter
    [InlineData("OBS", ColorKeys.Obsidian)]
    [InlineData("MNT", ColorKeys.Mint)]
    [InlineData("HZL", ColorKeys.Hazel)]
    public void AndroidColor_MapsTheVendorCode(string raw, string expected)
        => Assert.Equal(expected, Mappers.MapAndroidColor(raw));

    [Fact]
    public void AndroidColor_AWordNeedsNoCodeEntry()
    {
        // Some vendors publish the marketing name instead of a code, and those go
        // through the normal colour table first.
        Assert.Equal(ColorKeys.Obsidian, Mappers.MapAndroidColor("obsidian"));
        Assert.Equal(ColorKeys.Black, Mappers.MapAndroidColor("black"));
        Assert.Equal(ColorKeys.Pink, Mappers.MapAndroidColor("pink"));
    }

    [Fact]
    public void AndroidColor_AnUnknownCodeStaysVisible()
    {
        // Guessing here would put a wrong colour on a graded device's label, so
        // an unrecognised code is shown as it is instead of being expanded.
        Assert.Equal("QQQ", Mappers.MapAndroidColor("QQQ"));
    }

    [Fact]
    public void AndroidColor_NoPropertyMeansNoColor()
    {
        Assert.Equal("NOCOLOR", Mappers.MapAndroidColor(null));
        Assert.Equal("NOCOLOR", Mappers.MapAndroidColor(""));
        Assert.Equal("NOCOLOR", Mappers.MapAndroidColor("   "));
    }

    [Theory]
    // "128GB,Samsung" is what the Pixel 8 Pro reports, and 128GB is what it is sold as.
    [InlineData("128GB,Samsung", 0L, "128GB")]
    [InlineData("256GB,Micron", 0L, "256GB")]
    [InlineData("64GB", 0L, "64GB")]
    [InlineData("1TB,Kingston", 0L, "1TB")]
    [InlineData("512GB", 0L, "512GB")]
    [InlineData("", 128_000_000_000L, "128GB")]
    [InlineData("", 0L, "NOSTORAGE")]
    [InlineData(null, 0L, "NOSTORAGE")]
    public void AndroidStorage_PrefersTheAdvertisedCapacity(string? raw, long dataBytes, string expected)
        => Assert.Equal(expected, Mappers.MapAndroidStorage(raw, dataBytes));

    [Fact]
    public void AndroidStorage_UsableSizeFallsBackToTheSameBucketAsTheAd()
    {
        // A real 128GB phone reports about 115GB usable because the system takes
        // its own partitions. The fallback has to land on the same 128GB bucket,
        // or the label would disagree with itself depending on the property.
        Assert.Equal("128GB", Mappers.MapAndroidStorage("", 114982996L * 1024));
        Assert.Equal("256GB", Mappers.MapAndroidStorage("", 236_000_000_000L));
    }

    [Fact]
    public void AndroidMemory_ReadsTheInstalledSize()
    {
        // "12GiB,Micron,LPDRAM5,ff07": binary and decimal units both land on 12GB
        // because a phone is sold in decimal gigabytes.
        Assert.Equal("12GB", Mappers.MapAndroidMemory("12GiB,Micron,LPDDR5,ff07"));
        Assert.Equal("8GB", Mappers.MapAndroidMemory("8GiB,Samsung"));
        Assert.Equal("16GB", Mappers.MapAndroidMemory("16GB"));
        Assert.Equal("NOMEMORY", Mappers.MapAndroidMemory(null));
        Assert.Equal("NOMEMORY", Mappers.MapAndroidMemory(""));
        Assert.Equal("NOMEMORY", Mappers.MapAndroidMemory("unreadable"));
    }

    [Fact]
    public void AndroidIntegrity_SeesACleanStockPixel8Pro()
    {
        var facts = new AndroidDeviceFacts
        {
            BuildFingerprint = "google/husky/husky:17/CP3A.260905.009/2026092501:user/release-keys",
            FlashLocked = "1",
            VbmetaDeviceState = "locked",
            VerifiedBootState = "yellow",
            WarrantyBit = "",
        };

        var checks = AndroidIntegrityChecks.Build(facts);
        ComponentStatus Row(string name) => checks.Single(c => c.Name == name);

        Assert.Equal(ComponentStatusType.Passed, Row("Bootloader").Status);
        Assert.Equal(ComponentStatusType.Passed, Row("Vbmeta").Status);
        Assert.Equal(ComponentStatusType.Passed, Row("Systeemimage").Status);
    }

    [Fact]
    public void AndroidIntegrity_YellowIsNotAVerdict()
    {
        // This is the reason the verified boot row is informational. A stock,
        // locked and unmodified Pixel 8 Pro reports "yellow", so a check that
        // failed on anything but green would condemn a clean device.
        var checks = AndroidIntegrityChecks.Build(new AndroidDeviceFacts
        {
            BuildFingerprint = "google/husky/husky:17/CP3A.260905.009/2026092501:user/release-keys",
            FlashLocked = "1",
            VbmetaDeviceState = "locked",
            VerifiedBootState = "yellow",
        });

        var verifiedBoot = checks.Single(c => c.Name == "Verified Boot");
        Assert.Equal(ComponentStatusType.Unknown, verifiedBoot.Status);
        Assert.Contains("schone toestel", verifiedBoot.Details);
        Assert.DoesNotContain(checks, c => c.Status == ComponentStatusType.Failed);
    }

    [Fact]
    public void AndroidIntegrity_CatchesAnUnlockedBootloader()
    {
        var checks = AndroidIntegrityChecks.Build(new AndroidDeviceFacts
        {
            BuildFingerprint = "google/husky/husky:17/CP3A.260905.009/2026092501:user/release-keys",
            FlashLocked = "0",
            VbmetaDeviceState = "unlocked",
            VerifiedBootState = "orange",
        });

        Assert.Equal(ComponentStatusType.Failed, checks.Single(c => c.Name == "Bootloader").Status);
        Assert.Equal(ComponentStatusType.Failed, checks.Single(c => c.Name == "Vbmeta").Status);
    }

    [Fact]
    public void AndroidIntegrity_CatchesAModifiedSystemImage()
    {
        // A custom ROM is not signed with the factory key, so the fingerprint no
        // longer ends in the stock tag even with the bootloader relocked.
        var checks = AndroidIntegrityChecks.Build(new AndroidDeviceFacts
        {
            BuildFingerprint = "google/husky/husky:17/CP3A.260905.009/2026092501:user/test-keys",
            FlashLocked = "1",
            VbmetaDeviceState = "locked",
        });

        var system = checks.Single(c => c.Name == "Systeemimage");
        Assert.Equal(ComponentStatusType.Failed, system.Status);
        Assert.Contains("Aangepast systeem", system.Description);
    }

    [Fact]
    public void AndroidIntegrity_ReadsTheSamsungWarrantyBit()
    {
        // The one Android signal that actually says something about non-original
        // parts, and the reason it is worth reading where it exists.
        var tripped = AndroidIntegrityChecks.Build(new AndroidDeviceFacts
        {
            BuildFingerprint = "samsung/beyond1/beyond1:14/UP1A.231005.007/1234:user/release-keys",
            FlashLocked = "1",
            WarrantyBit = "0x1",
        });

        Assert.Equal(ComponentStatusType.Failed, tripped.Single(c => c.Name == "Warrantybit").Status);

        var intact = AndroidIntegrityChecks.Build(new AndroidDeviceFacts
        {
            BuildFingerprint = "samsung/beyond1/beyond1:14/UP1A.231005.007/1234:user/release-keys",
            FlashLocked = "1",
            WarrantyBit = "0x0",
        });

        Assert.Equal(ComponentStatusType.Passed, intact.Single(c => c.Name == "Warrantybit").Status);
    }

    [Fact]
    public void AndroidIntegrity_UnknownDeviceIsNotACleanBillOfHealth()
    {
        // A device that reports none of these must not read as verified. Every row
        // lands on Unknown, which is the honest answer rather than a pass.
        var checks = AndroidIntegrityChecks.Build(new AndroidDeviceFacts());

        Assert.All(checks, c => Assert.Equal(ComponentStatusType.Unknown, c.Status));
        Assert.DoesNotContain(checks, c => c.Status == ComponentStatusType.Passed);
    }

    [Theory]
    [InlineData("android (pixel)", true)]
    [InlineData("Android (Pixel)", true)]
    [InlineData("iPhone14,2", false)]
    [InlineData("iPad14,3", false)]
    [InlineData(null, false)]
    public void IsAndroidProductType_OnlyMatchesAndroid(string? productType, bool expected)
        => Assert.Equal(expected, Mappers.IsAndroidProductType(productType));

    [Theory]
    [InlineData("#ffffff", ColorKeys.White)]
    [InlineData("#3B3B3C", ColorKeys.Black)] // case-insensitive
    [InlineData("3", ColorKeys.Gold)]
    [InlineData("18", ColorKeys.Green)]
    public void MapColor_Works(string raw, string expected)
        => Assert.Equal(expected, Mappers.MapColor(raw));

    [Fact]
    public void MapColor_UnknownBecomesTheUnknownKey()
        => Assert.Equal(ColorKeys.Unknown, Mappers.MapColor("#abcdef"));

    [Theory]
    [InlineData(30_000_000_000, "32GB")]
    [InlineData(60_000_000_000, "64GB")]
    [InlineData(127_000_000_000, "128GB")]
    [InlineData(255_501_272_064, "256GB")]
    [InlineData(500_000_000_000, "512GB")]
    [InlineData(1_000_000_000_000, "1TB")]
    [InlineData(2_000_000_000_000, "2TB")]
    [InlineData(5_000_000_000_000, "5000GB")] // beyond 2TB: honest GB figure
    public void MapStorage_BucketsCorrectly(long bytes, string expected)
        => Assert.Equal(expected, Mappers.MapStorage(bytes));

    [Fact]
    public void VerifyComponent_MatchingSerials_ReturnsMatch()
    {
        var status = Parsers.VerifyComponent("F17T1234ABCD", "F17T1234ABCD");
        Assert.Equal(ComponentStatusType.Match, status);
    }

    [Fact]
    public void VerifyComponent_MismatchedSerials_ReturnsMismatch()
    {
        var status = Parsers.VerifyComponent("F17T1234ABCD", "F17T9999XYZW");
        Assert.Equal(ComponentStatusType.Mismatch, status);
    }

    [Fact]
    public void VerifyComponent_MissingOrEmpty_ReturnsUnknown()
    {
        var status = Parsers.VerifyComponent("", "F17T9999XYZW");
        Assert.Equal(ComponentStatusType.Unknown, status);
    }

    [Fact]
    public void DeviceData_3uToolsProperties_InitializeCorrectly()
    {
        var data = new DeviceData
        {
            MotherboardSerialNumber = "C0212345678",
            TouchIdFaceIdSerialNumber = "MESA12345",
            BluetoothMacAddress = "00:11:22:33:44:55",
            WifiMacAddress = "66:77:88:99:AA:BB",
            FmiVerificationSource = "Via Server (API)"
        };

        Assert.Equal("C0212345678", data.MotherboardSerialNumber);
        Assert.Equal("MESA12345", data.TouchIdFaceIdSerialNumber);
        Assert.Equal("Via Server (API)", data.FmiVerificationSource);
    }

    [Fact]
    public void CleanSerial_DecodesBase64MlbCorrectly()
    {
        Assert.Equal("F8Y51234ABCD", Parsers.CleanSerial("RjhZNTEyMzRBQkNE"));
    }

    [Fact]
    public void CleanSerial_DecodesBase64MlbBinary_ToHex()
    {
        // "THjDhg==" is Base64 for 4 bytes: 0x4C, 0x78, 0xC3, 0x86
        // Must be decoded to hex "4C78C386", never left as raw Base64!
        string result = Parsers.CleanSerial("THjDhg==");
        Assert.Equal("4C78C386", result);
    }

    [Theory]
    [InlineData("yellow", ColorKeys.Gold)]
    [InlineData("Geel", ColorKeys.Gold)]
    [InlineData("gold", ColorKeys.Gold)]
    [InlineData("7", ColorKeys.Gold)]
    [InlineData("#ffcc00", ColorKeys.Gold)]
    public void MapColor_YellowAndGoldVariations_ReturnTheGoldKey(string input, string expected)
    {
        Assert.Equal(expected, Mappers.MapColor(input));
    }

    [Fact]
    public void VerifyComponent_WifiAndBluetooth_WithLiveAsOriginal_ReturnsMatch()
    {
        string liveMac = "94:bf:2d:74:3e:3f";
        // When factory database key is absent, live read is treated as original OEM
        var status = Parsers.VerifyComponent(liveMac, liveMac);
        Assert.Equal(ComponentStatusType.Match, status);
    }

    [Fact]
    public void DeviceData_QualityAndPayMethod_NotifyTheirOwnNames()
    {
        var data = new DeviceData();
        var changedProps = new List<string>();
        data.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProps.Add(e.PropertyName); };

        // The model holds the raw values only. Wording is a property of the language
        // and belongs above the model, so a display property here is what put a Dutch
        // sentence on an English screen in the first place.
        Assert.Null(typeof(DeviceData).GetProperty("GradeDisplay"));
        Assert.Null(typeof(DeviceData).GetProperty("InvoiceMethodDisplay"));

        data.Quality = "A";
        Assert.Equal("A", data.Quality);
        Assert.Contains("Quality", changedProps);
        Assert.Contains("SelectedGrade", changedProps);

        data.PayMethod = "Marge";
        Assert.Equal("Marge", data.PayMethod);
        Assert.Contains("PayMethod", changedProps);
        Assert.Contains("SelectedInvoiceMethod", changedProps);

        Assert.DoesNotContain("GradeDisplay", changedProps);
        Assert.DoesNotContain("InvoiceMethodDisplay", changedProps);
    }

    [Fact]
    public void VerifyComponent_NoOutput_ReturnsUnknown_NeverMatch()
    {
        var status = Parsers.VerifyComponent("NO OUTPUT", "NO OUTPUT");
        Assert.Equal(ComponentStatusType.Unknown, status);
    }

    [Fact]
    public void VerifyComponent_NullOrWhitespace_ReturnsUnknown()
    {
        Assert.Equal(ComponentStatusType.Unknown, Parsers.VerifyComponent(null, null));
        Assert.Equal(ComponentStatusType.Unknown, Parsers.VerifyComponent("   ", "   "));
        Assert.Equal(ComponentStatusType.Unknown, Parsers.VerifyComponent("ERROR: timeout", "ERROR: timeout"));
    }

    [Theory]
    [InlineData("gold", ColorKeys.Gold)]
    [InlineData("silver", ColorKeys.Silver)]
    [InlineData("space gray", ColorKeys.SpaceGrey)]
    [InlineData("rose gold", ColorKeys.RoseGold)]
    [InlineData("midnight", ColorKeys.Midnight)]
    [InlineData("starlight", ColorKeys.Starlight)]
    public void MapColor_MapsAppleColorsToTheirKeys(string raw, string expected)
    {
        Assert.Equal(expected, Mappers.MapColor(raw));
    }

    [Theory]
    [InlineData("331cee2aaa5478334708e8682bac3ef0f8979251", true)]
    [InlineData("00008030-001A34567890CDEF", true)]
    [InlineData("emulator-5554", false)]
    [InlineData("RFCW123456", false)]
    public void LooksLikeIosUdid_ClassifiesCorrectly(string id, bool expected)
    {
        Assert.Equal(expected, DeviceService.LooksLikeIosUdid(id));
    }

    [Fact]
    public void DeviceSessionManager_PreservesAndResumesSession()
    {
        string testUdid = "TEST_UDID_RESUME_123";
        var originalData = new DeviceData { Model = "iPhone 13", Identifier = "358123456789012" };
        
        DeviceSessionManager.PreserveDisconnectedSession(testUdid, originalData, 65);

        Assert.True(DeviceSessionManager.TryGetPreservedSession(testUdid, out var session));
        Assert.NotNull(session);
        Assert.Equal(0, session.SavedProgress); // Progress is intentionally reset to 0 per user requirement
        Assert.Equal("iPhone 13", session.Data?.Model);

        DeviceSessionManager.ResetDevice(testUdid);
        Assert.False(DeviceSessionManager.TryGetPreservedSession(testUdid, out _));
    }
}


public class PanicRulesTests
{
    [Fact]
    public void ThermalWatchdog_TG0BAndTG0V_Detected()
    {
        string log = "panic(cpu 0): userspace watchdog timeout: no successful checkins from thermalmonitord since wake " +
                     "SD: 0 BC: 1 Missing sensor(s): TG0B TG0V";
        var titles = PanicRules.Match(log).Select(i => i.Title).ToList();
        Assert.Contains(titles, t => t.Contains("TG0B"));
        Assert.Contains(titles, t => t.Contains("TG0V"));
        Assert.Contains(titles, t => t.Contains("Thermal"));
    }

    [Theory]
    [InlineData("Missing sensor(s): Mic1", "Mic1")]
    [InlineData("Missing sensor(s): Mic2", "Mic2")]
    [InlineData("Missing sensor(s): PRS0", "PRS0")]
    public void MissingSensors_Detected(string log, string keyword)
        => Assert.Contains(PanicRules.Match(log).Select(i => i.Title), t => t.Contains(keyword));

    [Fact]
    public void AOPProximity_Detected()
    {
        var issues = PanicRules.Match("AOP PANIC: SCMto: 0 - prox");
        Assert.Contains(issues, i => i.Title.Contains("proximity"));
    }

    [Fact]
    public void ANS2_NAND_Detected()
        => Assert.Contains(PanicRules.Match("ANS2 Recoverable Panic boot failure"), i => i.Title.Contains("NAND"));

    [Fact]
    public void SEP_Boot_Detected()
        => Assert.Contains(PanicRules.Match("panic: SEP ROM boot panic seput"), i => i.Title.Contains("SEP"));

    [Fact]
    public void SoftwareWatchdog_IsWarningNotError()
    {
        var issue = PanicRules.Match("userspace watchdog timeout: no successful checkins from backboardd")
            .First(i => i.Title.Contains("Software"));
        Assert.Equal(Severity.Warning, issue.Level);
    }

    [Fact]
    public void CleanLog_NoFindings()
        => Assert.Empty(PanicRules.Match("completely normal sysdiagnose with no errors"));

    [Fact]
    public void EveryFinding_HasNonEmptyExplanationAndFix()
    {
        // Feed a kitchen-sink log so most rules fire; verify data integrity.
        string log = string.Join("\n", PanicRules.All.Select(r => r.Title));
        foreach (var issue in PanicRules.Match(log))
        {
            Assert.False(string.IsNullOrWhiteSpace(issue.Explanation), $"{issue.Title}: empty explanation");
            Assert.False(string.IsNullOrWhiteSpace(issue.Fix), $"{issue.Title}: empty fix");
        }
    }

    [Fact]
    public void AllPatterns_AreValidRegex()
    {
        // Every rule must compile - a bad pattern would silently vanish in production.
        foreach (var rule in PanicRules.All)
        {
            var ex = Record.Exception(() => System.Text.RegularExpressions.Regex.Match("test", rule.Pattern));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void Dedupe_CollapsesDuplicateTitles()
    {
        string log = "Missing sensor(s): Mic1";
        var once = PanicRules.Match(log).Dedupe();
        var twice = PanicRules.Match(log + "\n" + log).Dedupe();
        Assert.Equal(once.Count, twice.Count);
    }
}

// The label template and the values that fill it.
//
// It used to be one class with one method and a static path, and its tests set a
// global so that a test run could not have two templates at once. Everything the
// label is now decided in LabelFields, filled by DymoTemplate and written by
// LabelWriter, so these tests read the file that was really written rather than a
// return value. The fill rules themselves are in LabelExportTests.

public class LabelTemplateLookupTests : IDisposable
{
    private readonly string _template;

    public LabelTemplateLookupTests()
    {
        _template = Path.Combine(Path.GetTempPath(), $"my-{Guid.NewGuid():N}.dymo");
        File.WriteAllText(_template, "<TextObject><Text>MODEL</Text></TextObject>");
    }

    [Fact]
    public void TheTemplateTheCallerAsksForIsTheOneThatIsUsed()
        => Assert.Equal(_template, DymoTemplateFiles.Locate(_template));

    [Fact]
    public void ATemplateThatIsNotThereIsNamedInTheMessage()
    {
        // The operator set the path once and has since moved the file. The
        // message has to point at the setting, or they go looking in the install
        // folder instead.
        var error = Assert.Throws<FileNotFoundException>(
            () => DymoTemplateFiles.Locate("/nonexistent/my.dymo"));

        Assert.Contains("Settings", error.Message);
    }

    [Fact]
    public void AConfiguredTemplateThatIsNotThereIsNotQuietlyReplacedByTheShippedOne()
    {
        // Silently printing on the shipped layout would put out labels the shop
        // never chose, and nothing on screen would say so.
        Assert.Throws<FileNotFoundException>(
            () => DymoTemplateFiles.Locate(Path.Combine(Path.GetTempPath(), "gone.dymo")));
    }

    [Fact]
    public void AnEmptyPathFindsTheTemplateThatCameWithTheApp()
    {
        // Which is the only thing a fresh install can print with.
        string found = DymoTemplateFiles.Locate();

        Assert.EndsWith("my.dymo", found);
        Assert.True(File.Exists(found));
    }

    [Fact]
    public void TheShippedTemplateIsFoundWhateverTheSettingSays()
        => Assert.NotNull(DymoTemplateFiles.Shipped);

    [Fact]
    public void ReadingATemplateGivesBackWhatIsInIt()
        => Assert.Contains("MODEL", DymoTemplateFiles.Read(_template));

    [Fact]
    public void ATemplateThisAppCannotFillIsNotAccepted()
    {
        // Refused at the moment of choosing rather than at the moment of printing,
        // because a template with no fields in it prints the last device forever.
        string merged = Path.Combine(Path.GetTempPath(), $"merged-{Guid.NewGuid():N}.dymo");
        File.WriteAllText(merged, "<DesktopLabel Version=\"1\"><DYMOLabel Version=\"3\">"
            + "<TextObject><Name>T</Name><Text>356938035643809 13 Pro</Text></TextObject>"
            + "</DYMOLabel></DesktopLabel>");

        try
        {
            Assert.False(ExportService.IsUsableTemplate(merged));
            Assert.True(ExportService.IsUsableTemplate(_template));
        }
        finally
        {
            File.Delete(merged);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_template)) File.Delete(_template);
    }
}

public class AuditLogServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AuditLogServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"audit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ExportAuditLog_CreatesJsonFileWithAllFields()
    {
        var deviceData = new DeviceData
        {
            Identifier = "356938035643809",
            DeviceId = "00008110-001234567890",
            Model = "13Pro",
            ProductType = "iPhone14,2",
            Color = ColorKeys.White,
            Storage = "256GB",
            BatteryHealth = "94",
            Quality = "A",
            PayMethod = "Marge",
            IosVersion = "17.4",
            BatteryCycleCount = 142,
            BatteryDesignCapacity = 3095,
            BatteryCurrentCapacity = 2900,
            BatterySerialNumber = "F8Y12345678",
            OriginalBatterySerialNumber = "F8Y12345678",
            DisplaySerialNumber = "DTM98765432",
            CoverGlassSerialNumber = "CG123456",
            FrontCameraSerialNumber = "FCAM999",
            RearCameraSerialNumber = "RCAM888",
            MotherboardSerialNumber = "C39ZX01",
            ComponentChecks =
            [
                new ComponentStatus
                {
                    Name = "Batterij",
                    SerialRead = "F8Y12345678",
                    SerialOriginal = "F8Y12345678",
                    Status = ComponentStatusType.Match
                },
                new ComponentStatus
                {
                    Name = "Scherm (LCM)",
                    SerialRead = "DTM98765432",
                    SerialOriginal = "DTM00000000",
                    Status = ComponentStatusType.Mismatch
                }
            ]
        };

        string exportedPath = AuditLogService.ExportAuditLog(deviceData, _tempDir);

        Assert.True(File.Exists(exportedPath));
        string content = File.ReadAllText(exportedPath);
        Assert.Contains("356938035643809", content);
        Assert.Contains("F8Y12345678", content);
        Assert.Contains("Match", content);
        Assert.Contains("Mismatch", content);
        Assert.Contains("\"BatteryCycleCount\": 142", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
