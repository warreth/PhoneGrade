using System.Text.Json;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

/// <summary>
/// What the operator can take out of the inspection report, and what they cannot.
///
/// The report is the document somebody reads later, so every case here is about
/// the same line the feature walks: a row may be hidden, hiding it may be undone,
/// the numbers around it must not move, and the phone itself has no say in it.
/// The payload is the shape <c>buildSuiteResult()</c> in app.js builds.
/// </summary>
public class DismissedTestReportTests
{
    private const string SuitePayload = """
        {
          "type": "suite_complete",
          "sessionId": "AUTO-TEST-0001",
          "payload": {
            "sessionId": "AUTO-TEST-0001",
            "deviceUdid": "AUTO-TEST-0001",
            "userAgent": "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36",
            "platform": "Android",
            "startedAt": "2026-09-30T09:12:03.481Z",
            "completedAt": "2026-09-30T09:14:47.902Z",
            "allPassed": false,
            "tests": [
              { "id": "display", "name": "Display", "status": "passed", "notes": null, "durationMs": 3100, "details": {} },
              { "id": "camera", "name": "Camera", "status": "failed", "notes": "achter: lens geweigerd; voor: goedgekeurd", "durationMs": 4200, "details": {} },
              { "id": "location", "name": "GPS / Locatie", "status": "failed", "notes": "GPS gaf geen positie door (position unavailable)", "durationMs": 15000, "details": {} },
              { "id": "vibration", "name": "Trilmotor", "status": "skipped", "notes": "Deze browser kan de trilmotor niet aansturen.", "durationMs": 400, "details": {} }
            ]
          }
        }
        """;

    private sealed class Scope : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dismissed-{Guid.NewGuid():N}");
        private readonly string? _before = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");

        public Scope()
        {
            Directory.CreateDirectory(_dir);
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _dir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _before);
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The server reads it exactly this way: case-insensitive, because
    /// the phone writes camelCase and the model is PascalCase.</summary>
    private static InteractiveTestSuiteResult ReadSuite(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var message = JsonSerializer.Deserialize<DeviceSessionMessage>(json, options);

        Assert.NotNull(message?.Payload);
        return message!.Payload!;
    }

    private static MainWindowViewModel Loaded()
    {
        var vm = new MainWindowViewModel();
        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));
        return vm;
    }

    [AvaloniaFact]
    public void DismissingOneOfTwoFailuresLeavesTheOtherOnTheReport()
    {
        using var _ = new Scope();
        var vm = Loaded();

        Assert.Equal(2, vm.FailedInteractiveTests.Count);

        var row = vm.FailedInteractiveTests[0];

        vm.DismissInteractiveTestCommand.Execute(row).Subscribe();

        Assert.DoesNotContain(row, vm.FailedInteractiveTests);
        Assert.Equal("location", Assert.Single(vm.FailedInteractiveTests).Id);
        Assert.Single(vm.SkippedInteractiveTests);
        Assert.Same(row, Assert.Single(vm.DismissedInteractiveTests));
    }

    [AvaloniaFact]
    public void TheReportSaysHowMuchItIsHidingInsteadOfQuietlyDroppingRows()
    {
        using var _ = new Scope();
        var vm = Loaded();

        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();
        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();

        Assert.Equal(2, vm.DismissedInteractiveTests.Count);
        Assert.True(vm.HasDismissedInteractiveTests);
    }

    /// <summary>
    /// A green line over a report whose failures were all dismissed would have
    /// the document tell the next reader that nothing was wrong.
    /// </summary>
    [AvaloniaFact]
    public void TheGreenLineIsWithdrawnOnceEveryFailureHasBeenDismissed()
    {
        using var _ = new Scope();
        var vm = Loaded();
        Assert.False(vm.NoInteractiveTestProblems);

        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();
        Assert.False(vm.NoInteractiveTestProblems);

        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();

        Assert.Empty(vm.FailedInteractiveTests);
        Assert.False(vm.NoInteractiveTestProblems);
    }

    /// <summary>Hiding a row changes what is listed, never what was measured.</summary>
    [AvaloniaFact]
    public void TheSummaryKeepsReportingWhatWasMeasured()
    {
        using var _ = new Scope();
        var vm = Loaded();
        string before = vm.InteractiveTestSummary;

        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();

        Assert.Equal(before, vm.InteractiveTestSummary);
    }

    /// <summary>A dismissal that could not be undone would be a destructive button.</summary>
    [AvaloniaFact]
    public void RestoringPutsTheRowBackOnTheReport()
    {
        using var _ = new Scope();
        var vm = Loaded();
        var row = vm.FailedInteractiveTests[0];

        vm.DismissInteractiveTestCommand.Execute(row).Subscribe();
        Assert.DoesNotContain(row, vm.FailedInteractiveTests);

        vm.RestoreDismissedTestsCommand.Execute().Subscribe();

        Assert.Contains(row, vm.FailedInteractiveTests);
        Assert.Empty(vm.DismissedInteractiveTests);
        Assert.False(vm.HasDismissedInteractiveTests);
    }

    /// <summary>
    /// The lists are emptied and refilled whenever results are applied again, so
    /// a decision held only by a list would be back on the next refresh.
    /// </summary>
    [AvaloniaFact]
    public void ADismissedRowStaysGoneWhenTheSameResultsArriveAgain()
    {
        using var _ = new Scope();
        var vm = Loaded();
        var row = vm.FailedInteractiveTests[0];

        vm.DismissInteractiveTestCommand.Execute(row).Subscribe();
        vm.ApplyInteractiveResults(vm.DeviceData.InteractiveTests);

        Assert.DoesNotContain(row, vm.FailedInteractiveTests);
        Assert.Contains(row, vm.DismissedInteractiveTests);
    }

    /// <summary>What the phone measured again is newer than what was decided before.</summary>
    [AvaloniaFact]
    public void ANewRunShowsTheRowAgain()
    {
        using var _ = new Scope();
        var vm = Loaded();

        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();
        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        Assert.Equal(2, vm.FailedInteractiveTests.Count);
        Assert.Empty(vm.DismissedInteractiveTests);
        Assert.False(vm.HasDismissedInteractiveTests);
    }

    /// <summary>
    /// The row arrives from the phone, so the flag that keeps it out of the
    /// report must not arrive with it: a phone that could set it would be able
    /// to hide the very failure the report exists to show.
    /// </summary>
    [AvaloniaFact]
    public void ThePhoneCannotHideItsOwnFailure()
    {
        using var _ = new Scope();
        var vm = new MainWindowViewModel();
        vm.ApplyInteractiveResults(ReadSuite(SuitePayload.Replace(
            """{ "id": "camera", "name": "Camera", "status": "failed",""",
            """{ "id": "camera", "name": "Camera", "excluded": true, "status": "failed",""")));

        Assert.Equal(2, vm.FailedInteractiveTests.Count);
        Assert.Empty(vm.DismissedInteractiveTests);
    }

    /// <summary>
    /// The cross reaches its command through the list that owns the row, and
    /// that path is reflection rather than a compiled binding, so a renamed
    /// command would not fail the build, it would leave a dead button on the
    /// report. This is the test that would notice.
    /// </summary>
    [AvaloniaFact]
    public void TheCrossOnTheRowIsWiredUpAndTakesThatRowOut()
    {
        using var _ = new Scope();
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.WorkflowState = AppWorkflowState.Summary;
        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Button cross = window.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("dismissTest"));

        Assert.NotNull(cross.Command);

        InteractiveTestResult row = vm.FailedInteractiveTests[0];

        // The same entry point a click takes, so the test covers what the
        // operator's finger would actually reach.
        ((ICommand)cross.Command!).Execute(cross.CommandParameter);

        Assert.DoesNotContain(row, vm.FailedInteractiveTests);
        Assert.Same(row, Assert.Single(vm.DismissedInteractiveTests));
    }

    /// <summary>
    /// The report only claims to be hiding rows while it actually is. Shown in
    /// both directions: a line that stayed on after the undo would be the
    /// report saying a second time that it left something out.
    /// </summary>
    [AvaloniaFact]
    public void TheReportMentionsHidingRowsOnlyWhileItIsHidingThem()
    {
        using var _ = new Scope();
        using var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;

        vm.WorkflowState = AppWorkflowState.Summary;
        vm.ApplyInteractiveResults(ReadSuite(SuitePayload));

        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Border line = window.FindControl<Border>("DismissedLine")!;
        Assert.NotNull(line);
        Assert.False(line.IsVisible);

        vm.DismissInteractiveTestCommand.Execute(vm.FailedInteractiveTests[0]).Subscribe();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.True(line.IsVisible);

        vm.RestoreDismissedTestsCommand.Execute().Subscribe();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.False(line.IsVisible);
    }
}
