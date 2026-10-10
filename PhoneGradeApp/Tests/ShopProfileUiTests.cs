using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ShopProfiles;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The shop profile section ============
//
// Export writes the shareable half of the settings to a file, import shows what
// that file would change before anything is written, and apply goes through the
// same public properties the settings screen binds to. These tests drive the
// view model end to end and then look at the rendered section, because a preview
// nobody can see is not a preview.

[Collection(LanguageCollection.Name)]
public class ShopProfileUiTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"shop-profile-ui-{Guid.NewGuid():N}");
    private readonly string _profileDir = Path.Combine(Path.GetTempPath(), $"shop-profile-files-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

    public ShopProfileUiTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Directory.CreateDirectory(_profileDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);

        // Machine-local values that an import must leave alone, plus defaults for
        // everything the profile does carry.
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"),
            """{"Theme":"Dark","Language":"nl","IntroSeen":true,"TrialToken":"seeded-token","LastSeenVersion":"0.0.1","IsDebugMode":true,"TemplatePath":"/tmp/machine-template.dymo"}""");

        // Status lines are asserted in English so the wording comes from the
        // dictionary rather than from whichever language ran last.
        LocalizationManager.SetLanguage("en");
    }

    [AvaloniaFact]
    public void Export_WritesEveryShareableField()
    {
        using var vm = new MainWindowViewModel();
        vm.Theme = "Light";
        vm.Language = "English";
        vm.AutoActivate = true;
        vm.AutoDetectOnPlug = true;
        vm.AutoStartWebTest = true;
        vm.ShowSummaryScreenAfterTesting = false;
        vm.RequirePwaTest = false;
        vm.AutoFinishAfterTest = true;
        vm.EnableUsbEventMonitoring = false;
        vm.ImeiInfoApiKey = "key-123";
        vm.SelectedImeiChecks = ["blacklist_simple", "samsung_info_knox"];
        vm.EstimatedAppleDevices = 42;
        vm.EstimatedAndroidDevices = 7;
        vm.DefaultQuality = "B";
        vm.DefaultPaymentMethod = "BTW";
        vm.LabelStockPartNumber = "1983172";
        vm.LabelBarcodeMode = LabelBarcodeMode.Combined;
        vm.LabelSymbology = LabelCodeSymbology.Code128;
        vm.LabelBarcodeEnabled = false;
        vm.LabelVariant = LabelVariant.GradeBlock;
        vm.LabelShowBatteryCycles = false;
        vm.LabelBatteryThreshold = 80;
        vm.LabelCyclesThreshold = 300;
        vm.ExportFormats = [ExportFormat.DymoLabel, ExportFormat.Json, ExportFormat.Csv];
        vm.ExportFolderScheme = ExportFolderScheme.Inspection;
        vm.UseSecureOrigin = false;
        vm.UsePublicTunnel = false;
        vm.RunDiagnostics = false;
        vm.Enable85PercentChecker = false;
        vm.OpenEditorBeforePrint = true;
        vm.IncludePrereleases = true;

        string path = Path.Combine(_profileDir, "exported.json");
        vm.ExportShopProfileTo(path);

        Assert.True(File.Exists(path));
        Assert.True(ShopProfileCodec.TryParse(File.ReadAllText(path), out ShopProfile? profile, out string? error));
        Assert.Null(error);

        Assert.Equal(ShopProfile.CurrentSchemaVersion, profile!.SchemaVersion);
        Assert.Equal("Light", profile.Theme);
        Assert.Equal("en", profile.Language);
        Assert.True(profile.AutoActivate);
        Assert.True(profile.AutoDetectOnPlug);
        Assert.True(profile.AutoStartWebTest);
        Assert.False(profile.ShowSummaryScreenAfterTesting);
        Assert.False(profile.RequirePwaTest);
        Assert.True(profile.AutoFinishAfterTest);
        Assert.False(profile.EnableUsbEventMonitoring);
        Assert.Equal("key-123", profile.ImeiInfoApiKey);
        Assert.Equal(["blacklist_simple", "samsung_info_knox"], profile.SelectedImeiChecks);
        Assert.Equal(42, profile.EstimatedAppleDevices);
        Assert.Equal(7, profile.EstimatedAndroidDevices);
        Assert.Equal("B", profile.DefaultQuality);
        Assert.Equal("BTW", profile.DefaultPaymentMethod);
        Assert.Equal("1983172", profile.LabelStockPartNumber);
        Assert.Equal(LabelBarcodeMode.Combined, profile.LabelBarcodeMode);
        Assert.Equal(LabelCodeSymbology.Code128, profile.LabelSymbology);
        Assert.False(profile.LabelBarcodeEnabled);
        Assert.Equal(LabelVariant.GradeBlock, profile.LabelVariant);
        Assert.False(profile.LabelShowBatteryCycles);
        Assert.Equal(80, profile.LabelBatteryThreshold);
        Assert.Equal(300, profile.LabelCyclesThreshold);
        Assert.Equal([ExportFormat.DymoLabel, ExportFormat.Json, ExportFormat.Csv], profile.ExportFormats);
        Assert.Equal(ExportFolderScheme.Inspection, profile.ExportFolderScheme);
        Assert.False(profile.UseSecureOrigin);
        Assert.False(profile.UsePublicTunnel);
        Assert.False(profile.RunDiagnostics);
        Assert.False(profile.Enable85PercentChecker);
        Assert.True(profile.OpenEditorBeforePrint);
        Assert.True(profile.IncludePrereleases);

        Assert.Equal(
            string.Format(LocalizationManager.GetString("Settings_ShopProfileExportDone"), "exported.json"),
            vm.ShopProfileStatus);
    }

    [AvaloniaFact]
    public void Import_PreviewsThenAppliesEveryField()
    {
        using var vm = new MainWindowViewModel();

        // What the app itself holds after start; an import may not move any of it.
        AppSettings before = AppSettings.Load();
        Assert.False(string.IsNullOrEmpty(before.TrialToken));
        string token = before.TrialToken;
        string lastSeenVersion = before.LastSeenVersion;
        string? templatePath = before.TemplatePath;

        string path = WriteProfile(FullyLoadedProfile());
        vm.PreviewShopProfileImport(path);

        Assert.True(vm.HasShopProfilePreview);
        Assert.Equal(31, vm.ShopProfileChanges.Count);
        Assert.Contains("31", vm.ShopProfilePreviewSummary);
        Assert.Equal("", vm.ShopProfileStatus);

        vm.ApplyShopProfile();

        Assert.False(vm.HasShopProfilePreview);
        Assert.Empty(vm.ShopProfileChanges);
        Assert.Equal(LocalizationManager.GetString("Settings_ShopProfileApplied"), vm.ShopProfileStatus);

        Assert.Equal("Light", vm.Theme);
        Assert.Equal("English", vm.Language);
        Assert.True(vm.AutoActivate);
        Assert.True(vm.AutoDetectOnPlug);
        Assert.True(vm.AutoStartWebTest);
        Assert.False(vm.ShowSummaryScreenAfterTesting);
        Assert.False(vm.RequirePwaTest);
        Assert.True(vm.AutoFinishAfterTest);
        Assert.False(vm.EnableUsbEventMonitoring);
        Assert.Equal("imported-key", vm.ImeiInfoApiKey);
        Assert.Equal(["blacklist_simple", "samsung_info_knox"], vm.SelectedImeiChecks);
        Assert.Equal(42, vm.EstimatedAppleDevices);
        Assert.Equal(7, vm.EstimatedAndroidDevices);
        Assert.Equal("B", vm.DefaultQuality);
        Assert.Equal("BTW", vm.DefaultPaymentMethod);
        Assert.Equal("1983172", vm.LabelStockPartNumber);
        Assert.Equal(LabelBarcodeMode.Combined, vm.LabelBarcodeMode);
        Assert.Equal(LabelCodeSymbology.Code128, vm.LabelSymbology);
        Assert.False(vm.LabelBarcodeEnabled);
        Assert.Equal(LabelVariant.GradeBlock, vm.LabelVariant);
        Assert.False(vm.LabelShowBatteryCycles);
        Assert.Equal(80, vm.LabelBatteryThreshold);
        Assert.Equal(300, vm.LabelCyclesThreshold);
        Assert.Equal([ExportFormat.DymoLabel, ExportFormat.Json, ExportFormat.Csv], vm.ExportFormats);
        Assert.Equal(ExportFolderScheme.Inspection, vm.ExportFolderScheme);
        Assert.False(vm.UseSecureOrigin);
        Assert.False(vm.UsePublicTunnel);
        Assert.False(vm.RunDiagnostics);
        Assert.False(vm.Enable85PercentChecker);
        Assert.True(vm.OpenEditorBeforePrint);
        Assert.True(vm.IncludePrereleases);

        // The licence state and the machine's own flags survived the import.
        AppSettings after = AppSettings.Load();
        Assert.Equal(token, after.TrialToken);
        Assert.True(after.IntroSeen);
        Assert.Equal(lastSeenVersion, after.LastSeenVersion);
        Assert.True(after.IsDebugMode);
        Assert.Equal(templatePath, after.TemplatePath);
    }

    [AvaloniaFact]
    public void Import_FromANewerBuild_ShowsTheReason()
    {
        using var vm = new MainWindowViewModel();
        string path = Path.Combine(_profileDir, "newer.json");
        File.WriteAllText(path, """{"SchemaVersion":2}""");

        vm.PreviewShopProfileImport(path);

        Assert.False(vm.HasShopProfilePreview);
        Assert.Equal(LocalizationManager.GetString("Settings_ShopProfileErrorNewer"), vm.ShopProfileStatus);
        Assert.Equal("#ef4444", vm.ShopProfileStatusColor);
    }

    [AvaloniaFact]
    public void Import_OfSomethingElseEntirely_ShowsTheReason()
    {
        using var vm = new MainWindowViewModel();
        string path = Path.Combine(_profileDir, "garbage.json");
        File.WriteAllText(path, "not a profile at all");

        vm.PreviewShopProfileImport(path);

        Assert.False(vm.HasShopProfilePreview);
        Assert.Equal(LocalizationManager.GetString("Settings_ShopProfileErrorInvalid"), vm.ShopProfileStatus);
    }

    [AvaloniaFact]
    public void Import_WithNothingToChange_SaysSo()
    {
        using var vm = new MainWindowViewModel();
        string path = Path.Combine(_profileDir, "same.json");
        vm.ExportShopProfileTo(path);

        vm.PreviewShopProfileImport(path);

        Assert.False(vm.HasShopProfilePreview);
        Assert.Equal(LocalizationManager.GetString("Settings_ShopProfileNoChanges"), vm.ShopProfileStatus);
    }

    [AvaloniaFact]
    public void ThePreviewSaysEveryKeyItShows()
    {
        // Every row and every value has to come out of the dictionary. A key with
        // no wording would show up as its own name on the preview card, which is
        // exactly what an operator cannot read.
        IReadOnlyList<ShopProfileChange> changes =
            ShopProfileDiff.Compare(new AppSettings(), FullyLoadedProfile());

        Assert.Equal(31, changes.Count);

        foreach (ShopProfileChange change in changes)
        {
            Assert.NotEqual(change.LabelKey, LocalizationManager.GetString(change.LabelKey));

            foreach (string shown in new[] { change.OldDisplay, change.NewDisplay })
            {
                Assert.DoesNotContain("imported-key", shown);
                Assert.False(shown.StartsWith("Settings_", StringComparison.Ordinal),
                    $"{change.LabelKey} shows an untranslated value: {shown}");
                Assert.False(shown.StartsWith("Export_", StringComparison.Ordinal),
                    $"{change.LabelKey} shows an untranslated value: {shown}");
                Assert.False(shown.StartsWith("LabelStock_", StringComparison.Ordinal),
                    $"{change.LabelKey} shows an untranslated value: {shown}");
                Assert.False(shown.StartsWith("Payment_", StringComparison.Ordinal),
                    $"{change.LabelKey} shows an untranslated value: {shown}");
                Assert.False(shown.StartsWith("Quality_", StringComparison.Ordinal),
                    $"{change.LabelKey} shows an untranslated value: {shown}");
            }
        }
    }

    [AvaloniaFact]
    public void TheSection_OffersExportAndImport()
    {
        using MainWindow window = OpenSection();
        var vm = (MainWindowViewModel)window.DataContext!;

        Border card = CardHolding(window, vm.ExportShopProfileCommand);
        string text = Flatten(card);

        Assert.Contains(LocalizationManager.GetString("Settings_ShopProfileLead"), text);
        Assert.Contains(LocalizationManager.GetString("Settings_ShopProfileNote"), text);

        var buttons = Descendants(window).OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .ToList();

        Assert.Contains(buttons, button => ReferenceEquals(button.Command, vm.ExportShopProfileCommand));
        Assert.Contains(buttons, button => ReferenceEquals(button.Command, vm.ImportShopProfileCommand));
    }

    [AvaloniaFact]
    public void ThePreviewCard_ShowsTheChangesBeforeTheyAreApplied()
    {
        using MainWindow window = OpenSection();
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.PreviewShopProfileImport(WriteProfile(FullyLoadedProfile()));
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();

        string text = Flatten(window);
        Assert.Contains(LocalizationManager.GetString("Settings_ShopProfilePreviewTitle"), text);
        Assert.Contains(vm.ShopProfilePreviewSummary, text);

        var buttons = Descendants(window).OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .ToList();

        Assert.Contains(buttons, button => ReferenceEquals(button.Command, vm.ApplyShopProfileCommand));
        Assert.Contains(buttons, button => ReferenceEquals(button.Command, vm.CancelShopProfileImportCommand));

        // Nothing is written before Apply is pressed.
        AppSettings onDisk = AppSettings.Load();
        Assert.Equal("Dark", onDisk.Theme);
    }

    [AvaloniaFact]
    public void Preview_FollowsALanguageSwitch()
    {
        using var vm = new MainWindowViewModel();
        vm.PreviewShopProfileImport(WriteProfile(FullyLoadedProfile()));
        Assert.Contains("settings will be overwritten", vm.ShopProfilePreviewSummary);

        vm.Language = "Nederlands";
        Assert.Contains("instellingen worden overschreven", vm.ShopProfilePreviewSummary);

        vm.Language = "English";
        Assert.Contains("settings will be overwritten", vm.ShopProfilePreviewSummary);
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);

        LocalizationManager.SetLanguage("nl");

        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
        if (Directory.Exists(_profileDir)) Directory.Delete(_profileDir, true);
    }

    // ---- helpers ----

    private string WriteProfile(ShopProfile profile)
    {
        string path = Path.Combine(_profileDir, $"profile-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ShopProfileCodec.Serialize(profile));
        return path;
    }

    /// <summary>Every shareable field at a value other than its default.</summary>
    private static ShopProfile FullyLoadedProfile() => new()
    {
        SchemaVersion = ShopProfile.CurrentSchemaVersion,
        Theme = "Light",
        Language = "en",
        AutoActivate = true,
        AutoDetectOnPlug = true,
        AutoStartWebTest = true,
        ShowSummaryScreenAfterTesting = false,
        RequirePwaTest = false,
        AutoFinishAfterTest = true,
        EnableUsbEventMonitoring = false,
        ImeiInfoApiKey = "imported-key",
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
    };

    /// <summary>The window with the shop profile topic open and laid out.</summary>
    private static MainWindow OpenSection()
    {
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Theme = "Dark";
        window.Show();
        window.Width = 1200;
        window.Height = 1400;
        vm.IsSettingsDrawerOpen = true;
        vm.SelectedSettingsSection = "ShopProfile";
        window.UpdateLayout();
        for (int i = 0; i < 4; i++) AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        return window;
    }

    /// <summary>The settings card a given command's button sits in.</summary>
    private static Border CardHolding(Visual root, ICommand command)
    {
        var button = Descendants(root).OfType<Button>()
            .FirstOrDefault(candidate => candidate.IsEffectivelyVisible
                                         && ReferenceEquals(candidate.Command, command));
        Assert.NotNull(button);

        for (Visual? node = button; node is not null; node = node.GetVisualParent())
        {
            if (node is Border border && border.Classes.Contains("settingCard")) return border;
        }

        throw new InvalidOperationException("the button is not inside a settings card");
    }

    /// <summary>A card's words, one TextBlock per line.</summary>
    private static string Flatten(Visual root) =>
        string.Join("\n", Descendants(root).OfType<TextBlock>().Select(block => block.Text ?? ""));

    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root is null) yield break;
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var grand in Descendants(child)) yield return grand;
        }
    }
}
