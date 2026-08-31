using Xunit;
using AutoDymoLabel.Core;
using AutoDymoLabel.Core.Diagnostics;

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
    public void MapModel_EmptyReturnsNoModel()
        => Assert.Equal("NOMODEL", Mappers.MapModel("  "));

    [Theory]
    [InlineData("#ffffff", "Wit")]
    [InlineData("#3B3B3C", "Zwart")] // case-insensitive
    [InlineData("3", "Goud")]
    [InlineData("18", "Groen")]
    public void MapColor_Works(string raw, string expected)
        => Assert.Equal(expected, Mappers.MapColor(raw));

    [Fact]
    public void MapColor_UnknownReturnsNoColor()
        => Assert.Equal("NOCOLOR", Mappers.MapColor("#abcdef"));

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
    public void GenerateLabel_KeepsExistingPercentSign()
    {
        // The 85% checker writes "100%-X" — must not become "100%-X%"
        string path = LabelService.GenerateLabel(new DeviceData { BatteryHealth = "100%-X" });
        Assert.Contains("B=100%-X", File.ReadAllText(path));
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
