using Xunit;
using PhoneGrade.Core;
using PhoneGrade.Core.Diagnostics;

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
        Assert.Equal("Goed", Parsers.ParseAndroidBatteryStatus(Pixel8ProDumpsys));
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

    [Theory]
    [InlineData(2, "Goed")]
    [InlineData(3, "Oververhit")]
    [InlineData(4, "Defect")]
    [InlineData(5, "Overspanning")]
    [InlineData(6, "Storing")]
    [InlineData(7, "Te koud")]
    public void AndroidBatteryStatus_MapsTheStatusCode(int code, string expected)
    {
        string dumpsys = $"  status: 2\n  health: {code}\n  level: 55\n";
        Assert.Equal(expected, Parsers.ParseAndroidBatteryStatus(dumpsys));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  health: 99\n")]
    [InlineData("no battery service")]
    public void AndroidBatteryStatus_UnknownCodeIsNOBatt(string dumpsys)
        => Assert.Equal("NOBATT", Parsers.ParseAndroidBatteryStatus(dumpsys));

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
    [InlineData("android (pixel)", true)]
    [InlineData("Android (Pixel)", true)]
    [InlineData("iPhone14,2", false)]
    [InlineData("iPad14,3", false)]
    [InlineData(null, false)]
    public void IsAndroidProductType_OnlyMatchesAndroid(string? productType, bool expected)
        => Assert.Equal(expected, Mappers.IsAndroidProductType(productType));

    [Theory]
    [InlineData("#ffffff", "Wit")]
    [InlineData("#3B3B3C", "Zwart")] // case-insensitive
    [InlineData("3", "Goud")]
    [InlineData("18", "Groen")]
    public void MapColor_Works(string raw, string expected)
        => Assert.Equal(expected, Mappers.MapColor(raw));

    [Fact]
    public void MapColor_UnknownReturnsOnbekend()
        => Assert.Equal("Onbekend", Mappers.MapColor("#abcdef"));

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
    [InlineData("yellow", "Goud")]
    [InlineData("Geel", "Goud")]
    [InlineData("gold", "Goud")]
    [InlineData("7", "Goud")]
    [InlineData("#ffcc00", "Goud")]
    public void MapColor_YellowAndGoldVariations_ReturnGoud(string input, string expected)
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
    public void DeviceData_QualityAndPayMethod_NotifyPropertyChangedAndFormatDisplay()
    {
        var data = new DeviceData();
        var changedProps = new List<string>();
        data.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProps.Add(e.PropertyName); };

        Assert.Equal("Niet beoordeeld", data.GradeDisplay);
        Assert.Equal("Niet opgegeven", data.InvoiceMethodDisplay);

        // Mutate Quality
        data.Quality = "A";
        Assert.Equal("KLASSE A", data.GradeDisplay);
        Assert.Contains("Quality", changedProps);
        Assert.Contains("SelectedGrade", changedProps);
        Assert.Contains("GradeDisplay", changedProps);

        // Mutate PayMethod
        data.PayMethod = "Marge";
        Assert.Equal("Marge (0% BTW)", data.InvoiceMethodDisplay);
        Assert.Contains("PayMethod", changedProps);
        Assert.Contains("SelectedInvoiceMethod", changedProps);
        Assert.Contains("InvoiceMethodDisplay", changedProps);
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
    [InlineData("gold", "Goud")]
    [InlineData("silver", "Zilver")]
    [InlineData("space gray", "Spacegrijs")]
    [InlineData("rose gold", "Rosé Goud")]
    [InlineData("midnight", "Middernacht")]
    [InlineData("starlight", "Sterrenlicht")]
    public void MapColor_MapsAppleColorsAccurately(string raw, string expected)
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
        // Every rule must compile — a bad pattern would silently vanish in production.
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

public class LabelServiceTests : IDisposable
{
    private readonly string _template;

    public LabelServiceTests()
    {
        _template = Path.Combine(Path.GetTempPath(), $"my-{Guid.NewGuid():N}.dymo");
        File.WriteAllText(_template,
            "ID=IDENTIFIER M=MODEL C=PCOLOR B=BATTERY Q=QUALITY P=PAYM S=STORAGE");
        LabelService.ConfiguredTemplatePath = _template;
    }

    [Fact]
    public void GenerateLabel_ReplacesAllPlaceholders()
    {
        string path = LabelService.GenerateLabel(new DeviceData
        {
            Identifier = "356938035643809", Model = "13Pro", Color = "Wit",
            BatteryHealth = "90", Quality = "A", PayMethod = "Marge", Storage = "256GB",
        });
        Assert.Equal("ID=356938035643809 M=13Pro C=Wit B=90% Q=A P=Marge S=256GB",
            File.ReadAllText(path));
    }

    [Fact]
    public void GenerateLabel_AddsWarningForLowBattery()
    {
        // Battery < 85% should show "[X]" warning on label
        string path = LabelService.GenerateLabel(new DeviceData { BatteryHealth = "68" });
        Assert.Contains("68% [X]", File.ReadAllText(path));
        
        // Battery >= 85% should NOT show "[X]"
        path = LabelService.GenerateLabel(new DeviceData { BatteryHealth = "92" });
        string content = File.ReadAllText(path);
        Assert.Contains("92%", content);
        Assert.DoesNotContain("[X]", content);
    }

    [Fact]
    public void GenerateLabel_AndroidPercentFromCapacityCounters()
    {
        string path = LabelService.GenerateLabel(new DeviceData
        {
            BatteryHealth = $"{Parsers.ParseAndroidBatteryCondition("4400000", "5000000")}%",
        });
        Assert.Contains("B=88%", File.ReadAllText(path));
        Assert.DoesNotContain("[X]", File.ReadAllText(path));
    }

    [Fact]
    public void GenerateLabel_AndroidStatusWordGetsNoPercentSign()
    {
        // When the capacity counters are unreadable Android can only report a
        // status code. "Goed%" on a label would be nonsense.
        string path = LabelService.GenerateLabel(new DeviceData
        {
            BatteryHealth = Parsers.ParseAndroidBatteryStatus("  health: 2\n  level: 20\n"),
        });
        Assert.Contains("B=Goed ", File.ReadAllText(path));
    }

    [Fact]
    public void GenerateLabel_NoBatteryData_StaysUnchanged()
    {
        string path = LabelService.GenerateLabel(new DeviceData { BatteryHealth = "NOBATT" });
        Assert.Contains("B=NOBATT", File.ReadAllText(path));
    }

    [Fact]
    public void FindTemplate_FallsBackToBundledAssetsDir()
    {
        // The Tests bin inherits the UI's bundled Assets/my.dymo via the project
        // reference — the app-dir fallback should find it without a configured path.
        LabelService.ConfiguredTemplatePath = "/nonexistent/my.dymo";
        string found = LabelService.FindTemplate();
        Assert.EndsWith("my.dymo", found);
        Assert.True(File.Exists(found));
    }

    public void Dispose()
    {
        if (File.Exists(_template)) File.Delete(_template);
        LabelService.ConfiguredTemplatePath = null;
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
            Color = "Wit",
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
