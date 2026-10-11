using System;
using PhoneGrade.Core.Diagnostics;
using Xunit;

namespace Tests;

// ============ The crash report window ============
//
// iOS keeps crash reports forever, and a report from months ago says nothing
// about the phone on the bench today. The window a shop picks decides which
// reports the scan keeps. The date comes from the report's own name first,
// because a pull can stamp the copy with the time it was copied, and a report
// whose name carries the day the panic happened should be judged on that day.

public class CrashLogRangeTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0);

    [Theory]
    [InlineData("panic-full-2026-10-06-064858.ips", 2026, 10, 6)]
    [InlineData("panic-base-2025-12-31-235959.ips", 2025, 12, 31)]
    [InlineData("socd-2026-01-15-101112.ips", 2026, 1, 15)]
    public void TheDateInTheNameIsTheDayThePanicHappened(string fileName, int year, int month, int day)
        => Assert.Equal(new DateTime(year, month, day), CrashLogFiles.DateFromName(fileName));

    [Fact]
    public void ANameWithoutADateHasNoDate()
    {
        Assert.Null(CrashLogFiles.DateFromName("panic-full.ips"));
        Assert.Null(CrashLogFiles.DateFromName(""));
        Assert.Null(CrashLogFiles.DateFromName(null));
    }

    [Fact]
    public void TheWindowKeepsOnlyReportsInsideIt()
    {
        const string threeDaysOld = "panic-full-2026-10-08-010101.ips";
        const string fortyDaysOld = "panic-full-2026-09-01-010101.ips";
        DateTime staleWrite = new(2026, 7, 1, 0, 0, 0);

        Assert.False(CrashLogFiles.InRange(threeDaysOld, staleWrite, CrashLogRange.Day, Now));
        Assert.True(CrashLogFiles.InRange(threeDaysOld, staleWrite, CrashLogRange.Week, Now));
        Assert.True(CrashLogFiles.InRange(threeDaysOld, staleWrite, CrashLogRange.Month, Now));
        Assert.True(CrashLogFiles.InRange(threeDaysOld, staleWrite, CrashLogRange.All, Now));

        Assert.False(CrashLogFiles.InRange(fortyDaysOld, staleWrite, CrashLogRange.Day, Now));
        Assert.False(CrashLogFiles.InRange(fortyDaysOld, staleWrite, CrashLogRange.Week, Now));
        Assert.False(CrashLogFiles.InRange(fortyDaysOld, staleWrite, CrashLogRange.Month, Now));
        Assert.True(CrashLogFiles.InRange(fortyDaysOld, staleWrite, CrashLogRange.All, Now));
    }

    [Fact]
    public void AReportWithoutADateInItsNameIsJudgedByWhenItWasWritten()
    {
        // The pull stamps the copy, so this is the weak half of the pair; it only
        // decides for a file whose name says nothing, which is rare on iOS.
        Assert.True(CrashLogFiles.InRange("panic-full.ips", new(2026, 10, 11, 6, 0, 0), CrashLogRange.Day, Now));
        Assert.False(CrashLogFiles.InRange("panic-full.ips", new(2026, 10, 1, 0, 0, 0), CrashLogRange.Day, Now));
    }

    [Fact]
    public void AllMeansEveryReportHoweverOld()
    {
        Assert.True(CrashLogFiles.InRange("panic-full-2019-01-01-000000.ips",
            new DateTime(2019, 1, 1), CrashLogRange.All, Now));
        Assert.Equal(TimeSpan.MaxValue, CrashLogFiles.Window(CrashLogRange.All));
    }
}
