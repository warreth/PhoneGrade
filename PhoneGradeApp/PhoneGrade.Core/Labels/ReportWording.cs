namespace PhoneGrade.Core;

/// <summary>
/// Every sentence the report PDFs contain, in one language at a time.
///
/// A report is read by people: a customer disputing a grade, a bookkeeper filing
/// a purchase. Writing the headings in the language the code was written in is
/// how a Dutch shop ends up handing customers an English document. The view model
/// builds the right one from the app's own dictionaries and hands it in, so this
/// layer never needs to know which languages exist.
/// </summary>
public sealed record ReportWording
{
    public required string Title { get; init; }
    public required string Generated { get; init; }
    public required string Confidential { get; init; }
    public required string Verdict { get; init; }
    public required string VerdictClean { get; init; }
    public required string SectionDevice { get; init; }
    public required string SectionBattery { get; init; }
    public required string SectionNetwork { get; init; }
    public required string SectionSecurity { get; init; }
    public required string SectionComponents { get; init; }
    public required string SectionTests { get; init; }
    public required string SectionGrading { get; init; }
    public required string FieldName { get; init; }
    public required string FieldValue { get; init; }
    public required string NotAvailable { get; init; }
    public required string Unknown { get; init; }
    public required string Yes { get; init; }
    public required string No { get; init; }
    public required string Locked { get; init; }
    public required string Unlocked { get; init; }
    public required string LabelLine { get; init; }

    /// <summary>Column headings for the component table.</summary>
    public required string[] ComponentColumns { get; init; }

    /// <summary>Column headings for the test table.</summary>
    public required string[] TestColumns { get; init; }

    /// <summary>
    /// The English set. Used when no wording was handed in, so a caller that
    /// forgets still gets a readable document rather than an exception.
    /// </summary>
    public static ReportWording English { get; } = new()
    {
        Title = "Inspection report",
        Generated = "Generated",
        Confidential = "Confidential",
        Verdict = "Verdict",
        VerdictClean = "No faults found",
        SectionDevice = "Device",
        SectionBattery = "Battery",
        SectionNetwork = "Network",
        SectionSecurity = "Locks and security",
        SectionComponents = "Component originals",
        SectionTests = "Interactive tests",
        SectionGrading = "Grading",
        FieldName = "Field",
        FieldValue = "Value",
        NotAvailable = "Not reported",
        Unknown = "Unknown",
        Yes = "Yes",
        No = "No",
        Locked = "Locked",
        Unlocked = "Unlocked",
        LabelLine = "Label",
        ComponentColumns = ["Component", "Read", "Factory", "Status", "Notes"],
        TestColumns = ["Test", "Status", "Duration", "Notes"],
    };
}