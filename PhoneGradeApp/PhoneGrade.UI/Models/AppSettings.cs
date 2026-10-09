using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhoneGrade.Core;

namespace PhoneGrade.UI.Models;

/// <summary>App settings persisted to %LOCALAPPDATA%/PhoneGrade/settings.json.</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Dark";            // "Dark", "Light" or "System"
    public string Language { get; set; } = "nl";           // one of the codes in Services.SupportedLanguages
    public bool AutoActivate { get; set; } = false;
    public bool AutoDetectOnPlug { get; set; } = false;   // User must explicitly click to start inspecting
    public bool AutoStartWebTest { get; set; } = false;   // User must manually scan and run tests
    public bool ShowSummaryScreenAfterTesting { get; set; } = true;
    public bool RequirePwaTest { get; set; } = true;       // operator must complete PWA test before finishing
    public bool AutoFinishAfterTest { get; set; } = false; // automatically jump to summary screen after tests complete
    
    // IMEI.info BYOK API Configuration
    public string ImeiInfoApiKey { get; set; } = "";       // API key from imei.info dashboard
    public List<string> SelectedImeiChecks { get; set; } = new(); // Enabled check types (service codes)
    public int EstimatedAppleDevices { get; set; } = 10;   // For cost calculator
    public int EstimatedAndroidDevices { get; set; } = 10; // For cost calculator

    public bool RunDiagnostics { get; set; } = true;

    /// <summary>
    /// Whether a battery under the quality threshold carries the marker on the label.
    /// </summary>
    /// <remarks>
    /// The switch sits in the quality settings with the threshold it applies to. It is
    /// off for shops that grade every battery as it stands.
    /// </remarks>
    public bool Enable85PercentChecker { get; set; } = true;

    /// <summary>The battery percentage under which the marker goes on the label.</summary>
    /// <remarks>
    /// A shop norm rather than an app rule: what counts as a tired battery differs
    /// between a shop selling budget Android phones and one selling iPhones. The
    /// default is the percentage the label has always used.
    /// </remarks>
    public int LabelBatteryThreshold { get; set; } = LabelFields.LowBatteryPercent;

    /// <summary>
    /// Below this charge count the number is left off the label.
    /// </summary>
    /// <remarks>
    /// A percentage on its own is the number most likely to mislead: a battery at 90
    /// percent after nine hundred charges is worse than one at 80 after fifty. Below
    /// this floor the count says nothing and the paper is worth more to the rest of
    /// the label. The report keeps the real number either way.
    /// </remarks>
    public int LabelCyclesThreshold { get; set; } = 500;

    public bool OpenEditorBeforePrint { get; set; } = false;
    public string DefaultQuality { get; set; } = "";       // "", "A", "B", "C": empty asks

    /// <summary>
    /// The invoice method a finished inspection starts with: "", "Marge", "BTW", or
    /// <see cref="PaymentMethods.NeverAsk"/> for a shop that does not record one and
    /// does not want to be asked.
    /// </summary>
    public string DefaultPaymentMethod { get; set; } = "";
    public string? TemplatePath { get; set; }             // custom my.dymo override

    /// <summary>
    /// The DYMO part number of the roll in the printer, which is how a shop orders
    /// label stock and how the two are told apart: two rolls both called "address"
    /// are 89x28 and 89x36 and nothing about the name says which.
    /// </summary>
    public string LabelStockPartNumber { get; set; } = "1982991";

    /// <summary>
    /// What the label's barcode carries: the identifier alone, the identifier and
    /// the specification in one, or the two separately.
    /// </summary>
    public LabelBarcodeMode LabelBarcodeMode { get; set; } = LabelBarcodeMode.Identifier;

    /// <summary>
    /// Which symbology the label's barcode is drawn in.
    /// </summary>
    /// <remarks>
    /// Code39 by default, which is what every till in a phone shop reads and what the
    /// DYMO template that ships with the app declares. Code128 is denser, so it can
    /// carry the identifier and the specification in one code where Code39 cannot, and
    /// it carries lower case letters, so a model name goes on the code as the phone
    /// spelled it rather than as dashes.
    ///
    /// A scanner has to be told to read Code128, so this is asked rather than
    /// assumed: a barcode in a symbology the shop's own scanner does not read prints,
    /// looks like a barcode, and scans as nothing.
    /// </remarks>
    public LabelCodeSymbology LabelSymbology { get; set; } = LabelCodeSymbology.Code39;

    /// <summary>
    /// Whether the charge count is on the label at all. The threshold that decides
    /// when it is shown lives in the quality settings.
    /// </summary>
    public bool LabelShowBatteryCycles { get; set; } = true;

    /// <summary>
    /// How the label is arranged: the single specification line, the structured
    /// arrangement, or the grade as a block of its own.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LabelVariant LabelVariant { get; set; } = LabelVariant.Clean;

    /// <summary>
    /// Whether the label carries a barcode. On is one Code39 code with the serial
    /// number, which is what every till reads; off is for the shop whose scanner
    /// cannot read one at all.
    /// </summary>
    public bool LabelBarcodeEnabled { get; set; } = true;

    /// <summary>
    /// The files a finished inspection writes without being asked. The label and the
    /// report are the default; the numbers and the second label format are turned on
    /// by a shop that has a use for them.
    /// </summary>
    [JsonConverter(typeof(ExportFormatListConverter))]
    public List<ExportFormat> ExportFormats { get; set; } =
        [ExportFormat.DymoLabel, ExportFormat.ReportPdf];

    /// <summary>How the exports are grouped in the folder the operator sees.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExportFolderScheme ExportFolderScheme { get; set; } = ExportFolderScheme.Week;

    public bool EnableVerboseNetworkLogging { get; set; } = false; // Trace HTTP requests, CLI stdout/stderr, JSON payloads
    public bool IsDebugMode { get; set; } = false; // Global debug toggle for mobile PWA overlay and verbose tracing
    public bool EnableUsbEventMonitoring { get; set; } = true; // Use native USB event monitoring instead of polling

    /// <summary>
    /// Encrypted trial payload (scan count plus license key) as Base64, written
    /// by the licensing store. It lives here so that every ordinary settings
    /// save carries the current token along instead of overwriting it with a
    /// stale copy, and so a copied settings.json alone never reveals the count:
    /// the token only decrypts on the machine and account it was written for.
    /// </summary>
    public string TrialToken { get; set; } = "";

    /// <summary>
    /// Serves the PWA to the phone as http://localhost through an adb reverse
    /// tunnel instead of the LAN address. A browser only grants camera,
    /// microphone, motion and orientation access on a secure origin, and a plain
    /// http:// LAN address is not one, so over the network those four steps have
    /// nothing to run on. The tunnel needs the cable that is required anyway, so
    /// this is the default. Off means the phone is not served through adb
    /// reverse at all: the public https tunnel answers instead, and the LAN
    /// address is only what is left when that tunnel is switched off or cannot
    /// open.
    /// </summary>
    public bool UseSecureOrigin { get; set; } = true;

    /// <summary>
    /// Opens a public https address for a phone that cannot use the adb tunnel,
    /// which is every iPhone. Off keeps every address on the local network.
    /// </summary>
    public bool UsePublicTunnel { get; set; } = true;

    /// <summary>
    /// Whether the introduction screen has been dismissed. False on a fresh
    /// install, which is the only place it belongs: an operator who has already
    /// seen what the free tier is and where the paid plan lives should never be
    /// stopped by it again.
    /// </summary>
    public bool IntroSeen { get; set; } = false;

    /// <summary>
    /// Whether to be offered beta builds.
    ///
    /// Off unless somebody ticks it, and a shop that has ticked it says so here where
    /// the updater reads it, rather than the author pushing betas at whoever answers.
    /// A prerelease is a build nobody outside whoever wrote it has tested, and this is
    /// the only thing standing between that and a shop working a counter.
    ///
    /// Turning it off stops new betas being offered. It does not downgrade anybody: the
    /// next update is the newest stable, and there is no path back from inside the app,
    /// because that would need an installer moving someone backwards.
    /// </summary>
    public bool IncludePrereleases { get; set; } = false;

    /// <summary>
    /// The version whose changes were last put in front of the operator.
    ///
    /// The changelog used to open by itself when the updater left notes behind, and
    /// that was the only way it ever opened. Reading the notes from the release makes
    /// it a panel the operator can ask for, which needs a different question to ask
    /// "has this build already introduced itself". A version that differs from this
    /// one has, and that is the only thing that puts the panel up on its own.
    /// </summary>
    public string LastSeenVersion { get; set; } = "";

    /// <summary>
    /// Records that this version has introduced itself, and says whether it had not.
    /// </summary>
    public bool MarkVersionSeen(string? version)
    {
        string wanted = (version ?? "").Trim();
        if (wanted.Length == 0) return false;

        bool changed = !string.Equals(LastSeenVersion, wanted, StringComparison.OrdinalIgnoreCase);
        if (!changed) return false;

        LastSeenVersion = wanted;
        Save();
        return true;
    }

    [JsonIgnore]
    private static string SettingsDir =>
        Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneGrade");
    [JsonIgnore]
    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { /* corrupt settings → defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only dir / permission → keep running with in-memory settings */ }
    }
}

/// <summary>
/// Reads and writes the export format set as names rather than numbers.
/// </summary>
/// <remarks>
/// A settings file is a file a person may open, and a list of numbers is a list
/// nobody can check. Numbers are still read, because a file written by a build that
/// stored them is a file that has to keep loading: an enum member inserted in the
/// middle renumbers everything after it, and silently changing what a shop exports
/// over an app update is worse than any wording. The framework's own string enum
/// converter does not apply to a collection, which is why this exists.
/// </remarks>
public sealed class ExportFormatListConverter : JsonConverter<List<ExportFormat>>
{
    public override List<ExportFormat> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var formats = new List<ExportFormat>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return formats;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String
                && Enum.TryParse(reader.GetString(), ignoreCase: true, out ExportFormat named))
            {
                formats.Add(named);
            }
            else if (reader.TokenType == JsonTokenType.Number
                     && reader.TryGetInt32(out int number)
                     && Enum.IsDefined(typeof(ExportFormat), number))
            {
                formats.Add((ExportFormat)number);
            }
        }

        return formats;
    }

    public override void Write(
        Utf8JsonWriter writer, List<ExportFormat> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (ExportFormat format in value)
            writer.WriteStringValue(format.ToString());
        writer.WriteEndArray();
    }
}
