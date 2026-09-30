using System.Text.Json;
using PhoneGrade.Core;
using PhoneGrade.Tests;
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

    [Fact]
    public void ASkippedTestIsListedAndTheGreenLineIsWithdrawn()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        Assert.Contains("1 overgeslagen", vm.InteractiveTestSummary);
        Assert.Contains("overgeslagen", vm.InteractiveSessionStatus);
        Assert.DoesNotContain("Alles geslaagd", vm.InteractiveSessionStatus);

        Assert.Equal("Locatie (GPS)", Assert.Single(vm.SkippedInteractiveTests).Name);
        Assert.Single(vm.FailedInteractiveTests);
        Assert.False(vm.NoInteractiveTestProblems);
    }

    [Fact]
    public void AFailureStillReadsAsAFailureAlongsideTheSkippedOne()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        Assert.Equal("Camera", Assert.Single(vm.FailedInteractiveTests).Name);
        Assert.Contains("1 fout(en)", vm.InteractiveSessionStatus);
        Assert.True(vm.InteractiveSessionStatus.Contains("1 overgeslagen", StringComparison.Ordinal),
            "one row went wrong and another never ran, so the status line has to carry both numbers: "
            + vm.InteractiveSessionStatus);
    }

    [Fact]
    public void ARunWhereEverythingRanAndPassedKeepsItsGreenLine()
    {
        using var vm = new MainWindowViewModel();

        vm.ApplyInteractiveResults(ReadSuite(CompleteRun()));

        Assert.Empty(vm.SkippedInteractiveTests);
        Assert.Empty(vm.FailedInteractiveTests);
        Assert.True(vm.NoInteractiveTestProblems);
        Assert.Equal("Interactieve hardwaretest: Alles geslaagd!", vm.InteractiveSessionStatus);
        Assert.DoesNotContain("overgeslagen", vm.InteractiveTestSummary);
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
    /// The lists are only worth filling if something puts them on the screen.
    /// </summary>
    [Fact]
    public void TheReportAndTheResultsScreenBothHaveSomewhereToShowIt()
    {
        string view = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");
        Assert.Contains("ItemsSource=\"{Binding SkippedInteractiveTests}\"", view);
        Assert.Contains("OVERGESLAGEN", view);
        Assert.Contains("IsVisible=\"{Binding NoInteractiveTestProblems}\"", view);

        string index = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");
        Assert.Contains("id=\"skipped-count\"", index);

        string app = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        Assert.Contains("counts.skipped", app);

        string styles = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/styles.css");
        Assert.Contains(".result-badge.skipped", styles);
        Assert.Contains(".stat-value.skipped", styles);
    }
}
