using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.UI.Web;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Exercises the progress endpoints over real HTTP against a running server.
///
/// These drive the same routes the phone calls, with the same JSON the phone
/// sends, so a change to a field name or a route breaks here rather than on a
/// handset in the middle of a grading run.
/// </summary>
public class PwaProgressEndpointTests : IAsyncLifetime
{
    private TestRunnerServer? _server;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _server = new TestRunnerServer(preferredPort: 5075);
        await _server.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://localhost:{_server.BoundPort}") };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_server != null) await _server.DisposeAsync();
    }

    private Task<HttpResponseMessage> PostStepAsync(string sessionId, string body) =>
        _client!.PostAsync("/api/pwa/submit-step",
            new StringContent(body, Encoding.UTF8, "application/json"));

    private async Task<JsonElement> GetProgressAsync(string sessionId)
    {
        var resp = await _client!.GetAsync($"/api/pwa/progress?sessionId={sessionId}");
        Assert.True(resp.IsSuccessStatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>A step message shaped exactly like the one the PWA builds.</summary>
    private static string StepBody(string sessionId, string testId, string status, string stamp) => $$"""
        {
          "type": "test_complete",
          "sessionId": "{{sessionId}}",
          "testId": "{{testId}}",
          "testName": "{{testId}}",
          "status": "{{status}}",
          "notes": "",
          "durationMs": 1200,
          "details": {},
          "clientTimestamp": "{{stamp}}"
        }
        """;

    [Fact]
    public async Task ProgressIsEmptyForASessionThatHasNotRun()
    {
        var progress = await GetProgressAsync("FRESH_DEVICE");

        Assert.False(progress.GetProperty("started").GetBoolean());
        Assert.Equal(0, progress.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task AFinishedStepIsStoredAndCanBeReadBack()
    {
        var resp = await PostStepAsync("S1", StepBody("S1", "touch", "Passed",
            "2026-09-30T10:00:00.000Z"));
        Assert.True(resp.IsSuccessStatusCode);

        var progress = await GetProgressAsync("S1");
        var steps = progress.GetProperty("steps");

        Assert.Equal(1, steps.GetArrayLength());
        Assert.Equal("touch", steps[0].GetProperty("testId").GetString());
        Assert.Equal("passed", steps[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ATestStartIsRecordedAsTheStepInFlight()
    {
        await PostStepAsync("S2", """
            { "type": "test_start", "sessionId": "S2", "testId": "camera", "testName": "Camera" }
            """);

        var progress = await GetProgressAsync("S2");
        Assert.Equal("camera", progress.GetProperty("currentTestId").GetString());
    }

    [Fact]
    public async Task AReplayedOldStepIsRefusedOverHttp()
    {
        // This is the whole reason the phone stamps its messages. The retry goes
        // out at 10:05, then the queue from before the reload is flushed and the
        // 10:00 result arrives after it. The 10:00 one must not win.
        await PostStepAsync("S3", StepBody("S3", "touch", "Passed", "2026-09-30T10:05:00.000Z"));
        await PostStepAsync("S3", StepBody("S3", "touch", "Failed", "2026-09-30T10:00:00.000Z"));

        var steps = (await GetProgressAsync("S3")).GetProperty("steps");
        Assert.Equal(1, steps.GetArrayLength());
        Assert.Equal("passed", steps[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ASuiteCompleteFillsInEveryStepAndClosesTheRun()
    {
        await PostStepAsync("S4", StepBody("S4", "touch", "Passed", "2026-09-30T10:00:00.000Z"));

        var suite = """
            {
              "type": "suite_complete",
              "sessionId": "S4",
              "clientTimestamp": "2026-09-30T10:10:00.000Z",
              "payload": {
                "sessionId": "S4",
                "deviceUdid": "S4",
                "userAgent": "Mozilla/5.0 (Linux; Android 17) Chrome/140",
                "platform": "Android",
                "startedAt": "2026-09-30T10:00:00.000Z",
                "completedAt": "2026-09-30T10:10:00.000Z",
                "tests": [
                  { "id": "touch", "name": "Touch", "status": "Passed", "durationMs": 30000, "details": {} },
                  { "id": "camera", "name": "Camera", "status": "Failed", "durationMs": 5000, "details": {} }
                ]
              }
            }
            """;

        var resp = await _client!.PostAsync("/api/pwa/submit",
            new StringContent(suite, Encoding.UTF8, "application/json"));
        Assert.True(resp.IsSuccessStatusCode);

        var progress = await GetProgressAsync("S4");
        var steps = progress.GetProperty("steps");

        Assert.True(progress.GetProperty("finished").GetBoolean());
        Assert.Equal(2, steps.GetArrayLength());
        Assert.Equal(2, progress.GetProperty("totalTests").GetInt32());
    }

    [Fact]
    public async Task ARunAgainReopensAStoredRun()
    {
        await PostStepAsync("S5", StepBody("S5", "touch", "Passed", "2026-09-30T10:00:00.000Z"));
        await _client!.PostAsync("/api/pwa/submit", new StringContent("""
            {
              "type": "suite_complete",
              "sessionId": "S5",
              "clientTimestamp": "2026-09-30T10:10:00.000Z",
              "payload": {
                "sessionId": "S5", "deviceUdid": "S5", "platform": "Android",
                "startedAt": "2026-09-30T10:00:00.000Z", "completedAt": "2026-09-30T10:10:00.000Z",
                "tests": [ { "id": "touch", "name": "Touch", "status": "Passed", "durationMs": 1, "details": {} } ]
              }
            }
            """, Encoding.UTF8, "application/json"));

        Assert.True((await GetProgressAsync("S5")).GetProperty("finished").GetBoolean());

        // The operator pressed "Run Again" on the phone, which clears the flag and
        // starts over. The desktop must stop saying "klaar" straight away.
        await PostStepAsync("S5", """
            { "type": "test_start", "sessionId": "S5", "testId": "touch", "testName": "Touch" }
            """);

        Assert.False((await GetProgressAsync("S5")).GetProperty("finished").GetBoolean());
    }

    [Fact]
    public async Task ResetClearsTheStoredRun()
    {
        await PostStepAsync("S6", StepBody("S6", "touch", "Passed", "2026-09-30T10:00:00.000Z"));

        var resp = await _client!.PostAsync("/api/pwa/progress/reset",
            new StringContent("""{ "sessionId": "S6" }""", Encoding.UTF8, "application/json"));
        Assert.True(resp.IsSuccessStatusCode);

        var progress = await GetProgressAsync("S6");
        Assert.Empty(progress.GetProperty("steps").EnumerateArray());
        Assert.False(progress.GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task ResetLeavesOtherDevicesAlone()
    {
        await PostStepAsync("S7", StepBody("S7", "touch", "Passed", "2026-09-30T10:00:00.000Z"));
        await PostStepAsync("S8", StepBody("S8", "touch", "Passed", "2026-09-30T10:00:00.000Z"));

        await _client!.PostAsync("/api/pwa/progress/reset",
            new StringContent("""{ "sessionId": "S7" }""", Encoding.UTF8, "application/json"));

        Assert.Empty((await GetProgressAsync("S7")).GetProperty("steps").EnumerateArray());
        Assert.Single((await GetProgressAsync("S8")).GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public async Task ActiveSessionAnswersForAHomeScreenLaunch()
    {
        // Launched from the home screen the manifest sends the browser to "/" with
        // no sessionId, so the page has to ask which device it is grading.
        var before = await _client!.GetAsync("/api/pwa/active-session");
        Assert.True(before.IsSuccessStatusCode);
        Assert.Equal("UNKNOWN",
            JsonDocument.Parse(await before.Content.ReadAsStringAsync())
                .RootElement.GetProperty("sessionId").GetString());

        _server!.SetActiveSession("38091FDJG00EMF");

        var after = JsonDocument.Parse(
            await (await _client.GetAsync("/api/pwa/active-session")).Content.ReadAsStringAsync());

        Assert.Equal("38091FDJG00EMF", after.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task AMalformedStepBodyDoesNotTakeTheServerDown()
    {
        // The phone does not read response codes, so a 500 here would be dropped
        // silently on the client and the whole run would stop reporting.
        var resp = await PostStepAsync("S9", "{ this is not json");

        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        Assert.False((await GetProgressAsync("S9")).GetProperty("started").GetBoolean());
    }

    [Fact]
    public async Task AMalformedResetBodyClearsThePlaceholderSessionNotTheRealOne()
    {
        await PostStepAsync("S10", StepBody("S10", "touch", "Passed", "2026-09-30T10:00:00.000Z"));

        var resp = await _client!.PostAsync("/api/pwa/progress/reset",
            new StringContent("nonsense", Encoding.UTF8, "application/json"));
        Assert.True(resp.IsSuccessStatusCode);

        // The unreadable body must not be read as a session id, so the real
        // device keeps its run.
        Assert.Single((await GetProgressAsync("S10")).GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public async Task TheProgressChangedEventOnlyFiresForFreshResults()
    {
        var fired = 0;
        _server!.ProgressChanged += (s, e) => fired++;

        await PostStepAsync("S11", StepBody("S11", "touch", "Passed", "2026-09-30T10:05:00.000Z"));
        await PostStepAsync("S11", StepBody("S11", "touch", "Failed", "2026-09-30T10:00:00.000Z"));

        // The desktop redraws on this event, so a replay firing it would make the
        // status flicker on every reconnect for no change in the numbers.
        Assert.Equal(1, fired);
    }
}
