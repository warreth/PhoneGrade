using System.Globalization;

namespace PhoneGrade.Core;

/// <summary>
/// Where exports go, and the old single format entry points.
///
/// The one class an operator's whole export story used to live in. It wrote a CSV
/// and a PDF with hardcoded English headings, from two independent methods, with
/// no way to ask for both, and the label was a third thing in another class with
/// its own idea of where files belong. Everything now goes through
/// <see cref="LabelWriter"/>, which writes the set that was asked for and reports
/// each file separately. What is left here is the folder and the thin wrappers,
/// so a caller that only wants one format does not have to build a set.
/// </summary>
public static class ExportService
{
    /// <summary>
    /// Where exports land unless the operator says otherwise. Per user, so it is
    /// writable on all three platforms and does not need an installer to have run
    /// as an administrator first.
    /// </summary>
    public static string ExportDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhoneGrade", "exports");

    /// <summary>Opens a folder in the file manager.</summary>
    public static PrintService.Attempt OpenExportFolder() => PrintService.OpenFolder(ExportDir);

    /// <summary>
    /// Whether a path is a template this app can fill. Used by the settings screen
    /// to reject a file that is not a template before it becomes the one every
    /// label is written from.
    /// </summary>
    public static bool IsUsableTemplate(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            DymoTemplate.Fill(File.ReadAllText(path), LabelFields.From(new DeviceData()));
            return true;
        }
        catch (Exception)
        {
            // Unreadable, or a template with no fields in it. Both are worth
            // refusing at the moment of choosing rather than at the moment of
            // printing.
            return false;
        }
    }

    /// <summary>
    /// Writes one file. Kept as a method because one format is one line at the call
    /// site this way, and a caller that wants three asks <see cref="LabelWriter"/>
    /// for three so that one failure cannot hide the others.
    /// </summary>
    public static async Task<LabelWriter.Batch> ExportAsync(
        DeviceData data, ExportFormat format, string? folder = null, string? fileName = null,
        CancellationToken cancellation = default)
    {
        try
        {
            return await LabelWriter.WriteAsync(
                data, LabelWriter.Request.One(format, folder, fileName), cancellation)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A folder that cannot be created is a failure of the whole batch, and
            // it is reported as a failure of the one format that was asked for
            // rather than as an empty success.
            return new LabelWriter.Batch(
                [new LabelWriter.Outcome(format, null, false, ex.Message)], folder ?? ExportDir);
        }
    }

    /// <summary>
    /// The single line that names an export in a status bar. The path is in it
    /// because an operator who cannot find the file cannot hand it over.
    /// </summary>
    public static string Describe(LabelWriter.Outcome outcome, string folder)
    {
        if (!outcome.Succeeded)
            return $"{Name(outcome.Format)} failed: {outcome.Problem}";

        string full = outcome.Path!;
        // The name on its own when the file went where the panel said it would,
        // and the whole path when it did not, so the operator is never left
        // hunting for a file that is not where they were told.
        string where = Path.GetDirectoryName(full) == folder ? Path.GetFileName(full) : full;
        return outcome.Note is { Length: > 0 }
            ? $"{Name(outcome.Format)} saved as {where}. {outcome.Note}"
            : $"{Name(outcome.Format)} saved as {where}";
    }

    /// <summary>
    /// The name of a format in English. The UI replaces this with the wording from
    /// its own dictionaries; this is the fallback for a caller with no UI.
    /// </summary>
    internal static string Name(ExportFormat format) => format switch
    {
        ExportFormat.DymoLabel => "DYMO label",
        ExportFormat.LabelPdf => "Label PDF",
        ExportFormat.ReportPdf => "Report PDF",
        ExportFormat.Json => "JSON",
        ExportFormat.Csv => "CSV",
        _ => format.ToString(),
    };

    /// <summary>
    /// The formats a one-click export writes: the label for the printer in front of
    /// the operator, the label as a PDF for the printer that is not a DYMO, and the
    /// numbers for the shop's own records. Three files, one click, and every one of
    /// them written whether or not a printer was found.
    /// </summary>
    public static IReadOnlySet<ExportFormat> DefaultSet { get; } = new HashSet<ExportFormat>
    {
        ExportFormat.DymoLabel,
        ExportFormat.LabelPdf,
        ExportFormat.Json,
    };

    /// <summary>The formats offered in the export panel, in the order they are listed.</summary>
    public static IReadOnlyList<ExportFormat> All { get; } =
    [
        ExportFormat.DymoLabel,
        ExportFormat.LabelPdf,
        ExportFormat.ReportPdf,
        ExportFormat.Json,
        ExportFormat.Csv,
    ];

    /// <summary>
    /// The files already in the export folder for one device, newest first. The
    /// export panel offers these for printing again without writing them twice.
    /// </summary>
    public static IReadOnlyList<ExportFormat> Existing(string? stem, string? folder = null)
    {
        string dir = folder ?? ExportDir;
        if (stem is not { Length: > 0 } || !Directory.Exists(dir)) return [];

        var found = new List<ExportFormat>();
        foreach (ExportFormat format in All)
            if (File.Exists(Path.Combine(dir, $"{stem}{format.FileSuffix()}")))
                found.Add(format);
        return found;
    }

    /// <summary>
    /// Today, as a file name stamp. Local rather than UTC on purpose: an export is
    /// filed against the day it was taken, and a shop working at 23:00 expects
    /// that file to say today rather than tomorrow.
    /// </summary>
    internal static string Stamp(DateTime moment) =>
        moment.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}