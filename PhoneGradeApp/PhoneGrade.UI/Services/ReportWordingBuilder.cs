using PhoneGrade.Core;

namespace PhoneGrade.UI.Services;

/// <summary>
/// The wording for the report PDFs, read out of the app's own dictionaries.
///
/// The reports are documents people keep: one gets filed, one gets shown to a
/// customer disputing a grade. Writing their headings in the language the code
/// was written in is how a Dutch shop hands a customer an English document, so
/// the wording is looked up in the same place the rest of the screen gets it.
/// </summary>
public static class ReportWordingBuilder
{
    /// <summary>The wording in the language the operator is working in.</summary>
    public static ReportWording Current() => For(LocalizationManager.CurrentLanguage);

    /// <summary>The wording for one language, falling back to English per field.</summary>
    public static ReportWording For(string languageCode) => languageCode == "en" ? English() : Dutch();

    private static ReportWording English() => Build(
        title: "Export_ReportTitle",
        generated: "Export_ReportGenerated",
        confidential: "Export_Confidential",
        verdict: "Export_Verdict",
        verdictClean: "Export_VerdictClean",
        device: "Export_SectionDevice",
        battery: "Export_SectionBattery",
        network: "Export_SectionNetwork",
        security: "Export_SectionSecurity",
        components: "Export_SectionComponents",
        tests: "Export_SectionTests",
        grading: "Export_SectionGrading",
        fieldName: "Export_FieldName",
        fieldValue: "Export_FieldValue",
        notAvailable: "Export_NotReported",
        unknown: "Export_Unknown",
        yes: "Export_Yes",
        no: "Export_No",
        locked: "Export_Locked",
        unlocked: "Export_Unlocked",
        labelLine: "Export_LabelLine",
        componentColumns: ["Export_ColComponent", "Export_ColRead", "Export_ColFactory", "Export_ColStatus", "Export_ColNotes"],
        testColumns: ["Export_ColTest", "Export_ColStatus", "Export_ColDuration", "Export_ColNotes"]);

    private static ReportWording Dutch() => Build(
        title: "Export_ReportTitle",
        generated: "Export_ReportGenerated",
        confidential: "Export_Confidential",
        verdict: "Export_Verdict",
        verdictClean: "Export_VerdictClean",
        device: "Export_SectionDevice",
        battery: "Export_SectionBattery",
        network: "Export_SectionNetwork",
        security: "Export_SectionSecurity",
        components: "Export_SectionComponents",
        tests: "Export_SectionTests",
        grading: "Export_SectionGrading",
        fieldName: "Export_FieldName",
        fieldValue: "Export_FieldValue",
        notAvailable: "Export_NotReported",
        unknown: "Export_Unknown",
        yes: "Export_Yes",
        no: "Export_No",
        locked: "Export_Locked",
        unlocked: "Export_Unlocked",
        labelLine: "Export_LabelLine",
        componentColumns: ["Export_ColComponent", "Export_ColRead", "Export_ColFactory", "Export_ColStatus", "Export_ColNotes"],
        testColumns: ["Export_ColTest", "Export_ColStatus", "Export_ColDuration", "Export_ColNotes"]);

    /// <summary>
    /// Builds the record out of the dictionary, taking the English value for any
    /// key that is missing so that one absent string does not blank a heading.
    /// </summary>
    private static ReportWording Build(
        string title, string generated, string confidential, string verdict, string verdictClean,
        string device, string battery, string network, string security, string components,
        string tests, string grading, string fieldName, string fieldValue, string notAvailable,
        string unknown, string yes, string no, string locked, string unlocked, string labelLine,
        string[] componentColumns, string[] testColumns) => new()
    {
        Title = Say(title, "Inspection report"),
        Generated = Say(generated, "Generated"),
        Confidential = Say(confidential, "Confidential"),
        Verdict = Say(verdict, "Verdict"),
        VerdictClean = Say(verdictClean, "No faults found"),
        SectionDevice = Say(device, "Device"),
        SectionBattery = Say(battery, "Battery"),
        SectionNetwork = Say(network, "Network"),
        SectionSecurity = Say(security, "Locks and security"),
        SectionComponents = Say(components, "Component originals"),
        SectionTests = Say(tests, "Interactive tests"),
        SectionGrading = Say(grading, "Grading"),
        FieldName = Say(fieldName, "Field"),
        FieldValue = Say(fieldValue, "Value"),
        NotAvailable = Say(notAvailable, "Not reported"),
        Unknown = Say(unknown, "Unknown"),
        Yes = Say(yes, "Yes"),
        No = Say(no, "No"),
        Locked = Say(locked, "Locked"),
        Unlocked = Say(unlocked, "Unlocked"),
        LabelLine = Say(labelLine, "Label"),
        ComponentColumns = [.. componentColumns.Select(key => Say(key, "Column"))],
        TestColumns = [.. testColumns.Select(key => Say(key, "Column"))],
    };

    /// <summary>
    /// Reads one key, and takes the fallback when the dictionary does not carry it.
    /// A dictionary entry that has been forgotten shows as its own key today, which
    /// is worse than English in a document.
    /// </summary>
    private static string Say(string key, string fallback)
    {
        string value = LocalizationManager.GetString(key);
        return value == key || value.Length == 0 ? fallback : value;
    }
}