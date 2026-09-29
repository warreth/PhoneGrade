using System;
using System.Linq;
using System.Text.Json;
using PhoneGrade.UI.Web;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the desktop-side memory of a phone run.
///
/// The case that matters most is the group in the middle: a phone that reconnects
/// with a queue of results it produced before the reload, replayed over a run
/// that has already moved on. Without the timestamps in the phone's messages,
/// the old verdict lands last and wins.
/// </summary>
public class PwaProgressStoreTests
{
    private static PwaStepRecord Step(string testId, string status, DateTimeOffset at) => new()
    {
        TestId = testId,
        TestName = testId.ToUpperInvariant(),
        Status = status,
        ReportedAt = at,
    };

    private static DateTimeOffset At(int minute) =>
        new DateTimeOffset(2026, 9, 30, 10, minute, 0, TimeSpan.Zero);

    [Fact]
    public void AnUnknownSessionReadsAsAnEmptyRun()
    {
        var store = new PwaProgressStore();

        var snapshot = store.Get("never-seen");

        // Not a 404 and not a throw: the phone has to be able to treat "nothing
        // stored" and "first run" as the same case and simply start at the top.
        Assert.Equal("never-seen", snapshot.SessionId);
        Assert.False(snapshot.Started);
        Assert.False(snapshot.Finished);
        Assert.Empty(snapshot.Steps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingSessionIdFallsBackToAPlaceholder(string? sessionId)
    {
        var store = new PwaProgressStore();
        store.RecordStep(sessionId, Step("touch", "passed", At(0)), 12);

        Assert.Single(store.Get("UNKNOWN").Steps);
    }

    [Fact]
    public void StepsAreKeptInTheOrderThePhoneReportedThem()
    {
        var store = new PwaProgressStore();

        store.RecordStep("A", Step("touch", "passed", At(0)), 3);
        store.RecordStep("A", Step("display", "failed", At(1)), 3);
        store.RecordStep("A", Step("speaker", "passed", At(2)), 3);

        Assert.Equal(
            new[] { "touch", "display", "speaker" },
            store.Get("A").Steps.Select(s => s.TestId));
    }

    [Fact]
    public void AStepIsOnlyAcceptedWhenItIsNewerThanWhatIsStored()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "failed", At(0)), 3);

        // The operator hit "Opnieuw" on the phone and it came back clean. The
        // result is newer, so it replaces the earlier one in place.
        Assert.True(store.RecordStep("A", Step("touch", "passed", At(5)), 3));

        var snapshot = store.Get("A");
        Assert.Single(snapshot.Steps);
        Assert.Equal("passed", snapshot.Steps[0].Status);
    }

    [Fact]
    public void AReplayedOldResultCannotOverwriteANewerOne()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(5)), 3);

        // The offline queue replay. Same test, older stamp, worse verdict. It must
        // be refused, or the retry the operator just did is silently undone.
        Assert.False(store.RecordStep("A", Step("touch", "failed", At(0)), 3));

        Assert.Equal("passed", store.Get("A").Steps.Single().Status);
    }

    [Fact]
    public void AResultReplayedWithTheSameStampIsRefused()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(5)), 3);

        // A retry at the same instant cannot be told apart from a replay by time
        // alone, so it is treated as the replay. Refusing it is the safe side:
        // the verdict on screen stays the one the operator saw.
        Assert.False(store.RecordStep("A", Step("touch", "failed", At(5)), 3));
        Assert.Single(store.Get("A").Steps);
    }

    [Fact]
    public void AStepWithoutATestIdIsNotStored()
    {
        var store = new PwaProgressStore();

        Assert.False(store.RecordStep("A", new PwaStepRecord { TestId = "", Status = "passed" }, 3));
        Assert.False(store.RecordStep("A", new PwaStepRecord { TestId = "   ", Status = "passed" }, 3));
        Assert.Empty(store.Get("A").Steps);
    }

    [Fact]
    public void TestIdsAreMatchedWithoutRegardToCase()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("Touch", "failed", At(0)), 3);

        Assert.True(store.RecordStep("A", Step("touch", "passed", At(5)), 3));
        Assert.Single(store.Get("A").Steps);
    }

    [Fact]
    public void FinishingAStarterClearsTheTestInFlight()
    {
        var store = new PwaProgressStore();
        store.RecordStart("A", "display", "Display Test", 12);
        store.RecordStep("A", Step("display", "passed", At(1)), 12);

        var snapshot = store.Get("A");
        Assert.True(snapshot.Started);
        // The step that was running is done, so nothing is in flight any more.
        Assert.Null(snapshot.CurrentTestId);
        Assert.Null(snapshot.CurrentTestName);
    }

    [Fact]
    public void StartingARunAgainReopensAFinishedOne()
    {
        var store = new PwaProgressStore();
        store.RecordFinish("A", At(10));
        store.RecordStart("A", "touch", "Touch Test", 12);

        // The operator pressed "Run Again", which clears the finished flag on the
        // phone before the first step arrives. If this did not reopen the run the
        // progress on the desktop would keep saying "klaar" during the whole rerun.
        var snapshot = store.Get("A");
        Assert.False(snapshot.Finished);
        Assert.Equal("touch", snapshot.CurrentTestId);
    }

    [Fact]
    public void FinishingASuiteMarksItDone()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(0)), 1);

        Assert.True(store.RecordFinish("A", At(1)));

        var snapshot = store.Get("A");
        Assert.True(snapshot.Finished);
        Assert.True(snapshot.Started);
    }

    [Fact]
    public void AReplayedSuiteCompleteDoesNotReopenAStoredRun()
    {
        var store = new PwaProgressStore();
        store.RecordFinish("A", At(10));

        // The same payload replayed from the offline queue carries the moment it
        // was first sent, which is older than what is stored.
        Assert.False(store.RecordFinish("A", At(3)));
        Assert.True(store.Get("A").Finished);
    }

    [Fact]
    public void SessionsDoNotLeakIntoEachOther()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(0)), 2);
        store.RecordStep("B", Step("touch", "failed", At(0)), 2);

        // A second device put on the bench must not inherit the first one's
        // verdicts, or it would start halfway through on the basis of results
        // that belong to a different handset.
        Assert.Equal("passed", store.Get("A").Steps.Single().Status);
        Assert.Equal("failed", store.Get("B").Steps.Single().Status);
        Assert.Empty(store.Get("C").Steps);
    }

    [Fact]
    public void ResetClearsOneSessionAndLeavesTheOthers()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(0)), 2);
        store.RecordStep("B", Step("touch", "passed", At(0)), 2);

        store.Reset("A");

        Assert.Empty(store.Get("A").Steps);
        Assert.Single(store.Get("B").Steps);
    }

    [Fact]
    public void ClearEmptiesEverything()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(0)), 2);
        store.RecordStep("B", Step("touch", "passed", At(0)), 2);

        store.Clear();

        Assert.Empty(store.Get("A").Steps);
        Assert.Empty(store.Get("B").Steps);
    }

    [Fact]
    public void TheSnapshotShapeIsWhatThePhoneExpects()
    {
        // The phone reads steps[].testId, steps[].status and steps[].reportedAt.
        // Renaming any of them breaks the resume silently, so the wire names are
        // pinned here rather than left to the default PascalCase convention.
        var store = new PwaProgressStore();
        store.RecordStart("A", "touch", "Touch Test", 12);
        store.RecordStep("A", Step("touch", "passed", At(0)), 12);

        string json = JsonSerializer.Serialize(store.Get("A"));

        Assert.Contains("\"testId\":\"touch\"", json);
        Assert.Contains("\"status\":\"passed\"", json);
        Assert.Contains("\"reportedAt\":", json);
        Assert.Contains("\"sessionId\":\"A\"", json);
        Assert.Contains("\"totalTests\":12", json);
        Assert.Contains("\"currentTestId\":", json);
    }

    [Fact]
    public void ThePhoneCanLookUpWhatIsStoredById()
    {
        var store = new PwaProgressStore();
        store.RecordStep("A", Step("touch", "passed", At(0)), 2);
        store.RecordStep("A", Step("display", "failed", At(1)), 2);

        var byId = store.Get("A").ByTestId();

        Assert.Equal("passed", byId["touch"].Status);
        Assert.Equal("failed", byId["display"].Status);
    }
}
