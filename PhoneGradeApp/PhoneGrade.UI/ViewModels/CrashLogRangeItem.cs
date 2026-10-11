using System.Collections.Generic;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// One choice in the crash report window, with the resource key its label reads
/// from rather than the translated text, so a language switch moves the picker
/// along with the rest of the window.
/// </summary>
public sealed class CrashLogRangeItem
{
    private CrashLogRangeItem(CrashLogRange range, string titleKey)
    {
        Range = range;
        TitleKey = titleKey;
    }

    public CrashLogRange Range { get; }

    /// <summary>The resource key of the shown name.</summary>
    public string TitleKey { get; }

    public static IReadOnlyList<CrashLogRangeItem> All { get; } =
    [
        new(CrashLogRange.Day, "CrashLogRange_Day"),
        new(CrashLogRange.Week, "CrashLogRange_Week"),
        new(CrashLogRange.Month, "CrashLogRange_Month"),
        new(CrashLogRange.All, "CrashLogRange_All"),
    ];
}
