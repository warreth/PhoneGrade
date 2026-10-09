using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.ShopProfiles;
using Xunit;

namespace Tests;

// ============ The shop profile: what travels between computers ============
//
// A profile is the shareable half of the settings. These tests pin both sides of
// that promise: everything a shop configures travels, and everything that belongs
// to one machine (the licence token, the first-run flags, the debug switches, the
// template path) never does. The exact property set of the file is pinned as
// well, so a setting added later cannot start travelling by accident.

public class ShopProfileTests
{
    /// <summary>
    /// Settings with every shareable field set to something other than its
    /// default, plus values in the machine-local fields that must not travel.
    /// </summary>
    private static AppSettings FullyLoaded() => new()
    {
        Theme = "Light",
        Language = "en",
        AutoActivate = true,
        AutoDetectOnPlug = true,
        AutoStartWebTest = true,
        ShowSummaryScreenAfterTesting = false,
        RequirePwaTest = false,
        AutoFinishAfterTest = true,
        EnableUsbEventMonitoring = false,
        ImeiInfoApiKey = "key-123",
        SelectedImeiChecks = ["blacklist_simple", "samsung_info_knox"],
        EstimatedAppleDevices = 42,
        EstimatedAndroidDevices = 7,
        DefaultQuality = "B",
        DefaultPaymentMethod = "BTW",
        LabelStockPartNumber = "1983172",
        LabelBarcodeMode = LabelBarcodeMode.Combined,
        LabelSymbology = LabelCodeSymbology.Code128,
        LabelBarcodeEnabled = false,
        LabelVariant = LabelVariant.GradeBlock,
        LabelShowBatteryCycles = false,
        LabelBatteryThreshold = 80,
        LabelCyclesThreshold = 300,
        ExportFormats = [ExportFormat.DymoLabel, ExportFormat.Json, ExportFormat.Csv],
        ExportFolderScheme = ExportFolderScheme.Inspection,
        UseSecureOrigin = false,
        UsePublicTunnel = false,
        RunDiagnostics = false,
        Enable85PercentChecker = false,
        OpenEditorBeforePrint = true,
        IncludePrereleases = true,

        // Machine-local, and the reason the file is an allowlist.
        TemplatePath = "/private/template.dymo",
        TrialToken = "TOKEN-VALUE",
        IntroSeen = true,
        LastSeenVersion = "9.9.9",
        IsDebugMode = true,
        EnableVerboseNetworkLogging = true,
    };

    [Fact]
    public void FromSettings_CopiesEveryShareableField()
    {
        AppSettings settings = FullyLoaded();
        ShopProfile profile = ShopProfileMapper.FromSettings(settings, "1.2.3");

        Assert.Equal(ShopProfile.CurrentSchemaVersion, profile.SchemaVersion);
        Assert.Equal("1.2.3", profile.AppVersion);
        Assert.NotNull(profile.ExportedAtUtc);

        Assert.Equal(settings.Theme, profile.Theme);
        Assert.Equal(settings.Language, profile.Language);
        Assert.Equal(settings.AutoActivate, profile.AutoActivate);
        Assert.Equal(settings.AutoDetectOnPlug, profile.AutoDetectOnPlug);
        Assert.Equal(settings.AutoStartWebTest, profile.AutoStartWebTest);
        Assert.Equal(settings.ShowSummaryScreenAfterTesting, profile.ShowSummaryScreenAfterTesting);
        Assert.Equal(settings.RequirePwaTest, profile.RequirePwaTest);
        Assert.Equal(settings.AutoFinishAfterTest, profile.AutoFinishAfterTest);
        Assert.Equal(settings.EnableUsbEventMonitoring, profile.EnableUsbEventMonitoring);
        Assert.Equal(settings.ImeiInfoApiKey, profile.ImeiInfoApiKey);
        Assert.Equal(settings.SelectedImeiChecks, profile.SelectedImeiChecks);
        Assert.Equal(settings.EstimatedAppleDevices, profile.EstimatedAppleDevices);
        Assert.Equal(settings.EstimatedAndroidDevices, profile.EstimatedAndroidDevices);
        Assert.Equal(settings.DefaultQuality, profile.DefaultQuality);
        Assert.Equal(settings.DefaultPaymentMethod, profile.DefaultPaymentMethod);
        Assert.Equal(settings.LabelStockPartNumber, profile.LabelStockPartNumber);
        Assert.Equal(settings.LabelBarcodeMode, profile.LabelBarcodeMode);
        Assert.Equal(settings.LabelSymbology, profile.LabelSymbology);
        Assert.Equal(settings.LabelBarcodeEnabled, profile.LabelBarcodeEnabled);
        Assert.Equal(settings.LabelVariant, profile.LabelVariant);
        Assert.Equal(settings.LabelShowBatteryCycles, profile.LabelShowBatteryCycles);
        Assert.Equal(settings.LabelBatteryThreshold, profile.LabelBatteryThreshold);
        Assert.Equal(settings.LabelCyclesThreshold, profile.LabelCyclesThreshold);
        Assert.Equal(settings.ExportFormats, profile.ExportFormats);
        Assert.Equal(settings.ExportFolderScheme, profile.ExportFolderScheme);
        Assert.Equal(settings.UseSecureOrigin, profile.UseSecureOrigin);
        Assert.Equal(settings.UsePublicTunnel, profile.UsePublicTunnel);
        Assert.Equal(settings.RunDiagnostics, profile.RunDiagnostics);
        Assert.Equal(settings.Enable85PercentChecker, profile.Enable85PercentChecker);
        Assert.Equal(settings.OpenEditorBeforePrint, profile.OpenEditorBeforePrint);
        Assert.Equal(settings.IncludePrereleases, profile.IncludePrereleases);
    }

    [Fact]
    public void Serialize_ContainsExactlyTheShareableProperties()
    {
        string json = ShopProfileCodec.Serialize(ShopProfileMapper.FromSettings(new AppSettings()));

        using var document = JsonDocument.Parse(json);
        string[] actual = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "AppVersion", "AutoActivate", "AutoDetectOnPlug", "AutoFinishAfterTest",
            "AutoStartWebTest", "DefaultPaymentMethod", "DefaultQuality",
            "Enable85PercentChecker", "EnableUsbEventMonitoring", "EstimatedAndroidDevices",
            "EstimatedAppleDevices", "ExportFolderScheme", "ExportFormats", "ExportedAtUtc",
            "ImeiInfoApiKey", "IncludePrereleases", "LabelBarcodeEnabled", "LabelBarcodeMode",
            "LabelBatteryThreshold", "LabelCyclesThreshold", "LabelShowBatteryCycles",
            "LabelStockPartNumber", "LabelSymbology", "LabelVariant", "Language",
            "OpenEditorBeforePrint", "RequirePwaTest", "RunDiagnostics", "SchemaVersion",
            "SelectedImeiChecks", "ShowSummaryScreenAfterTesting", "Theme", "UsePublicTunnel",
            "UseSecureOrigin",
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Serialize_NeverCarriesTheMachineLocalFields()
    {
        string json = ShopProfileCodec.Serialize(ShopProfileMapper.FromSettings(FullyLoaded()));

        string[] mustNotAppear =
        [
            "TrialToken", "TOKEN-VALUE",
            "IntroSeen",
            "LastSeenVersion", "9.9.9",
            "IsDebugMode", "EnableVerboseNetworkLogging",
            "TemplatePath", "template.dymo",
        ];

        foreach (string absent in mustNotAppear)
            Assert.DoesNotContain(absent, json);
    }

    [Fact]
    public void SerializeParse_RoundTripsEveryField()
    {
        ShopProfile original = ShopProfileMapper.FromSettings(FullyLoaded(), "1.2.3");
        string json = ShopProfileCodec.Serialize(original);

        Assert.True(ShopProfileCodec.TryParse(json, out ShopProfile? parsed, out string? error));
        Assert.Null(error);
        Assert.NotNull(parsed);
        AssertProfilesEqual(original, parsed!);
    }

    [Fact]
    public void TryParse_InvalidJson_IsRefused()
    {
        Assert.False(ShopProfileCodec.TryParse("{ this is not json ]", out _, out string? error));
        Assert.Equal(ShopProfileCodec.InvalidFileKey, error);
    }

    [Fact]
    public void TryParse_WithoutSchemaVersion_IsRefused()
    {
        Assert.False(ShopProfileCodec.TryParse("""{"Theme":"Light"}""", out _, out string? error));
        Assert.Equal(ShopProfileCodec.InvalidFileKey, error);
    }

    [Fact]
    public void TryParse_FromANewerBuild_IsRefusedWithItsOwnMessage()
    {
        Assert.False(ShopProfileCodec.TryParse("""{"SchemaVersion":2}""", out _, out string? error));
        Assert.Equal(ShopProfileCodec.NewerVersionKey, error);
    }

    [Fact]
    public void TryParse_UnknownProperties_AreIgnored()
    {
        string json = """{"SchemaVersion":1,"SomethingFromTheFuture":true,"Theme":"Light"}""";

        Assert.True(ShopProfileCodec.TryParse(json, out ShopProfile? profile, out string? error));
        Assert.Null(error);
        Assert.Equal("Light", profile!.Theme);
    }

    [Fact]
    public void TryParse_MissingFields_StayNull()
    {
        Assert.True(ShopProfileCodec.TryParse("""{"SchemaVersion":1}""", out ShopProfile? profile, out _));

        Assert.Null(profile!.Theme);
        Assert.Null(profile.RequirePwaTest);
        Assert.Null(profile.ExportFormats);
        Assert.Null(profile.ExportFolderScheme);
    }

    [Fact]
    public void Compare_ListsOnlyTheSettingsThatChange()
    {
        var incoming = new ShopProfile
        {
            SchemaVersion = 1,
            RequirePwaTest = false,
            LabelStockPartNumber = "1983172",
        };

        IReadOnlyList<ShopProfileChange> changes =
            ShopProfileDiff.Compare(new AppSettings(), incoming, key => key);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, change => change.LabelKey == "Settings_RequirePwaTest");
        Assert.Contains(changes, change => change.LabelKey == "Export_Stock");
    }

    [Fact]
    public void Compare_SaysOnAndOffRatherThanTrueAndFalse()
    {
        var incoming = new ShopProfile { SchemaVersion = 1, AutoStartWebTest = true };
        IReadOnlyList<ShopProfileChange> changes =
            ShopProfileDiff.Compare(new AppSettings(), incoming, key => $"[{key}]");

        ShopProfileChange change = Assert.Single(changes);
        Assert.Equal("Settings_AutoStartWebTest", change.LabelKey);
        Assert.Equal("[Settings_ShopProfileValueOff]", change.OldDisplay);
        Assert.Equal("[Settings_ShopProfileValueOn]", change.NewDisplay);
    }

    [Fact]
    public void Compare_NeverShowsTheApiKeyItself()
    {
        var incoming = new ShopProfile { SchemaVersion = 1, ImeiInfoApiKey = "SECRET-KEY" };
        IReadOnlyList<ShopProfileChange> changes =
            ShopProfileDiff.Compare(new AppSettings(), incoming, key => $"[{key}]");

        ShopProfileChange change = Assert.Single(changes);
        Assert.Equal("[Settings_ShopProfileValueNotSet]", change.OldDisplay);
        Assert.Equal("[Settings_ShopProfileValueSet]", change.NewDisplay);
        Assert.DoesNotContain("SECRET-KEY", change.OldDisplay + change.NewDisplay);
    }

    [Fact]
    public void Compare_NamesTheSelectedChecks()
    {
        var incoming = new ShopProfile { SchemaVersion = 1, SelectedImeiChecks = ["blacklist_simple"] };
        IReadOnlyList<ShopProfileChange> changes =
            ShopProfileDiff.Compare(new AppSettings(), incoming, key => $"[{key}]");

        ShopProfileChange change = Assert.Single(changes);
        Assert.Equal("Settings_ImeiInfoChecks", change.LabelKey);
        Assert.Equal("[Settings_ShopProfileValueNone]", change.OldDisplay);
        Assert.Equal("[Settings_ImeiCheck_BlacklistSimple]", change.NewDisplay);
    }

    [Fact]
    public void Compare_SameChecksInADifferentOrder_IsNoChange()
    {
        var current = new AppSettings { SelectedImeiChecks = ["blacklist_simple", "samsung_info_knox"] };
        var incoming = new ShopProfile
        {
            SchemaVersion = 1,
            SelectedImeiChecks = ["samsung_info_knox", "blacklist_simple"],
        };

        Assert.Empty(ShopProfileDiff.Compare(current, incoming, key => key));
    }

    [Fact]
    public void Compare_NamesTheExportFormatsAndTheFolderScheme()
    {
        var incoming = new ShopProfile
        {
            SchemaVersion = 1,
            ExportFormats = [ExportFormat.Json],
            ExportFolderScheme = ExportFolderScheme.Month,
        };

        IReadOnlyList<ShopProfileChange> changes =
            ShopProfileDiff.Compare(new AppSettings(), incoming, key => $"[{key}]");

        Assert.Contains(changes, change =>
            change.LabelKey == "Export_FormatsTitle" && change.NewDisplay == "[Export_FormatJson]");
        Assert.Contains(changes, change =>
            change.LabelKey == "Settings_LabelFolder" && change.NewDisplay == "[Settings_FolderMonth]");
    }

    [Fact]
    public void Compare_WithIdenticalSettings_IsEmpty()
    {
        AppSettings settings = FullyLoaded();
        ShopProfile incoming = ShopProfileMapper.FromSettings(settings);

        Assert.Empty(ShopProfileDiff.Compare(settings, incoming, key => key));
    }

    [Fact]
    public void Serialize_WritesEnumsAsNames()
    {
        string json = ShopProfileCodec.Serialize(ShopProfileMapper.FromSettings(FullyLoaded()));

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("GradeBlock", root.GetProperty("LabelVariant").GetString());
        Assert.Equal("Inspection", root.GetProperty("ExportFolderScheme").GetString());
        Assert.Equal(
            new[] { "DymoLabel", "Json", "Csv" },
            root.GetProperty("ExportFormats").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void TheShopProfileKeysAreInEveryLanguage()
    {
        string[] keys =
        [
            "Settings_SecShopProfile", "Settings_ShopProfileLead", "Settings_ShopProfileNote",
            "Settings_ShopProfileExport", "Settings_ShopProfileImport", "Settings_ShopProfileExportDone",
            "Settings_ShopProfileExportFailed", "Settings_ShopProfileReadFailed",
            "Settings_ShopProfilePickerFailed", "Settings_ShopProfileErrorInvalid",
            "Settings_ShopProfileErrorNewer", "Settings_ShopProfileNoChanges",
            "Settings_ShopProfilePreviewTitle", "Settings_ShopProfilePreviewSummary",
            "Settings_ShopProfileApply", "Settings_ShopProfileCancel", "Settings_ShopProfileApplied",
            "Settings_ShopProfileValueOn", "Settings_ShopProfileValueOff",
            "Settings_ShopProfileValueSet", "Settings_ShopProfileValueNotSet",
            "Settings_ShopProfileValueNone",
        ];

        string resources = RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Resources");
        string[] files = Directory.GetFiles(resources, "Strings.*.axaml");
        Assert.Equal(7, files.Length);

        foreach (string file in files)
        {
            var defined = XDocument.Load(file)
                .Descendants()
                .Attributes()
                .Where(attribute => attribute.Name.LocalName == "Key")
                .Select(attribute => attribute.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string key in keys)
                Assert.Contains(key, defined);
        }
    }

    private static void AssertProfilesEqual(ShopProfile expected, ShopProfile actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.ExportedAtUtc, actual.ExportedAtUtc);
        Assert.Equal(expected.AppVersion, actual.AppVersion);
        Assert.Equal(expected.Theme, actual.Theme);
        Assert.Equal(expected.Language, actual.Language);
        Assert.Equal(expected.AutoActivate, actual.AutoActivate);
        Assert.Equal(expected.AutoDetectOnPlug, actual.AutoDetectOnPlug);
        Assert.Equal(expected.AutoStartWebTest, actual.AutoStartWebTest);
        Assert.Equal(expected.ShowSummaryScreenAfterTesting, actual.ShowSummaryScreenAfterTesting);
        Assert.Equal(expected.RequirePwaTest, actual.RequirePwaTest);
        Assert.Equal(expected.AutoFinishAfterTest, actual.AutoFinishAfterTest);
        Assert.Equal(expected.EnableUsbEventMonitoring, actual.EnableUsbEventMonitoring);
        Assert.Equal(expected.ImeiInfoApiKey, actual.ImeiInfoApiKey);
        Assert.Equal(expected.SelectedImeiChecks, actual.SelectedImeiChecks);
        Assert.Equal(expected.EstimatedAppleDevices, actual.EstimatedAppleDevices);
        Assert.Equal(expected.EstimatedAndroidDevices, actual.EstimatedAndroidDevices);
        Assert.Equal(expected.DefaultQuality, actual.DefaultQuality);
        Assert.Equal(expected.DefaultPaymentMethod, actual.DefaultPaymentMethod);
        Assert.Equal(expected.LabelStockPartNumber, actual.LabelStockPartNumber);
        Assert.Equal(expected.LabelBarcodeMode, actual.LabelBarcodeMode);
        Assert.Equal(expected.LabelSymbology, actual.LabelSymbology);
        Assert.Equal(expected.LabelBarcodeEnabled, actual.LabelBarcodeEnabled);
        Assert.Equal(expected.LabelVariant, actual.LabelVariant);
        Assert.Equal(expected.LabelShowBatteryCycles, actual.LabelShowBatteryCycles);
        Assert.Equal(expected.LabelBatteryThreshold, actual.LabelBatteryThreshold);
        Assert.Equal(expected.LabelCyclesThreshold, actual.LabelCyclesThreshold);
        Assert.Equal(expected.ExportFormats, actual.ExportFormats);
        Assert.Equal(expected.ExportFolderScheme, actual.ExportFolderScheme);
        Assert.Equal(expected.UseSecureOrigin, actual.UseSecureOrigin);
        Assert.Equal(expected.UsePublicTunnel, actual.UsePublicTunnel);
        Assert.Equal(expected.RunDiagnostics, actual.RunDiagnostics);
        Assert.Equal(expected.Enable85PercentChecker, actual.Enable85PercentChecker);
        Assert.Equal(expected.OpenEditorBeforePrint, actual.OpenEditorBeforePrint);
        Assert.Equal(expected.IncludePrereleases, actual.IncludePrereleases);
    }
}
