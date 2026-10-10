namespace PhoneGrade.Core;

/// <summary>
/// How a shop wants its exports grouped on disk.
/// </summary>
/// <remarks>
/// A counter that does a dozen inspections a day wants one folder per day or one per
/// week; a shop that looks a device up again months later wants the folder it filed
/// under. The choice is a setting rather than a rule because the right answer is the
/// shop's, not this app's.
/// </remarks>
public enum ExportFolderScheme
{
    /// <summary>One folder per calendar day, named 2026-10-08.</summary>
    Day,

    /// <summary>One folder per ISO week, named 2026-w41. The default.</summary>
    Week,

    /// <summary>One folder per month, named 2026-10.</summary>
    Month,

    /// <summary>One folder per device, named by its serial number.</summary>
    Inspection,
}

/// <summary>
/// Where the files of one inspection go inside the exports folder.
/// </summary>
/// <remarks>
/// One place that knows how the schemes are named, so the panel, the writer and the
/// settings preview cannot each invent their own date format: a preview that says
/// 2026-w41 and a file that lands in 2026-W41 is a promise the app did not keep.
/// </remarks>
public static class ExportFolders
{
    /// <summary>The folder one inspection's files belong in.</summary>
    /// <param name="root">The exports folder the operator sees.</param>
    /// <param name="scheme">How the shop groups its files.</param>
    /// <param name="moment">The moment of the inspection, local time.</param>
    /// <param name="deviceToken">
    /// The serial number, for the per device scheme. Placeholders are not suitable
    /// folder names and are replaced by the caller before they arrive here.
    /// </param>
    public static string FolderUnder(
        string root, ExportFolderScheme scheme, DateTime moment, string deviceToken)
    {
        return scheme switch
        {
            ExportFolderScheme.Day => Path.Combine(root, moment.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            ExportFolderScheme.Week => Path.Combine(root, WeekName(moment)),
            ExportFolderScheme.Month => Path.Combine(root, moment.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)),
            _ => Path.Combine(root, FileNames.Sanitize(deviceToken, "unknown")),
        };
    }

    /// <summary>An ISO week as a folder name, for example 2026-w41.</summary>
    public static string WeekName(DateTime moment) =>
        $"{System.Globalization.ISOWeek.GetYear(moment):0000}-w{System.Globalization.ISOWeek.GetWeekOfYear(moment):00}";
}
