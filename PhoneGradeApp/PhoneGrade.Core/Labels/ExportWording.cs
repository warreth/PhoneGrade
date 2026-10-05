namespace PhoneGrade.Core;

/// <summary>
/// Every sentence the export panel shows about a file that did not arrive, a note
/// about one that did, or a print that did or did not start.
///
/// These are sentences an operator reads at the moment something went wrong, in
/// the middle of a shop with a phone on the counter. Writing them in the language
/// the code was written in means the one line that matters most is the one line
/// nobody can act on. So they are held here as format strings and the view model
/// fills them in from the app's own dictionaries, the same way the report PDFs get
/// their headings.
///
/// Nothing here is localised in this project. The English set is the default and
/// the fallback, so a caller with no UI still gets a readable sentence rather than
/// an empty one.
/// </summary>
public sealed record ExportWording
{
    /// <summary>The set used when no wording was handed in.</summary>
    public static ExportWording English { get; } = new()
    {
        TemplateMissing = "The label template {0} is not there. Pick another one in Settings, or clear the setting to use the template that came with the app.",
        TemplateUnreadable = "The label template at {0} could not be read: {1}",
        NoTemplateFound = "No label template ({0}) was found. Looked in: {1}",
        TemplateHasNoFields = "The label template carries none of the fields this app fills, so every label would come out the same. Open the template that came with the app again.",
        WriteFailed = "Writing the {0} file failed: {1}",
        UnfilledFields = "The template also asks for {0}, which nothing fills.",
        BarcodeReplaced = "Characters the barcode cannot carry were replaced with a dash, and the text is printed beside it.",
        BarcodeWrapped = "Asterisks were added so the barcode can be read back.",

        NoFileAt = "There is no file at {0}.",
        NoFolderAt = "There is no folder at {0}.",
        NoPrinterChosen = "No printer was chosen.",
        PrintDialogOpened = "Sent to the print dialog.",
        PrintNoHandler = "No print handler is installed, so the file was opened instead. Print it from there.",
        PrintPreviewOpened = "Opened in Preview. Print it from there.",
        PrintDefaultApp = "Opened in the default application. Print it from there.",
        PrintNotStarted = "Printing could not be started: {0}",
        PrintQueueNeedsDialog = "Choosing a printer by name needs a print dialog on Windows. Use Print instead.",
        PrintSentToQueue = "Sent to {0}.",
        PrintToQueueFailed = "Printing to {0} failed: {1}",
        FolderOpened = "Opened the folder.",
        FolderNotOpened = "The folder could not be opened: {0}",
        FileOpened = "Opened.",
        FileNotOpened = "The file could not be opened: {0}",
    };

    /// <summary>A .dymo template the operator configured is not where it was said to be.</summary>
    public required string TemplateMissing { get; init; }

    /// <summary>A template is there but cannot be read. The reason is the platform's.</summary>
    public required string TemplateUnreadable { get; init; }

    /// <summary>No template at all, not even the one that came with the app.</summary>
    public required string NoTemplateFound { get; init; }

    /// <summary>The template carries no field this app fills, so every label would be identical.</summary>
    public required string TemplateHasNoFields { get; init; }

    /// <summary>Writing a file threw. The reason is whatever the platform said.</summary>
    public required string WriteFailed { get; init; }

    /// <summary>Fields the operator's own template asks for that nothing fills.</summary>
    public required string UnfilledFields { get; init; }

    /// <summary>The identifier had characters a barcode cannot carry.</summary>
    public required string BarcodeReplaced { get; init; }

    /// <summary>The identifier gained its markers so a scanner can tell where the data stops.</summary>
    public required string BarcodeWrapped { get; init; }

    /// <summary>The file to print or open is not on disk.</summary>
    public required string NoFileAt { get; init; }

    /// <summary>The folder to open is not on disk.</summary>
    public required string NoFolderAt { get; init; }

    /// <summary>A print to a named queue was asked for with nothing chosen.</summary>
    public required string NoPrinterChosen { get; init; }

    /// <summary>The platform took the file and its own print dialog is up.</summary>
    public required string PrintDialogOpened { get; init; }

    /// <summary>No application has registered a print command for this kind of file.</summary>
    public required string PrintNoHandler { get; init; }

    /// <summary>macOS handed the PDF to Preview.</summary>
    public required string PrintPreviewOpened { get; init; }

    /// <summary>Linux handed the file to whatever is registered for it.</summary>
    public required string PrintDefaultApp { get; init; }

    /// <summary>Starting the platform's print path threw.</summary>
    public required string PrintNotStarted { get; init; }

    /// <summary>CUPS can print to a named queue; Windows cannot without a driver that exposes one.</summary>
    public required string PrintQueueNeedsDialog { get; init; }

    /// <summary>The file went to a queue without a dialog.</summary>
    public required string PrintSentToQueue { get; init; }

    /// <summary>Sending to a queue threw.</summary>
    public required string PrintToQueueFailed { get; init; }

    /// <summary>The file manager opened the folder.</summary>
    public required string FolderOpened { get; init; }

    /// <summary>Opening the folder threw.</summary>
    public required string FolderNotOpened { get; init; }

    /// <summary>The file opened in whatever handles it.</summary>
    public required string FileOpened { get; init; }

    /// <summary>Opening the file threw.</summary>
    public required string FileNotOpened { get; init; }

    /// <summary>
    /// Fills one of these in. Empty means the format string is the whole sentence,
    /// which keeps the strings without a value in them readable in a dictionary.
    /// </summary>
    public string Say(string format, params object?[] arguments) =>
        arguments.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.CurrentCulture, format, arguments);
}
