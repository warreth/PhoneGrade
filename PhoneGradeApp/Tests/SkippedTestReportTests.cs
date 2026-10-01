using System.Text.Json;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

/// <summary>
/// What the desktop shows when the phone did not run every step.
///
/// The payload is the shape <c>buildSuiteResult()</c> in app.js builds and
/// <c>DeviceTest.toJSON()</c> fills row by row: camelCase fields, lowercase
/// statuses, one row per verdict, sent as one suite_complete message.
/// </summary>
public class SkippedTestReportTests
{
    private const string SuitePayload = """
        {
          "type": "suite_complete",
          "sessionId": "AUTO-TEST-0001",
          "payload": {
            "sessionId": "AUTO-TEST-0001",
            "deviceUdid": "AUTO-TEST-0001",
            "userAgent": "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
            "platform": "iOS",
            "startedAt": "2026-09-30T09:12:03.481Z",
            "completedAt": "2026-09-30T09:14:47.902Z",
            "tests": [
              { "id": "touch_canvas", "name": "Touchscreen", "status": "passed", "notes": "", "durationMs": 4210, "details": {} },
              { "id": "location", "name": "Locatie (GPS)", "status": "skipped", "notes": "De browser heeft geen locatie-API", "durationMs": 0, "details": {} },
              { "id": "camera", "name": "Camera", "status": "failed", "notes": "Geen zicht op de sensor", "durationMs": 1180, "details": {} }
            ]
          }
        }
        """;

    /// <summary>The same run, with every row settled as passed.</summary>
    private static string CompleteRun() => SuitePayload
        .Replace("\"status\": \"skipped\"", "\"status\": \"passed\"")
        .Replace("\"status\": \"failed\"", "\"status\": \"passed\"");

    private static InteractiveTestSuiteResult ReadSuite(string json)
    {
        // The server reads it exactly this way: case-insensitive, because the
        // phone writes camelCase and the model is PascalCase.
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var message = JsonSerializer.Deserialize<DeviceSessionMessage>(json, options);

        Assert.NotNull(message?.Payload);
        return message!.Payload!;
    }

    [Fact]
    public void ThePhonesSkippedWordReachesTheDesktopAsSkipped()
    {
        var suite = ReadSuite(SuitePayload);

        Assert.Equal(3, suite.Tests.Count);
        Assert.Equal(TestStatus.Skipped, suite.Tests[1].Status);
        Assert.Equal(TestStatus.Failed, suite.Tests[2].Status);
        Assert.False(suite.AllPassed, "a skipped row is not a run where everything passed");
    }

    [AvaloniaFact]
    public void ASkippedTestIsListedAndTheGreenLineIsWithdrawn()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        Assert.Equal(
            string.Format(LocalizationManager.GetString("Session_TestsSkipped"), 1, 1, 1, "iOS"),
            vm.InteractiveTestSummary);
        Assert.Equal(
            string.Format(LocalizationManager.GetString("Session_FailedSkipped"), 1, 1),
            vm.InteractiveSessionStatus);

        // The wording only resolves when the language dictionary is on the app,
        // so a raw key here means the run read nothing at all.
        Assert.NotEqual("Session_FailedSkipped", vm.InteractiveSessionStatus);
        Assert.NotEqual(LocalizationManager.GetString("Session_AllPassed"), vm.InteractiveSessionStatus);

        Assert.Equal("Locatie (GPS)", Assert.Single(vm.SkippedInteractiveTests).Name);
        Assert.Single(vm.FailedInteractiveTests);
        Assert.False(vm.NoInteractiveTestProblems);
    }

    [AvaloniaFact]
    public void AFailureStillReadsAsAFailureAlongsideTheSkippedOne()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        Assert.Equal("Camera", Assert.Single(vm.FailedInteractiveTests).Name);

        // One row went wrong and another never ran, so the line the operator
        // reads has to carry both numbers.
        Assert.Equal(
            string.Format(LocalizationManager.GetString("Session_FailedSkipped"), 1, 1),
            vm.InteractiveSessionStatus);
        Assert.NotEqual("Session_FailedSkipped", vm.InteractiveSessionStatus);
    }

    [AvaloniaFact]
    public void ARunWhereEverythingRanAndPassedKeepsItsGreenLine()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(CompleteRun()));

        Assert.Empty(vm.SkippedInteractiveTests);
        Assert.Empty(vm.FailedInteractiveTests);
        Assert.True(vm.NoInteractiveTestProblems);
        Assert.Equal(LocalizationManager.GetString("Session_AllPassed"), vm.InteractiveSessionStatus);
        Assert.NotEqual("Session_AllPassed", vm.InteractiveSessionStatus);
        Assert.Equal(
            string.Format(LocalizationManager.GetString("Session_TestsPlain"), 3, 0, "iOS"),
            vm.InteractiveTestSummary);
    }

    [Fact]
    public void ASecondRunReplacesWhatTheFirstOneLeftInTheReport()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));
        Assert.Single(vm.SkippedInteractiveTests);

        vm.ApplyInteractiveResults(ReadSuite(CompleteRun()));

        Assert.Empty(vm.SkippedInteractiveTests);
        Assert.Empty(vm.FailedInteractiveTests);
        Assert.True(vm.NoInteractiveTestProblems,
            "rows from the run before are still on screen, so the report is about the wrong phone");
    }

    /// <summary>
    /// The reason for a skip reaches the list and stays attached to the row it
    /// belongs to, and the view has to bind it.
    ///
    /// The row already carried the note the phone sent; the report read the name
    /// and the badge and left it there, so GPS came out as a bare OVERGESLAGEN.
    /// </summary>
    [Fact]
    public void TheReasonForASkipStaysOnTheRowThatIsShown()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        var skipped = Assert.Single(vm.SkippedInteractiveTests);
        Assert.Equal("De browser heeft geen locatie-API", skipped.Notes);

        var failed = Assert.Single(vm.FailedInteractiveTests);
        Assert.Equal("Geen zicht op de sensor", failed.Notes);

        string view = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");
        Assert.Contains("Text=\"{Binding Notes}\"", view);

        // The note is null on a row with nothing to say, and a bound TextBlock
        // with an empty string still takes a line. The row must not grow a
        // blank one.
        Assert.Contains("StringConverters.IsNotNullOrEmpty", view);
    }

    /// <summary>
    /// The lists are only worth filling if something puts them on the screen.
    /// </summary>
    [Fact]
    public void TheReportAndTheResultsScreenBothHaveSomewhereToShowIt()
    {
        string view = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");
        Assert.Contains("ItemsSource=\"{Binding SkippedInteractiveTests}\"", view);
        Assert.Contains("Report_Skipped", view);
        Assert.Contains("IsVisible=\"{Binding NoInteractiveTestProblems}\"", view);

        string index = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");
        Assert.Contains("id=\"skipped-count\"", index);

        string app = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        Assert.Contains("counts.skipped", app);

        string styles = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/styles.css");
        Assert.Contains(".result-badge.skipped", styles);
        Assert.Contains(".stat-value.skipped", styles);
    }

    /// <summary>
    /// The reason for a skip is only useful if it reaches the screen it is read
    /// on. The row already carries the note; the results screen used to show the
    /// name and the badge and leave it there, so GPS read as a bare SKIPPED.
    /// </summary>
    [Fact]
    public void TheResultsScreenShowsTheReasonUnderTheRow()
    {
        string app = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        Assert.Contains("buildResultRow", app);

        string row = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/ResultRows.js");
        Assert.Contains("result-item-note", row);
        Assert.Contains("test.notes", row);

        string styles = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/styles.css");
        Assert.Contains(".result-item-note", styles);

        // A note on its own line needs the row to be allowed to have a second
        // line. Without the wrap the reason is squeezed in beside the badge.
        Assert.Contains("flex-wrap: wrap", styles);
    }
}
