using PhoneGrade.Core;

namespace PhoneGrade.UI.Services;

/// <summary>
/// The sentences the export panel shows about a file that did not arrive, read out
/// of the app's own dictionaries.
///
/// These are the lines an operator reads at the exact moment something went wrong,
/// in the middle of a shop. A failure line in the language of the code is a line
/// nobody can act on, so the wording is looked up in the same place the rest of the
/// panel gets it. The format placeholders stay in place: a dictionary entry that
/// drops {0} would turn "Sent to DYMO_LabelWriter." into the queue's name with no
/// sentence around it.
/// </summary>
public static class ExportWordingBuilder
{
    /// <summary>The wording in the language the operator is working in.</summary>
    public static ExportWording Current() => For(LocalizationManager.CurrentLanguage);

    /// <summary>
    /// The wording for one language. Every field is a key rather than a string,
    /// because both dictionaries carry every one of them, so there is nothing to
    /// choose between here.
    /// </summary>
    public static ExportWording For(string languageCode) => Build(
        templateMissing: "Status_TemplateMissing",
        templateUnreadable: "Status_TemplateUnreadable",
        noTemplateFound: "Status_NoTemplateFound",
        templateHasNoFields: "Status_TemplateHasNoFields",
        writeFailed: "Status_WriteFailed",
        unfilledFields: "Status_UnfilledFields",
        barcodeReplaced: "Status_BarcodeReplaced",
        barcodeWrapped: "Status_BarcodeWrapped",
        noFileAt: "Status_NoFileAt",
        noFolderAt: "Status_NoFolderAt",
        noPrinterChosen: "Status_NoPrinterChosen",
        printDialogOpened: "Status_PrintDialogOpened",
        printNoHandler: "Status_PrintNoHandler",
        printPreviewOpened: "Status_PrintPreviewOpened",
        printDefaultApp: "Status_PrintDefaultApp",
        printNotStarted: "Status_PrintNotStarted",
        printQueueNeedsDialog: "Status_PrintQueueNeedsDialog",
        printSentToQueue: "Status_PrintSentToQueue",
        printToQueueFailed: "Status_PrintToQueueFailed",
        folderOpened: "Status_FolderOpened",
        folderNotOpened: "Status_FolderNotOpened",
        fileOpened: "Status_FileOpened",
        fileNotOpened: "Status_FileNotOpened");

    /// <summary>
    /// Reads every key and takes the English value for the ones the dictionary
    /// does not carry. One forgotten string shows as English rather than as its own
    /// key, which is the lesser of the two failures.
    /// </summary>
    private static ExportWording Build(
        string templateMissing, string templateUnreadable, string noTemplateFound,
        string templateHasNoFields, string writeFailed, string unfilledFields,
        string barcodeReplaced, string barcodeWrapped, string noFileAt, string noFolderAt,
        string noPrinterChosen, string printDialogOpened, string printNoHandler,
        string printPreviewOpened, string printDefaultApp, string printNotStarted,
        string printQueueNeedsDialog, string printSentToQueue, string printToQueueFailed,
        string folderOpened, string folderNotOpened, string fileOpened, string fileNotOpened)
    {
        ExportWording english = ExportWording.English;

        return new ExportWording
        {
            TemplateMissing = Say(templateMissing, english.TemplateMissing),
            TemplateUnreadable = Say(templateUnreadable, english.TemplateUnreadable),
            NoTemplateFound = Say(noTemplateFound, english.NoTemplateFound),
            TemplateHasNoFields = Say(templateHasNoFields, english.TemplateHasNoFields),
            WriteFailed = Say(writeFailed, english.WriteFailed),
            UnfilledFields = Say(unfilledFields, english.UnfilledFields),
            BarcodeReplaced = Say(barcodeReplaced, english.BarcodeReplaced),
            BarcodeWrapped = Say(barcodeWrapped, english.BarcodeWrapped),
            NoFileAt = Say(noFileAt, english.NoFileAt),
            NoFolderAt = Say(noFolderAt, english.NoFolderAt),
            NoPrinterChosen = Say(noPrinterChosen, english.NoPrinterChosen),
            PrintDialogOpened = Say(printDialogOpened, english.PrintDialogOpened),
            PrintNoHandler = Say(printNoHandler, english.PrintNoHandler),
            PrintPreviewOpened = Say(printPreviewOpened, english.PrintPreviewOpened),
            PrintDefaultApp = Say(printDefaultApp, english.PrintDefaultApp),
            PrintNotStarted = Say(printNotStarted, english.PrintNotStarted),
            PrintQueueNeedsDialog = Say(printQueueNeedsDialog, english.PrintQueueNeedsDialog),
            PrintSentToQueue = Say(printSentToQueue, english.PrintSentToQueue),
            PrintToQueueFailed = Say(printToQueueFailed, english.PrintToQueueFailed),
            FolderOpened = Say(folderOpened, english.FolderOpened),
            FolderNotOpened = Say(folderNotOpened, english.FolderNotOpened),
            FileOpened = Say(fileOpened, english.FileOpened),
            FileNotOpened = Say(fileNotOpened, english.FileNotOpened),
        };
    }

    /// <summary>
    /// Reads one key, and takes the fallback when the dictionary does not carry it
    /// or when it carries it without the placeholders the English one has. A Dutch
    /// sentence with {0} missing would print the path and nothing else, which reads
    /// as a fault in the app rather than as a translation gap.
    /// </summary>
    private static string Say(string key, string fallback)
    {
        string value = LocalizationManager.GetString(key);
        if (value == key || value.Length == 0) return fallback;

        return Count(value, '{') == Count(fallback, '{') ? value : fallback;
    }

    private static int Count(string text, char character)
    {
        int count = 0;
        foreach (char current in text)
            if (current == character) count++;
        return count;
    }
}
