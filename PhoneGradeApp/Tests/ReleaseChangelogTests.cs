using System;
using System.IO;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// The changelog that appears after an update installed itself.
///
/// An update replaces the binaries and restarts, so these notes are the only
/// record of what changed that the operator ever sees. That makes two properties
/// worth holding: the note is shown exactly once, and a note that cannot be read
/// never stops the app from starting.
/// </summary>
public class ReleaseChangelogTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public ReleaseChangelogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "phonegrade-changelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, ReleaseChangelog.FileName);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void RecordedNotesAreReadBack()
    {
        ReleaseChangelog.Record("5.1.0", "- Fixed the label barcode\n- Faster Android detection", _file);

        var changelog = ReleaseChangelog.Take(_file);

        Assert.NotNull(changelog);
        Assert.Equal("5.1.0", changelog!.Version);

        // Split on the platform's own newline: the file was written with
        // Environment.NewLine and a '\r' left at the end of a line would show as
        // a broken line in the panel.
        string[] lines = changelog.Notes.Split(
            new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);

        Assert.Equal(2, lines.Length);
        Assert.Equal("Fixed the label barcode", lines[0]);
        Assert.Equal("Faster Android detection", lines[1]);

        /* The split above is on the platform's own newline, so anything left
         * over is a real stray: a lone carriage return from the packaging step
         * that survived into the text, which renders as a broken line in the
         * panel. Splitting on '\n' alone would also match the \r half of a
         * \r\n separator, which is correct and not what this is checking. */
        foreach (string line in lines)
        {
            Assert.False(line.EndsWith('\r'), $"line ends with a stray CR: '{line}'");
        }
    }

    [Fact]
    public void NotesAreTakenOnceOnly()
    {
        ReleaseChangelog.Record("5.1.0", "- Something changed", _file);

        Assert.NotNull(ReleaseChangelog.Take(_file));
        // An operator who closes the panel and starts the app tomorrow must not be
        // shown yesterday's changes again.
        Assert.Null(ReleaseChangelog.Take(_file));
        Assert.Null(ReleaseChangelog.Take(_file));
    }

    [Fact]
    public void NothingRecordedMeansNothingShown()
    {
        Assert.Null(ReleaseChangelog.Take(_file));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyNotesWriteNothing(string? notes)
    {
        // A release packaged without notes must not put an empty panel in front of
        // the operator on the next start.
        ReleaseChangelog.Record("5.1.0", notes, _file);

        Assert.False(File.Exists(_file));
        Assert.Null(ReleaseChangelog.Take(_file));
    }

    [Fact]
    public void NotesThatAreOnlyMarkupWriteNothing()
    {
        ReleaseChangelog.Record("5.1.0", "<p></p>\n<ul></ul>", _file);

        Assert.False(File.Exists(_file));
    }

    [Fact]
    public void UnreadableFileIsClearedRatherThanShownForever()
    {
        // A corrupt note must not fail the launch, and must not come back on the
        // next one either.
        File.WriteAllText(_file, "{ this is not json");

        Assert.Null(ReleaseChangelog.Take(_file));
        Assert.False(File.Exists(_file));
        Assert.Null(ReleaseChangelog.Take(_file));
    }

    [Fact]
    public void RecordSurvivesAnUnwritableLocation()
    {
        // An update that succeeded but could not write its note must not be
        // reported as a failed update, or the retry reinstalls it. A path whose
        // parent is a file is the case that reproduces that everywhere: no
        // platform allows a directory to be created beneath it.
        string blocker = Path.Combine(_dir, "not-a-directory");
        File.WriteAllText(blocker, "this is a file where a directory would have to be");

        string impossible = Path.Combine(blocker, "sub", ReleaseChangelog.FileName);

        // Nothing here may throw, and nothing may be written.
        ReleaseChangelog.Record("5.1.0", "- Something changed", impossible);

        Assert.False(File.Exists(impossible));
        Assert.Null(ReleaseChangelog.Take(impossible));
    }

    [Fact]
    public void ClearRemovesTheFileAndToleratesItsAbsence()
    {
        ReleaseChangelog.Record("5.1.0", "- Something changed", _file);
        Assert.True(File.Exists(_file));

        ReleaseChangelog.Clear(_file);
        Assert.False(File.Exists(_file));

        // Clearing again, and clearing a path that never existed, are both no-ops.
        ReleaseChangelog.Clear(_file);
        ReleaseChangelog.Clear(Path.Combine(_dir, "never-existed.json"));
    }

    [Theory]
    // Markdown as an author writes it: bullets and heading marks become nothing
    // and the sentence stays.
    [InlineData("- Fixed the barcode", "Fixed the barcode")]
    [InlineData("* Fixed the barcode", "Fixed the barcode")]
    [InlineData("+ Fixed the barcode", "Fixed the barcode")]
    [InlineData("### Fixed", "Fixed")]
    [InlineData("## Fixed", "Fixed")]
    [InlineData("**Bold** change", "Bold change")]
    [InlineData("  - Indented bullet", "Indented bullet")]
    public void MarkdownIsFlattenedIntoPlainLines(string input, string expected)
    {
        Assert.Equal(expected, ReleaseChangelog.Normalise(input));
    }

    [Fact]
    public void BlankLinesAreDropped()
    {
        // Notes with blank lines between them would otherwise render as gaps in
        // a text block that has no line spacing of its own.
        string result = ReleaseChangelog.Normalise("- One\n\n\n- Two");

        Assert.Equal("One" + Environment.NewLine + "Two", result);
    }

    [Fact]
    public void HtmlIsFlattenedWithoutLosingTheLines()
    {
        // Velopack renders the notes to HTML when packaging. Taking the tags out
        // rather than the document keeps the author's line breaks.
        const string html = "<html><body><h1>What changed</h1><p>- Fixed the barcode</p>"
            + "<p>- Faster detection</p></body></html>";

        string result = ReleaseChangelog.Normalise(html);

        Assert.Contains("Fixed the barcode", result);
        Assert.Contains("Faster detection", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }

    [Fact]
    public void EntitiesInHtmlNotesAreDecoded()
    {
        string result = ReleaseChangelog.Normalise(
            "<p>Label &amp; report &lt;v2&gt; &quot;quoted&quot;</p>");

        Assert.Contains("Label & report", result);
        Assert.Contains("<v2>", result);
        Assert.Contains("\"quoted\"", result);
    }

    [Fact]
    public void EmptyAndWhitespaceNotesNormaliseToNothing()
    {
        Assert.Equal("", ReleaseChangelog.Normalise(""));
        Assert.Equal("", ReleaseChangelog.Normalise("   "));
        Assert.Equal("", ReleaseChangelog.Normalise("\n\n  \n"));
    }

    [Fact]
    public void WindowsAndUnixLineEndingsAgree()
    {
        // The notes come from a packaging step on whichever machine cut the
        // release, so the line ending is not ours to choose.
        string fromWindows = ReleaseChangelog.Normalise("- One\r\n- Two");
        string fromUnix = ReleaseChangelog.Normalise("- One\n- Two");

        Assert.Equal(fromUnix, fromWindows);
        Assert.Contains("One", fromWindows);
        Assert.Contains("Two", fromWindows);
    }
}
