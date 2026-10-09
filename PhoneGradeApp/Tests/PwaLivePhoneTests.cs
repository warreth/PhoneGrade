using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// A fact that only runs when a phone is attached for it to run on. The
/// environment variable is the ask:
///
///     PHONEGRADE_PWA_DEVICE=AAUF... dotnet test PhoneGradeApp/Tests/Tests.csproj \
///         --filter FullyQualifiedName~PwaLivePhoneTests
///
/// The harness answers the browser prompts itself and drives the steps with
/// real touch events, so the run needs the phone on the desk and nothing else.
/// It writes the suite JSON, the progress snapshot, the browser console and a
/// screenshot per step to PHONEGRADE_PWA_OUT (or a folder under the temp path).
/// </summary>
public sealed class PwaLiveFactAttribute : FactAttribute
{
    public const string Switch = "PHONEGRADE_PWA_DEVICE";

    public PwaLiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Switch)))
            Skip = $"Set {Switch}=<serial> with the phone attached to run the interactive suite on the handset.";
    }
}

public class PwaLivePhoneTests
{
    /// <summary>
    /// Runs the whole interactive suite on the attached phone through its own
    /// browser, the way an operator would. The objective steps are asserted:
    /// the touch grid is really covered, the camera really captures, and the
    /// location step really gets a fix. Every row has to end with a verdict,
    /// and a failed row fails the run: the phone, not the harness, is under
    /// test.
    /// </summary>
    [PwaLiveFact]
    public async Task TheWholeSuiteRunsOnTheAttachedPhone()
    {
        string? serial = Environment.GetEnvironmentVariable(PwaLiveFactAttribute.Switch);
        Assert.False(string.IsNullOrWhiteSpace(serial));

        string output = Environment.GetEnvironmentVariable("PHONEGRADE_PWA_OUT")
            ?? Path.Combine(Path.GetTempPath(), "phonegrade-pwa-live", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        // One step instead of the suite, for iterating on a single screen. The
        // value is a runner test id ("location", "touch", "camera", "powerlock").
        string only = Environment.GetEnvironmentVariable("PHONEGRADE_PWA_TEST") ?? "";

        await using var harness = new PwaPhoneHarness(serial!, output);
        PwaLiveResult result = await harness.RunAsync(new PwaLiveOptions
        {
            Serial = serial!,
            OutputDirectory = output,
            OnlyTestId = only,
        });

        Assert.False(string.IsNullOrWhiteSpace(result.SuiteJson),
            $"the suite never produced a result; artifacts are in {result.ArtifactsDirectory}");

        if (only.Length > 0)
        {
            // A single-step run is for one screen; it has to reach a verdict,
            // and nothing about the rest of the suite is claimed.
            var rows = result.StepStatuses
                .Where(pair => pair.Key.Equals(only, StringComparison.OrdinalIgnoreCase)
                            || pair.Key.StartsWith(only + "-", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(rows.Count > 0, $"{only} produced no rows; artifacts are in {result.ArtifactsDirectory}");
            Assert.All(rows, row => Assert.True(
                row.Value is "passed" or "failed" or "skipped",
                $"{row.Key} has no verdict ('{row.Value}'); artifacts are in {result.ArtifactsDirectory}"));
            return;
        }

        // The steps the harness can measure are the ones it holds the run to.
        Assert.Equal("passed", result.StatusOf("touch"));
        Assert.Equal("passed", result.StatusOf("location"));
        Assert.Equal("passed", result.StatusOf("camera"));

        // Every row has to carry a verdict: a step left running is a driver
        // that did not understand the screen, and a failed row is a phone that
        // needs the technician, not the harness.
        foreach (string testId in PwaPhoneHarness.SuiteTestIds)
        {
            string status = result.StatusOf(testId);
            Assert.True(status is "passed" or "failed" or "skipped",
                $"{testId} has no verdict ('{status}'); artifacts are in {result.ArtifactsDirectory}");
        }

        Assert.DoesNotContain("failed", result.StepStatuses.Values);
    }
}
