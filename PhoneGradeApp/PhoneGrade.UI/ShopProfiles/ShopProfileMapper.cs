using System;
using System.Collections.Generic;
using PhoneGrade.Core;
using PhoneGrade.UI.Models;

namespace PhoneGrade.UI.ShopProfiles;

/// <summary>
/// Builds the shareable profile out of the settings of this computer.
///
/// The mapping is written out field by field rather than by copying the whole
/// settings object and deleting the private parts. A denylist would leak the
/// first field somebody adds later and forgets to name here; an allowlist leaks
/// nothing, and a new setting simply does not travel until somebody decides it
/// should. The test next to this class pins the complete property set of the
/// file, so that decision cannot be made by accident either.
/// </summary>
public static class ShopProfileMapper
{
    public static ShopProfile FromSettings(AppSettings settings, string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ShopProfile
        {
            SchemaVersion = ShopProfile.CurrentSchemaVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            AppVersion = appVersion,

            Theme = settings.Theme,
            Language = settings.Language,

            AutoActivate = settings.AutoActivate,
            AutoDetectOnPlug = settings.AutoDetectOnPlug,
            AutoStartWebTest = settings.AutoStartWebTest,
            ShowSummaryScreenAfterTesting = settings.ShowSummaryScreenAfterTesting,
            RequirePwaTest = settings.RequirePwaTest,
            AutoFinishAfterTest = settings.AutoFinishAfterTest,
            EnableUsbEventMonitoring = settings.EnableUsbEventMonitoring,

            ImeiInfoApiKey = settings.ImeiInfoApiKey,
            SelectedImeiChecks = settings.SelectedImeiChecks is null
                ? null
                : new List<string>(settings.SelectedImeiChecks),
            EstimatedAppleDevices = settings.EstimatedAppleDevices,
            EstimatedAndroidDevices = settings.EstimatedAndroidDevices,

            DefaultQuality = settings.DefaultQuality,
            DefaultPaymentMethod = settings.DefaultPaymentMethod,

            LabelStockPartNumber = settings.LabelStockPartNumber,
            LabelBarcodeMode = settings.LabelBarcodeMode,
            LabelSymbology = settings.LabelSymbology,
            LabelBarcodeEnabled = settings.LabelBarcodeEnabled,
            LabelVariant = settings.LabelVariant,
            LabelShowBatteryCycles = settings.LabelShowBatteryCycles,
            LabelBatteryThreshold = settings.LabelBatteryThreshold,
            LabelCyclesThreshold = settings.LabelCyclesThreshold,

            ExportFormats = settings.ExportFormats is null
                ? null
                : new List<ExportFormat>(settings.ExportFormats),
            ExportFolderScheme = settings.ExportFolderScheme,

            UseSecureOrigin = settings.UseSecureOrigin,
            UsePublicTunnel = settings.UsePublicTunnel,

            RunDiagnostics = settings.RunDiagnostics,
            Enable85PercentChecker = settings.Enable85PercentChecker,
            OpenEditorBeforePrint = settings.OpenEditorBeforePrint,
            IncludePrereleases = settings.IncludePrereleases,
        };
    }
}
