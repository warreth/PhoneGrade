using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhoneGrade.Core;
using PhoneGrade.UI.Models;
using PhoneGrade.UI.Services;

namespace PhoneGrade.UI.ShopProfiles;

/// <summary>
/// One row of the import preview: which setting changes, and what it changes
/// from and to, already said in the operator's language.
/// </summary>
public sealed record ShopProfileChange(string LabelKey, string OldDisplay, string NewDisplay);

/// <summary>
/// Compares a profile with the settings of this computer, so an import can show
/// what it is about to overwrite before anything is written.
///
/// Only settings the profile actually carries are compared, and only when they
/// differ: the preview is the list of changes, not a copy of the whole file.
/// The API key is never shown, only whether one is set, because the preview is
/// read by whoever is standing at the bench.
/// </summary>
public static class ShopProfileDiff
{
    /// <summary>
    /// The changes, in the order the settings are found in the app rather than
    /// in the file, so the same profile always reads the same way.
    /// <paramref name="localize"/> exists so tests can assert the exact wording
    /// without a resource dictionary; the app passes nothing and gets the live
    /// one.
    /// </summary>
    public static IReadOnlyList<ShopProfileChange> Compare(
        AppSettings current, ShopProfile incoming, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        Func<string, string> say = localize ?? LocalizationManager.GetString;
        var changes = new List<ShopProfileChange>();

        void Bool(string labelKey, bool currentValue, bool? incomingValue)
        {
            if (incomingValue is { } wanted && wanted != currentValue)
                changes.Add(new ShopProfileChange(labelKey, OnOff(say, currentValue), OnOff(say, wanted)));
        }

        void Number(string labelKey, int currentValue, int? incomingValue)
        {
            if (incomingValue is { } wanted && wanted != currentValue)
                changes.Add(new ShopProfileChange(
                    labelKey,
                    currentValue.ToString(CultureInfo.CurrentCulture),
                    wanted.ToString(CultureInfo.CurrentCulture)));
        }

        void Text(string labelKey, string currentValue, string? incomingValue, Func<string, string>? display = null)
        {
            if (incomingValue is null) return;
            Func<string, string> show = display ?? (value => value);
            string oldShown = show(currentValue);
            string newShown = show(incomingValue);
            if (!string.Equals(oldShown, newShown, StringComparison.Ordinal))
                changes.Add(new ShopProfileChange(labelKey, oldShown, newShown));
        }

        // General
        Text("Settings_Language", current.Language, incoming.Language, code => SupportedLanguages.NameOf(code));
        Text("Settings_Theme", current.Theme, incoming.Theme);

        // Workflow
        Bool("Settings_AutoDetect", current.AutoDetectOnPlug, incoming.AutoDetectOnPlug);
        Bool("Settings_AutoStartWebTest", current.AutoStartWebTest, incoming.AutoStartWebTest);
        Bool("Settings_ShowSummary", current.ShowSummaryScreenAfterTesting, incoming.ShowSummaryScreenAfterTesting);
        Bool("Settings_RequirePwaTest", current.RequirePwaTest, incoming.RequirePwaTest);
        Bool("Settings_AutoFinishAfterTest", current.AutoFinishAfterTest, incoming.AutoFinishAfterTest);
        Bool("Settings_UsbEventMonitoring", current.EnableUsbEventMonitoring, incoming.EnableUsbEventMonitoring);
        Bool("Settings_AutoActivate", current.AutoActivate, incoming.AutoActivate);

        // IMEI.info
        if (incoming.ImeiInfoApiKey is { } key)
        {
            if (!string.Equals(current.ImeiInfoApiKey ?? "", key, StringComparison.Ordinal))
                changes.Add(new ShopProfileChange(
                    "Settings_ImeiInfoApiKey",
                    Secret(say, current.ImeiInfoApiKey),
                    Secret(say, key)));
        }

        if (incoming.SelectedImeiChecks is { } checks)
        {
            var currentChecks = current.SelectedImeiChecks ?? new List<string>();
            if (!currentChecks.ToHashSet().SetEquals(checks))
                changes.Add(new ShopProfileChange(
                    "Settings_ImeiInfoChecks",
                    CheckNames(say, currentChecks),
                    CheckNames(say, checks)));
        }

        Number("Settings_ImeiInfoAppleDevices", current.EstimatedAppleDevices, incoming.EstimatedAppleDevices);
        Number("Settings_ImeiInfoAndroidDevices", current.EstimatedAndroidDevices, incoming.EstimatedAndroidDevices);

        // Inspection defaults
        Text("Settings_Quality", current.DefaultQuality, incoming.DefaultQuality, value =>
            value.Length == 0 ? say("Settings_QualityAsk") : value);
        Text("Settings_Payment", current.DefaultPaymentMethod, incoming.DefaultPaymentMethod, value => value switch
        {
            "" => say("Settings_PaymentAsk"),
            PaymentMethods.NeverAsk => say("Settings_PaymentNever"),
            "Marge" => say("Payment_Marge"),
            "BTW" => say("Payment_BTW"),
            _ => value,
        });

        // What the label says
        Text("Export_Stock", current.LabelStockPartNumber, incoming.LabelStockPartNumber, part =>
        {
            string normalized = LabelStock.FromPartNumber(part).PartNumber;
            string key = "LabelStock_" + normalized;
            string shown = say(key);
            return shown == key ? normalized : shown;
        });

        if (incoming.LabelBarcodeMode is { } barcodeMode && barcodeMode != current.LabelBarcodeMode)
            changes.Add(new ShopProfileChange(
                "Export_BarcodeTitle",
                BarcodeName(say, current.LabelBarcodeMode),
                BarcodeName(say, barcodeMode)));

        if (incoming.LabelSymbology is { } symbology && symbology != current.LabelSymbology)
            changes.Add(new ShopProfileChange(
                "Export_SymbologyTitle",
                SymbologyName(say, current.LabelSymbology),
                SymbologyName(say, symbology)));

        if (incoming.LabelVariant is { } variant && variant != current.LabelVariant)
            changes.Add(new ShopProfileChange(
                "Settings_LabelVariant",
                VariantName(say, current.LabelVariant),
                VariantName(say, variant)));

        Bool("Settings_LabelBarcode", current.LabelBarcodeEnabled, incoming.LabelBarcodeEnabled);
        Bool("Export_LabelShowCycles", current.LabelShowBatteryCycles, incoming.LabelShowBatteryCycles);
        Number("Settings_QualityBatteryThreshold", current.LabelBatteryThreshold, incoming.LabelBatteryThreshold);
        Number("Settings_QualityCyclesThreshold", current.LabelCyclesThreshold, incoming.LabelCyclesThreshold);

        if (incoming.ExportFormats is { } formats)
        {
            var currentFormats = current.ExportFormats ?? new List<ExportFormat>();
            if (!currentFormats.ToHashSet().SetEquals(formats))
                changes.Add(new ShopProfileChange(
                    "Export_FormatsTitle",
                    FormatNames(say, currentFormats),
                    FormatNames(say, formats)));
        }

        if (incoming.ExportFolderScheme is { } scheme && scheme != current.ExportFolderScheme)
            changes.Add(new ShopProfileChange(
                "Settings_LabelFolder",
                FolderName(say, current.ExportFolderScheme),
                FolderName(say, scheme)));

        // Connection
        Bool("Settings_SecureOrigin", current.UseSecureOrigin, incoming.UseSecureOrigin);
        Bool("Settings_PublicTunnel", current.UsePublicTunnel, incoming.UsePublicTunnel);

        // Advanced and support
        Bool("Settings_RunDiagnostics", current.RunDiagnostics, incoming.RunDiagnostics);
        Bool("Settings_Enable85Checker", current.Enable85PercentChecker, incoming.Enable85PercentChecker);
        Bool("Settings_OpenEditor", current.OpenEditorBeforePrint, incoming.OpenEditorBeforePrint);
        Bool("Settings_IncludePrereleases", current.IncludePrereleases, incoming.IncludePrereleases);

        return changes;
    }

    private static string OnOff(Func<string, string> say, bool value) =>
        say(value ? "Settings_ShopProfileValueOn" : "Settings_ShopProfileValueOff");

    private static string Secret(Func<string, string> say, string? key) =>
        say(string.IsNullOrEmpty(key) ? "Settings_ShopProfileValueNotSet" : "Settings_ShopProfileValueSet");

    private static string BarcodeName(Func<string, string> say, LabelBarcodeMode mode) => say(mode switch
    {
        LabelBarcodeMode.None => "Export_BarcodeNone",
        LabelBarcodeMode.Combined => "Export_BarcodeCombined",
        LabelBarcodeMode.Split => "Export_BarcodeSplit",
        _ => "Export_BarcodeIdentifier",
    });

    private static string SymbologyName(Func<string, string> say, LabelCodeSymbology symbology) =>
        say(symbology == LabelCodeSymbology.Code39 ? "Export_Symbology39" : "Export_Symbology128");

    private static string VariantName(Func<string, string> say, LabelVariant variant) => say(variant switch
    {
        LabelVariant.Structured => "Settings_LabelVariantStructured",
        LabelVariant.GradeBlock => "Settings_LabelVariantGradeBlock",
        _ => "Settings_LabelVariantClean",
    });

    /// <summary>The written files by name, because a set is what a shop recognises.</summary>
    private static string FormatNames(Func<string, string> say, IReadOnlyList<ExportFormat> formats)
    {
        if (formats.Count == 0) return say("Settings_ShopProfileValueNone");
        return string.Join(", ", formats.Select(format => say(FormatNameKey(format))));
    }

    private static string FormatNameKey(ExportFormat format) => format switch
    {
        ExportFormat.LabelPdf => "Export_FormatLabelPdf",
        ExportFormat.ReportPdf => "Export_FormatReportPdf",
        ExportFormat.Json => "Export_FormatJson",
        ExportFormat.Csv => "Export_FormatCsv",
        _ => "Export_FormatDymo",
    };

    private static string FolderName(Func<string, string> say, ExportFolderScheme scheme) => say(scheme switch
    {
        ExportFolderScheme.Day => "Settings_FolderDay",
        ExportFolderScheme.Month => "Settings_FolderMonth",
        ExportFolderScheme.Inspection => "Settings_FolderInspection",
        _ => "Settings_FolderWeek",
    });

    /// <summary>
    /// The selected checks by name, because a count alone would call two
    /// different sets of checks "2 to 2" and show no change at all.
    /// </summary>
    private static string CheckNames(Func<string, string> say, IReadOnlyList<string> codes)
    {
        if (codes.Count == 0) return say("Settings_ShopProfileValueNone");

        return string.Join(", ", codes.Select(code =>
        {
            string key = "Settings_ImeiCheck_" + (code switch
            {
                "apple_carrier_lock_fmi" => "AppleCarrierLockFmi",
                "blacklist_simple" => "BlacklistSimple",
                "blacklist_premium" => "BlacklistPremium",
                "samsung_info_knox" => "SamsungInfoKnox",
                _ => "",
            });

            if (key == "Settings_ImeiCheck_") return code;
            string shown = say(key);
            return shown == key ? code : shown;
        }));
    }
}
