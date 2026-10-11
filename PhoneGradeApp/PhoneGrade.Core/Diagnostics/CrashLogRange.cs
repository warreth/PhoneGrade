using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhoneGrade.Core.Diagnostics;

/// <summary>
/// How far back the crash report scan looks.
/// </summary>
/// <remarks>
/// Crash reports accumulate on a handset and are never cleaned by iOS. A report
/// from months ago says nothing about the phone on the bench today, and a shop
/// that trades in repaired devices does not want an old panic to keep costing a
/// phone its grade. The window is a shop's choice, so it is a setting rather
/// than a constant.
/// </remarks>
public enum CrashLogRange
{
    /// <summary>Reports of the last day.</summary>
    Day,

    /// <summary>Reports of the last week.</summary>
    Week,

    /// <summary>Reports of the last month, which is the default.</summary>
    Month,

    /// <summary>Every report on the handset, however old.</summary>
    All,
}

/// <summary>
/// Which crash report files a scan keeps, and the date a report is about.
/// </summary>
/// <remarks>
/// The date is read from the file's own name first, because that is the moment
/// the panic happened. A pull can stamp the copy with the time it was copied, so
/// the write time is only the fallback for a file whose name carries no date.
/// </remarks>
public static class CrashLogFiles
{
    /// <summary>Whether a report belongs to the window the shop chose.</summary>
    /// <param name="fileName">The report's own name, e.g. panic-full-2026-10-06-064858.ips.</param>
    /// <param name="lastWrite">When the file was last written, used when the name has no date.</param>
    /// <param name="range">The window the shop chose.</param>
    /// <param name="now">The moment the scan runs.</param>
    public static bool InRange(string fileName, DateTime lastWrite, CrashLogRange range, DateTime now)
    {
        if (range == CrashLogRange.All) return true;

        DateTime dated = DateFromName(fileName) ?? lastWrite;
        return now - dated <= Window(range);
    }

    /// <summary>The length of a window, in days. All means no limit.</summary>
    public static TimeSpan Window(CrashLogRange range) => range switch
    {
        CrashLogRange.Day => TimeSpan.FromDays(1),
        CrashLogRange.Week => TimeSpan.FromDays(7),
        CrashLogRange.Month => TimeSpan.FromDays(30),
        _ => TimeSpan.MaxValue,
    };

    /// <summary>
    /// The date in a report's name, or null when it carries none.
    /// </summary>
    /// <remarks>
    /// iOS names a report panic-full-2026-10-06-064858.ips: the date is the day
    /// the panic happened. Baseband and socd reports use the same shape.
    /// </remarks>
    public static DateTime? DateFromName(string fileName)
    {
        Match match = Regex.Match(fileName ?? "", @"(\d{4})-(\d{2})-(\d{2})");
        if (!match.Success) return null;

        return DateTime.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date
            : null;
    }
}
