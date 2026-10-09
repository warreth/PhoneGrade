using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhoneGrade.Core;
using PhoneGrade.UI.Models;

namespace PhoneGrade.UI.ShopProfiles;

/// <summary>
/// The shareable half of the settings: what a shop wants every computer behind
/// its counter to answer the same way.
///
/// A profile is not a backup. Everything that belongs to one computer rather
/// than to the shop is deliberately absent:
///
/// - the encrypted trial and licence token, which only decrypts on the machine
///   it was written for and would burn a seat if it travelled;
/// - <c>IntroSeen</c> and <c>LastSeenVersion</c>, which are about what this
///   install has already shown its operator;
/// - the debug switches, which support turns on for one machine at a time;
/// - <c>TemplatePath</c>, an absolute path to a .dymo file. A configured path
///   that is missing fails the export on purpose, so copying one computer's
///   path to another would break label printing there rather than fall back.
///
/// Every property is nullable, and null means "this profile does not carry this
/// setting, leave the machine alone". That is what makes a hand-edited file and
/// a file from an older build safe to import: they simply do not speak about
/// the settings they do not mention, instead of resetting them to defaults.
/// </summary>
public sealed class ShopProfile
{
    /// <summary>The version this build writes, and the highest it will import.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The file name offered when a profile is exported.</summary>
    public const string SuggestedFileName = "phonegrade-shop-profile.json";

    /// <summary>Which layout the rest of the file follows. Required on import.</summary>
    public int? SchemaVersion { get; set; }

    /// <summary>When the file was written, for support rather than for the app.</summary>
    public DateTimeOffset? ExportedAtUtc { get; set; }

    /// <summary>The PhoneGrade version that wrote the file, informational only.</summary>
    public string? AppVersion { get; set; }

    // ============ General ============

    public string? Theme { get; set; }

    /// <summary>The language code, as the settings store it, not the shown name.</summary>
    public string? Language { get; set; }

    // ============ Workflow ============

    public bool? AutoActivate { get; set; }
    public bool? AutoDetectOnPlug { get; set; }
    public bool? AutoStartWebTest { get; set; }
    public bool? ShowSummaryScreenAfterTesting { get; set; }
    public bool? RequirePwaTest { get; set; }
    public bool? AutoFinishAfterTest { get; set; }
    public bool? EnableUsbEventMonitoring { get; set; }

    // ============ IMEI.info BYOK ============

    public string? ImeiInfoApiKey { get; set; }
    public List<string>? SelectedImeiChecks { get; set; }
    public int? EstimatedAppleDevices { get; set; }
    public int? EstimatedAndroidDevices { get; set; }

    // ============ Defaults for an inspection ============

    public string? DefaultQuality { get; set; }
    public string? DefaultPaymentMethod { get; set; }

    // ============ What the label says ============

    public string? LabelStockPartNumber { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LabelBarcodeMode? LabelBarcodeMode { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LabelCodeSymbology? LabelSymbology { get; set; }

    /// <summary>Whether the label carries a barcode at all.</summary>
    public bool? LabelBarcodeEnabled { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LabelVariant? LabelVariant { get; set; }

    public bool? LabelShowBatteryCycles { get; set; }

    /// <summary>The percentage under which a battery carries the marker.</summary>
    public int? LabelBatteryThreshold { get; set; }

    /// <summary>Below this charge count the number is left off the label.</summary>
    public int? LabelCyclesThreshold { get; set; }

    /// <summary>The files a finished inspection writes without being asked.</summary>
    [JsonConverter(typeof(ExportFormatListConverter))]
    public List<ExportFormat>? ExportFormats { get; set; }

    /// <summary>How the exports are grouped in the folder the operator sees.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExportFolderScheme? ExportFolderScheme { get; set; }

    // ============ Connection ============

    public bool? UseSecureOrigin { get; set; }
    public bool? UsePublicTunnel { get; set; }

    // ============ Advanced and support ============

    public bool? RunDiagnostics { get; set; }
    public bool? Enable85PercentChecker { get; set; }
    public bool? OpenEditorBeforePrint { get; set; }
    public bool? IncludePrereleases { get; set; }
}
