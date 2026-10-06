namespace PhoneGrade.Core;

/// <summary>
/// The four files a finished inspection can leave behind: a label for a DYMO
/// printer, a label as a PDF for anything else, a full report as a PDF, and the
/// raw numbers as JSON.
///
/// One entry point writes all of them and reports each one separately, because
/// they fail for unrelated reasons. A DYMO file is written whether or not a
/// printer exists, a PDF needs fonts it might not find, and JSON cannot fail at
/// all. An export that reported one answer for all of them would say "done" over
/// a report that was never written.
/// </summary>
public static class LabelWriter
{
    /// <summary>What one of the files came out as.</summary>
    /// <param name="Format">Which file this is about.</param>
    /// <param name="Path">Where it was written, when it was written.</param>
    /// <param name="Succeeded">Whether it was written.</param>
    /// <param name="Problem">Why not, in a sentence an operator can act on.</param>
    /// <param name="Note">
    /// Something the operator should know that is not a failure: a template field
    /// nothing filled, a barcode that had to be changed.
    /// </param>
    public sealed record Outcome(ExportFormat Format, string? Path, bool Succeeded, string? Problem, string? Note = null);

    /// <summary>The set of files written, in the order they were asked for.</summary>
    public sealed record Batch(IReadOnlyList<Outcome> Files, string Folder)
    {
        /// <summary>True when nothing failed. Notes do not make a batch a failure.</summary>
        public bool Succeeded => Files.All(file => file.Succeeded);

        /// <summary>The ones that went wrong, for the line under the buttons.</summary>
        public IEnumerable<Outcome> Failures => Files.Where(file => !file.Succeeded);
    }

    /// <summary>What to write, and where.</summary>
    /// <param name="Formats">Which files to write. An empty set writes nothing.</param>
    /// <param name="Folder">
    /// Where they go. Empty means the default exports folder, which is per user
    /// and therefore the same place on all three operating systems.
    /// </param>
    /// <param name="FileName">
    /// The stem every file shares, so the four of them sit next to each other and
    /// an operator can see they belong to one inspection.
    /// </param>
    /// <param name="TemplatePath">A .dymo template to use instead of the shipped one.</param>
    /// <param name="Layout">The label stock size.</param>
    /// <param name="FlagLowBattery">Whether a battery under 85 percent carries the marker.</param>
    /// <param name="Wording">
    /// The wording for the report PDF. A report is read by people, so it is
    /// written in the language the operator is working in rather than the
    /// language the code was written in.
    /// </param>
    /// <param name="Messages">
    /// The sentences the panel shows next to each file: why one did not arrive,
    /// what was noticed about one that did. Same reasoning as the report wording,
    /// because these are read by the same person at the same moment.
    /// </param>
    public sealed record Request(
        IReadOnlyCollection<ExportFormat> Formats,
        string? Folder = null,
        string? FileName = null,
        string? TemplatePath = null,
        LabelLayout? Layout = null,
        LabelBarcodeMode Barcode = LabelBarcodeMode.Identifier,
        LabelCodeSymbology? Symbology = null,
        bool FlagLowBattery = true,
        LabelContent? Content = null,
        ReportWording? Wording = null,
        ExportWording? Messages = null)
    {
        public static Request One(ExportFormat format, string? folder = null, string? fileName = null) =>
            new([format], folder, fileName);
    }

    /// <summary>
    /// Writes everything the request asks for. Each file is written on its own so
    /// that one failure does not take the rest with it, and the result says which
    /// is which.
    /// </summary>
    public static async Task<Batch> WriteAsync(DeviceData data, Request request, CancellationToken cancellation = default)
    {
        LabelFields fields = LabelFields.From(
            data, request.FlagLowBattery, content: request.Content);
        LabelLayout layout = request.Layout ?? LabelLayout.Address;

        string folder = request.Folder is { Length: > 0 } chosen ? chosen : ExportService.ExportDir;
        Directory.CreateDirectory(folder);

        string stem = Sanitize(request.FileName ?? FileStem(data));
        var files = new List<Outcome>();

        var wanted = new HashSet<ExportFormat>(request.Formats);
        foreach (ExportFormat format in Order(wanted))
        {
            cancellation.ThrowIfCancellationRequested();
            files.Add(await WriteOneAsync(format, data, fields, layout, folder, stem, request)
                .ConfigureAwait(false));
        }

        return new Batch(files, folder);
    }

    /// <summary>
    /// The order the files are written in. The label comes first because it is
    /// the one the operator is standing in front of the printer waiting for, and
    /// the JSON comes last because it is the one nobody waits for.
    /// </summary>
    private static IEnumerable<ExportFormat> Order(ISet<ExportFormat> formats)
    {
        ExportFormat[] order =
        [
            ExportFormat.DymoLabel,
            ExportFormat.LabelPdf,
            ExportFormat.ReportPdf,
            ExportFormat.Json,
            ExportFormat.Csv,
        ];
        return order.Where(formats.Contains);
    }

    private static async Task<Outcome> WriteOneAsync(
        ExportFormat format, DeviceData data, LabelFields fields, LabelLayout layout,
        string folder, string stem, Request request)
    {
        ExportWording messages = request.Messages ?? ExportWording.English;

        // The label PDF is drawn here, so it uses the symbology the operator chose.
        // The .dymo file cannot: DYMO draws that file rather than this app, so its
        // barcode objects keep the symbology the template declares.
        LabelCodeSymbology drawn = request.Symbology ?? LabelCodeSymbology.Code39;

        // The two PDFs share a stem and must not share a file name: a label and a
        // full report are different documents, and one of them overwriting the
        // other is how an operator ends up filing a receipt-sized label as the
        // inspection record.
        string path = Path.Combine(folder, $"{stem}{format.FileSuffix()}");
        try
        {
            return format switch
            {
                ExportFormat.DymoLabel => DymoLabel(path, fields, layout, request.Barcode, request.TemplatePath, messages),
                ExportFormat.LabelPdf => LabelPdf(path, fields, layout, request.Barcode, drawn),
                ExportFormat.ReportPdf => ReportPdf(path, data, request.Wording),
                ExportFormat.Json => Json(path, data),
                ExportFormat.Csv => Csv(path, data),
                _ => new Outcome(format, null, false, $"Unknown format {format}."),
            };
        }
        catch (Exception ex)
        {
            // A file that was refused before it was written carries the reason it
            // was refused, which is already an operator's sentence. Anything else
            // is the platform talking, so it is named rather than passed on bare.
            string problem = ex is InvalidDataException or FileNotFoundException or InvalidOperationException
                ? ex.Message
                : messages.Say(messages.WriteFailed, format, ex.Message);

            return new Outcome(format, null, false, problem);
        }
        finally
        {
            // Written off the UI thread, which is the only reason this is async.
            await Task.Yield();
        }
    }

    private static Outcome DymoLabel(string path, LabelFields fields, LabelLayout layout,
        LabelBarcodeMode mode, string? templatePath, ExportWording messages)
    {
        string template = DymoTemplateFiles.Read(templatePath, messages);

        // The template is filled in one pass by the same code that decides what the
        // barcodes carry, so the .dymo file cannot come out with something on it
        // that the label PDF and the preview do not also have. Its symbology is the
        // one the template declares, which is what the file has to carry.
        DymoFillResult filled = DymoTemplate.Fill(template, fields, layout, mode, messages);

        File.WriteAllText(path, filled.Text, new System.Text.UTF8Encoding(false));

        var notes = new List<string>();
        if (filled.UnfilledFields.Count > 0)
            notes.Add(messages.Say(messages.UnfilledFields, string.Join(", ", filled.UnfilledFields)));

        return new Outcome(ExportFormat.DymoLabel, path, true, null,
            notes.Count > 0 ? string.Join(" ", notes) : null);
    }

    private static Outcome LabelPdf(
        string path, LabelFields fields, LabelLayout layout, LabelBarcodeMode mode,
        LabelCodeSymbology symbology)
    {
        ReportFonts.Ensure();
        LabelPdfWriter.Write(path, fields, layout, mode, symbology);
        return new Outcome(ExportFormat.LabelPdf, path, true, null);
    }

    private static Outcome ReportPdf(string path, DeviceData data, ReportWording? wording)
    {
        ReportFonts.Ensure();
        ReportPdfWriter.Write(path, data, wording ?? ReportWording.English);
        return new Outcome(ExportFormat.ReportPdf, path, true, null);
    }

    private static Outcome Json(string path, DeviceData data)
    {
        File.WriteAllText(path, DeviceReportJson.Serialize(data), new System.Text.UTF8Encoding(false));
        return new Outcome(ExportFormat.Json, path, true, null);
    }

    private static Outcome Csv(string path, DeviceData data)
    {
        File.WriteAllText(path, CsvReport.Write(data), new System.Text.UTF8Encoding(false));
        return new Outcome(ExportFormat.Csv, path, true, null);
    }

    /// <summary>
    /// The name every file of one inspection shares. Readable, ordered and free of
    /// characters a file system or a URL cannot take, because these names end up
    /// in both.
    /// </summary>
    public static string FileStem(DeviceData data, DateTime? moment = null)
    {
        string identifier = Sanitize(data.Identifier);
        if (identifier == DevicePlaceholders.Identifier) identifier = "unknown";

        string stamp = (moment ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"{identifier}-{stamp}";
    }

    /// <summary>
    /// Replaces what a file system or a browser will not take. Deliberately wider
    /// than Path.GetInvalidFileNameChars: this stem is used for downloads and for
    /// paths as well as for disk files, and a slash in it is a directory change.
    /// </summary>
    internal static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "export";
        char[] forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        return string.Concat(name.Select(character => forbidden.Contains(character) ? '_' : character)).Trim();
    }
}

/// <summary>The files an inspection can leave behind.</summary>
public enum ExportFormat
{
    /// <summary>A .dymo file for DYMO Label and the web service.</summary>
    DymoLabel,

    /// <summary>A label as a PDF, for any printer on any of the three platforms.</summary>
    LabelPdf,

    /// <summary>The full inspection report as a PDF.</summary>
    ReportPdf,

    /// <summary>Every number the phone reported, as JSON.</summary>
    Json,

    /// <summary>The same numbers as CSV, for a spreadsheet.</summary>
    Csv,
}

public static class ExportFormatExtensions
{
    /// <summary>
    /// What goes on the end of the file name, extension included. The two PDFs
    /// differ here and nowhere else, so a label and a report of the same device
    /// can sit next to each other.
    /// </summary>
    public static string FileSuffix(this ExportFormat format) => format switch
    {
        ExportFormat.DymoLabel => ".dymo",
        ExportFormat.LabelPdf => "-label.pdf",
        ExportFormat.ReportPdf => "-report.pdf",
        ExportFormat.Json => ".json",
        ExportFormat.Csv => ".csv",
        _ => ".dat",
    };

    /// <summary>The extension on its own, without the stem.</summary>
    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.DymoLabel => ".dymo",
        ExportFormat.LabelPdf => ".pdf",
        ExportFormat.ReportPdf => ".pdf",
        ExportFormat.Json => ".json",
        ExportFormat.Csv => ".csv",
        _ => ".dat",
    };

    /// <summary>The MIME type, for the shell that hands the file to the system.</summary>
    public static string ContentType(this ExportFormat format) => format switch
    {
        ExportFormat.DymoLabel => "application/xml",
        ExportFormat.LabelPdf or ExportFormat.ReportPdf => "application/pdf",
        ExportFormat.Json => "application/json",
        ExportFormat.Csv => "text/csv",
        _ => "application/octet-stream",
    };
}