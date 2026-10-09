using PhoneGrade.Core;
using Xunit;

namespace Tests;

// ============ Where the files land ============
//
// The folder scheme is a shop's choice, and the name of every folder is a promise
// the panel makes before anything is written. One place knows how the schemes are
// named, so the preview, the writer and the settings cannot each invent their own
// date format: a preview that says 2026-w41 and a file that lands in 2026-W41 is a
// promise the app did not keep.

public class ExportFolderTests
{
    private const string Root = "/exports";

    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809", Model = "iPhone 13 Pro",
        Color = ColorKeys.White, Storage = "256GB",
    };

    [Fact]
    public void PerDayIsTheDate_PerMonthIsTheMonth()
    {
        var moment = new DateTime(2026, 10, 8, 15, 6, 7);

        Assert.Equal(
            Path.Combine(Root, "2026-10-08"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Day, moment, "3569"));
        Assert.Equal(
            Path.Combine(Root, "2026-10"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Month, moment, "3569"));
    }

    [Fact]
    public void PerWeekIsTheIsoWeek_IncludingTheYearBoundary()
    {
        // The last Monday of December belongs to the first week of the next year,
        // and a shop that files its December exports by month would never find
        // them there. ISO weeks are the whole reason this is not a simple divide.
        Assert.Equal(
            Path.Combine(Root, "2026-w41"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Week, new DateTime(2026, 10, 8), "3569"));

        Assert.Equal(
            Path.Combine(Root, "2026-w01"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Week, new DateTime(2025, 12, 29), "3569"));
    }

    [Fact]
    public void PerDeviceIsTheSerialNumber()
    {
        Assert.Equal(
            Path.Combine(Root, "356938035643809"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Inspection, new DateTime(2026, 10, 8), "356938035643809"));
    }

    [Fact]
    public void AFolderNameCannotBecomeADirectory()
    {
        // The device token comes from the phone. A slash in it would file the
        // export somewhere nobody looks, on a machine that allows it at all.
        Assert.Equal(
            Path.Combine(Root, "abc_def"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Inspection, DateTime.Now, "abc/def"));
    }

    [Fact]
    public void AnUnknownDeviceIsNamedRatherThanLeftBlank()
    {
        Assert.Equal(
            Path.Combine(Root, "unknown"),
            ExportFolders.FolderUnder(Root, ExportFolderScheme.Inspection, DateTime.Now, ""));
    }

    [Fact]
    public void TheDeviceTokenIsTheSerial_OrUnknownWhenThereIsNone()
    {
        Assert.Equal("356938035643809", LabelWriter.DeviceToken(Phone()));

        // The placeholder is not a name: NOID as a folder is a folder every unread
        // phone shares.
        var bare = new DeviceData();
        Assert.Equal("unknown", LabelWriter.DeviceToken(bare));
    }

    [Fact]
    public void TheFileNameIsTheDateTheModelAndTheSerial()
    {
        Assert.Equal(
            "2026-03-04 iPhone13Pro 356938035643809",
            LabelWriter.FileStem(Phone(), new DateTime(2026, 3, 4, 15, 6, 7)));

        // A model that was never read is left out rather than printed as NOMODEL.
        var bare = new DeviceData { Identifier = "356938035643809" };
        Assert.Equal("2026-03-04 356938035643809", LabelWriter.FileStem(bare, new DateTime(2026, 3, 4)));
    }
}
