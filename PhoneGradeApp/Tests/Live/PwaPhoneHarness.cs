using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoneGrade.Core;
using PhoneGrade.UI.Web;
using SkiaSharp;

namespace PhoneGrade.Tests;

/// <summary>How one live PWA run on a phone is configured.</summary>
internal sealed class PwaLiveOptions
{
    public required string Serial { get; init; }
    public required string OutputDirectory { get; init; }
    public string Language { get; init; } = "nl";
    public int Port { get; init; } = 5055;
    public int TimeoutMinutes { get; init; } = 12;

    /// <summary>
    /// Runs one step instead of the whole suite. The id is the one the runner
    /// knows ("location", "touch", "camera"). Empty means the whole suite.
    /// </summary>
    public string OnlyTestId { get; init; } = "";

    /// <summary>
    /// Only lists and probes the cameras the browser exposes, and writes what
    /// it found. Nothing is photographed through the suite and no verdict is
    /// pressed, so this is the quiet way to see what lenses the phone offers.
    /// </summary>
    public bool ProbeCameras { get; init; }
}

/// <summary>What a live PWA run produced.</summary>
internal sealed class PwaLiveResult
{
    public required string ArtifactsDirectory { get; init; }
    public required string SuiteJson { get; init; }
    public string ProgressJson { get; init; } = "";
    public IReadOnlyList<string> ConsoleEvents { get; init; } = [];
    public IReadOnlyList<string> Log { get; init; } = [];
    public IReadOnlyDictionary<string, string> StepStatuses { get; init; } = new Dictionary<string, string>();

    /// <summary>The statuses of one test row, or "" when the row is not in the suite.</summary>
    public string StatusOf(string testId) =>
        StepStatuses.TryGetValue(testId, out string? status) ? status : "";
}

/// <summary>
/// Runs the interactive PWA suite on a phone that is plugged in, through the
/// browser the phone actually uses, and drives it the way a person would:
/// real touch events at the coordinates of the buttons, a real power-button
/// cycle for the lock step, and a real screen rotation for the rotation step.
///
/// The suite page polls its desktop server over the REST channel, so the
/// harness starts a real <see cref="TestRunnerServer"/> on the pc, opens the
/// page over an adb reverse tunnel (which gives the browser a trusted origin,
/// so geolocation and the camera are available), and then talks to the page
/// over the Chrome DevTools Protocol. Screenshots are taken with adb so they
/// show the panel as it really is.
///
/// The harness does not pretend to be a person where a person is required: it
/// reads objective data (microphone peak, captured frames, camera photo
/// brightness, display colour in the screenshot) and answers the questions it
/// can answer, leaving a log line for every step so the run is auditable.
/// </summary>
internal sealed class PwaPhoneHarness : IAsyncDisposable
{
    /// <summary>
    /// The run's log, written to disk as it happens. A test host that crashes
    /// under memory pressure still leaves the story of the run behind, which a
    /// log written at the end does not.
    /// </summary>
    private sealed class LiveLog : List<string>
    {
        public string? FilePath { get; set; }

        public new void Add(string line)
        {
            base.Add(line);

            if (FilePath is null) return;
            try { File.AppendAllText(FilePath, line + Environment.NewLine); }
            catch { /* the log is best effort, never a reason to fail a run */ }
        }
    }

    private readonly LiveLog _log = [];
    private readonly string _serial;
    private readonly string _outputDirectory;
    private readonly string _screenshotDirectory;
    private TestRunnerServer? _server;
    private CdpSession? _cdp;
    private AdbReverseLease? _reverse;

    public PwaPhoneHarness(string serial, string outputDirectory)
    {
        _serial = serial;
        _outputDirectory = outputDirectory;
        _screenshotDirectory = Path.Combine(outputDirectory, "screenshots");
    }

    public async Task<PwaLiveResult> RunAsync(PwaLiveOptions options, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDirectory);
        Directory.CreateDirectory(_screenshotDirectory);
        _log.FilePath = Path.Combine(_outputDirectory, "harness.log");

        try
        {
            return await RunCoreAsync(options, ct);
        }
        catch (Exception ex)
        {
            _log.Add($"run failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(_outputDirectory, "harness.log"), _log, CancellationToken.None);
        }
    }

    private async Task<PwaLiveResult> RunCoreAsync(PwaLiveOptions options, CancellationToken ct)
    {        int port = await StartServerAsync(options, ct);
        string origin = $"http://localhost:{port}";
        // No debug console: its overlay covers the bottom of the page, and the
        // touch grid and the display card's buttons live there.
        string url = $"{origin}/?sessionId={Uri.EscapeDataString(options.Serial)}&lang={options.Language}";
        _pageUrl = url;

        _log.Add($"server on port {port}, opening {url}");

        // A trusted origin is what makes the browser hand out geolocation and
        // the camera without a dialog, and adb reverse is what turns localhost
        // into the pc's own loopback.
        _reverse = await AdbReverseLease.OpenAsync(_serial, port, _log.Add);

        string locationModeBefore = await SetLocationServicesAsync(enable: true);

        try
        {
            await ShellAsync("input keyevent 224");
            await OpenInBrowserAsync(url);
            await ConnectToPageAsync(port, options, ct);
            await GrantPermissionsAsync(origin, ct);
            await PreparePageAsync(url, options.Serial, ct);
            await WaitForPageReadyAsync(ct);

            if (options.ProbeCameras)
            {
                string probeJson = await ProbeCamerasAsync(ct);
                await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "cameras.json"), Pretty(probeJson), ct);
                _log.Add($"camera probe written to {Path.Combine(_outputDirectory, "cameras.json")}");

                return new PwaLiveResult
                {
                    ArtifactsDirectory = _outputDirectory,
                    SuiteJson = "{}",
                    Log = _log.ToArray(),
                    ConsoleEvents = _cdp!.Events,
                };
            }

            await _cdp!.TryEvaluateAsync(
                options.OnlyTestId.Length > 0
                    ? $"window.testRunner.startTest({System.Text.Json.JsonSerializer.Serialize(options.OnlyTestId)})"
                    : "window.testRunner.startSuite({ resume: false })",
                awaitPromise: false);
            _log.Add(options.OnlyTestId.Length > 0 ? $"step {options.OnlyTestId} started" : "suite started");

            await DriveUntilFinishedAsync(options, ct);

            string suiteJson = await _cdp!.EvaluateStringAsync("JSON.stringify(window.testRunner.buildSuiteResult())") ?? "";
            string progressJson = await ReadProgressAsync(port, options.Serial, ct);

            var result = new PwaLiveResult
            {
                ArtifactsDirectory = _outputDirectory,
                SuiteJson = suiteJson,
                ProgressJson = progressJson,
                ConsoleEvents = _cdp!.Events,
                Log = _log.ToArray(),
                StepStatuses = ParseStatuses(suiteJson),
            };

            await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "result.json"), Pretty(suiteJson), ct);
            await File.WriteAllTextAsync(Path.Combine(_outputDirectory, "progress.json"), Pretty(progressJson), ct);
            await File.WriteAllLinesAsync(Path.Combine(_outputDirectory, "console.log"), result.ConsoleEvents, ct);
            await File.WriteAllLinesAsync(Path.Combine(_outputDirectory, "harness.log"), _log, ct);

            _log.Add($"artifacts written to {_outputDirectory}");
            return result;
        }
        finally
        {
            await RestoreLocationServicesAsync(locationModeBefore);
            if (_reverse is not null) { await _reverse.DisposeAsync(); _reverse = null; }
            if (_cdp is not null) { await _cdp.DisposeAsync(); _cdp = null; }
            if (_server is not null) { await _server.DisposeAsync(); _server = null; }
        }
    }

    /// <summary>Names of every step that produces rows, in suite order.</summary>
    public static readonly string[] SuiteTestIds =
    [
        "touch", "forcetouch", "display", "rotation", "speaker", "microphone",
        "call", "camera", "sensor", "location", "vibration",
        "powerlock-power", "powerlock-biometric", "cosmetic-back", "cosmetic-screen",
        "cosmetic-camera-glass", "cosmetic-volume-up", "cosmetic-volume-down",
    ];

    private async Task<int> StartServerAsync(PwaLiveOptions options, CancellationToken ct)
    {
        _server = new TestRunnerServer(options.Port);
        await _server.StartAsync(ct);
        return _server.BoundPort;
    }

    private async Task OpenInBrowserAsync(string url)
    {
        // The default browser is the one the phone actually has configured; the
        // intent goes through that, so a run tests what the operator will use.
        // The component is remembered so the browser can be brought back to the
        // front after the dialer has had its turn.
        var (resolved, _, _) = await ToolRunner.ExecuteAsync("adb",
            $"-s {_serial} shell cmd package resolve-activity --brief -a android.intent.action.VIEW -d \"{url}\"");
        _browserComponent = resolved.Split('\n')
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Contains('/') && !line.Contains(' ')) ?? "";

        var (_, _, exit) = await ToolRunner.ExecuteAsync("adb",
            $"-s {_serial} shell am start -a android.intent.action.VIEW -d \"{url}\"");
        if (exit != 0) throw new InvalidOperationException($"could not open the browser on {_serial}");

        // The browser exposes its DevTools socket a moment after it starts.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(500);
            var (sockets, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell cat /proc/net/unix");
            string? socket = Regex.Matches(sockets, @"@(\S*chrome_devtools_remote\S*)")
                .Select(m => m.Groups[1].Value)
                .FirstOrDefault();
            if (socket is not null)
            {
                _devToolsSocket = socket;
                return;
            }
        }

        throw new InvalidOperationException("the browser never opened a DevTools socket; is it running?");
    }

    private string _browserComponent = "";

    /// <summary>
    /// Puts the browser back on top of whatever a step opened.
    ///
    /// The dialer step starts a second app, and a browser in the background
    /// gets no camera frames, no motion events and no vibration: everything
    /// after that step would be measured against a page the phone is not
    /// showing. A person switches back to the browser; so does the harness.
    /// </summary>
    private async Task EnsureBrowserForegroundAsync(CancellationToken ct)
    {
        if (_browserComponent.Length == 0) return;

        string package = _browserComponent.Split('/')[0];
        var (top, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell dumpsys activity activities");
        string? topLine = top.Split('\n').FirstOrDefault(line => line.Contains("topResumedActivity"));
        if (topLine is not null && topLine.Contains(package, StringComparison.OrdinalIgnoreCase)) return;

        await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell am start -n {_browserComponent}");
        await ShellAsync("input keyevent 224");
        await Task.Delay(700, ct);
        _log.Add("browser brought back to the front");
    }

    private string _devToolsSocket = "";
    private int _localForwardPort = 9300;

    private async Task ConnectToPageAsync(int port, PwaLiveOptions options, CancellationToken ct)
    {
        // Each chrome_devtools_remote socket is forwarded to its own local port
        // so a second browser on the phone cannot take the target away.
        await ToolRunner.ExecuteAsync("adb", $"-s {_serial} forward tcp:{_localForwardPort} localabstract:{_devToolsSocket}");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        string? webSocketUrl = null;

        for (int attempt = 0; attempt < 40 && webSocketUrl is null; attempt++)
        {
            try
            {
                string json = await http.GetStringAsync($"http://127.0.0.1:{_localForwardPort}/json", ct);
                using var document = JsonDocument.Parse(json);

                // A phone that has run the suite before still has the old tab
                // open, and its url matches the plain address as well. The tab
                // carrying this session is the one that was just opened.
                string preferred = "";
                string fallback = "";

                foreach (JsonElement target in document.RootElement.EnumerateArray())
                {
                    if (!target.TryGetProperty("url", out JsonElement targetUrl)) continue;
                    if (!target.TryGetProperty("webSocketDebuggerUrl", out JsonElement ws)) continue;
                    string url = targetUrl.GetString() ?? "";
                    if (!url.Contains($"localhost:{port}", StringComparison.OrdinalIgnoreCase)) continue;

                    if (url.Contains(options.Serial, StringComparison.OrdinalIgnoreCase) && preferred.Length == 0)
                        preferred = ws.GetString() ?? "";
                    else if (fallback.Length == 0)
                        fallback = ws.GetString() ?? "";
                }

                webSocketUrl = preferred.Length > 0 ? preferred : fallback.Length > 0 ? fallback : null;
            }
            catch (Exception ex)
            {
                _log.Add($"devtools endpoint not up yet: {ex.Message}");
            }

            if (webSocketUrl is null) await Task.Delay(500, ct);
        }

        if (webSocketUrl is null) throw new InvalidOperationException("the PWA page never appeared in the browser's targets");

        _cdp = await CdpSession.ConnectAsync(webSocketUrl, ct);
        await _cdp.SendAsync("Runtime.enable", ct: ct);
        await _cdp.SendAsync("Page.enable", ct: ct);
        await _cdp.SendAsync("Log.enable", ct: ct);
        _log.Add($"attached to the page at {webSocketUrl}");
    }

    private async Task GrantPermissionsAsync(string origin, CancellationToken ct)
    {
        // A person would tap "allow" on the location and camera prompts. The
        // browser can be told the same thing over DevTools, which keeps the run
        // unattended without changing what the page is allowed to do.
        try
        {
            await _cdp!.SendAsync("Browser.grantPermissions", new Dictionary<string, object?>
            {
                ["origin"] = origin,
                ["permissions"] = new[] { "geolocation", "videoCapture", "audioCapture" },
            }, ct);
            _log.Add("browser permissions granted for " + origin);
        }
        catch (Exception ex)
        {
            // Older builds only know the single-permission call.
            try
            {
                await _cdp!.SendAsync("Browser.setPermission", new Dictionary<string, object?>
                {
                    ["permission"] = new Dictionary<string, object?> { ["name"] = "geolocation" },
                    ["setting"] = "granted",
                    ["origin"] = origin,
                }, ct);
                _log.Add("geolocation granted through Browser.setPermission");
            }
            catch (Exception inner)
            {
                _log.Add($"could not grant permissions over DevTools: {ex.Message} / {inner.Message}");
            }
        }

        // Whether the page now sees the grant is the difference between a
        // geolocation request and a permission dialog nobody can press.
        string state = await _cdp!.EvaluateStringAsync(
            "navigator.permissions && navigator.permissions.query ? navigator.permissions.query({ name: 'geolocation' }).then(p => p.state).catch(e => 'error: ' + e.message) : 'no permissions api'", awaitPromise: true) ?? "";
        _log.Add($"geolocation permission state: {state}");
    }

    /// <summary>
    /// Forces the page onto the run's own url with nothing cached in front of
    /// it.
    ///
    /// A phone that has run the suite before carries a service worker that
    /// serves the previous build out of its own cache, and a tab left open from
    /// an earlier session holds the old page object. Both would make a run look
    /// at code that was never just built, so the worker is unregistered, its
    /// caches are dropped, and the page is navigated fresh.
    /// </summary>
    private async Task PreparePageAsync(string url, string sessionId, CancellationToken ct)
    {
        try
        {
            // The debug console is remembered in localStorage by an earlier
            // run that asked for it, and its overlay covers the bottom of the
            // page where the touch grid and the display buttons live.
            await _cdp!.TryEvaluateAsync("localStorage.removeItem('pg_debug'); true");
            await _cdp.TryEvaluateAsync(
                "navigator.serviceWorker ? navigator.serviceWorker.getRegistrations().then(rs => Promise.all(rs.map(r => r.unregister()))).then(() => true) : true");
            await _cdp.TryEvaluateAsync(
                "typeof caches !== 'undefined' ? caches.keys().then(keys => Promise.all(keys.map(k => caches.delete(k)))).then(() => true) : true");
            await _cdp.SendAsync("Page.navigate", new Dictionary<string, object?> { ["url"] = url }, ct);
            _log.Add($"navigated a fresh page for session {sessionId}");
        }
        catch (Exception ex)
        {
            _log.Add($"could not prepare a fresh page: {ex.Message}");
        }
    }

    /// <summary>
    /// Lists the video inputs the browser exposes, then opens each one for a
    /// moment and reports its settings and how bright its first frames are.
    ///
    /// This is the quiet answer to "does the phone have lenses the suite never
    /// opened": it opens cameras but presses nothing, so no verdict and no
    /// haptics are involved.
    /// </summary>
    private async Task<string> ProbeCamerasAsync(CancellationToken ct)
    {
        string expression = """
            (async () => {
              const out = { first: null, devices: [], cameras: [] };
              try {
                const first = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
                const track = first.getVideoTracks()[0];
                out.first = track.getSettings ? track.getSettings() : null;
                const devices = await navigator.mediaDevices.enumerateDevices();
                out.devices = devices.filter(d => d.kind === 'videoinput').map(d => ({ deviceId: d.deviceId, label: d.label }));
                first.getTracks().forEach(t => t.stop());
                await new Promise(r => setTimeout(r, 400));
                for (const d of out.devices) {
                  try {
                    const s = await navigator.mediaDevices.getUserMedia({ video: { deviceId: { exact: d.deviceId } } });
                    const t = s.getVideoTracks()[0];
                    const settings = t.getSettings ? t.getSettings() : {};
                    const v = document.createElement('video');
                    v.muted = true; v.playsInline = true; v.srcObject = s;
                    await v.play().catch(() => {});
                    await new Promise(r => setTimeout(r, 1200));
                    let brightness = -1;
                    if (v.videoWidth) {
                      const c = document.createElement('canvas'); c.width = 32; c.height = 32;
                      const x = c.getContext('2d'); x.drawImage(v, 0, 0, 32, 32);
                      const data = x.getImageData(0, 0, 32, 32).data;
                      let m = 0; for (let k = 0; k < data.length; k += 4) m = Math.max(m, data[k], data[k + 1], data[k + 2]);
                      brightness = m;
                    }
                    out.cameras.push({ deviceId: d.deviceId, label: d.label, settings, videoWidth: v.videoWidth, videoHeight: v.videoHeight, brightness });
                    s.getTracks().forEach(tr => tr.stop());
                    await new Promise(r => setTimeout(r, 400));
                  } catch (e) {
                    out.cameras.push({ deviceId: d.deviceId, label: d.label, error: (e && e.name) || String(e) });
                  }
                }
              } catch (e) {
                out.error = (e && e.name) || String(e);
              }
              return JSON.stringify(out);
            })()
            """;

        string json = await _cdp!.EvaluateStringAsync(expression, awaitPromise: true) ?? "";
        _log.Add("camera probe: " + (json.Length > 800 ? json[..800] + "..." : json));
        return json;
    }

    private async Task WaitForPageReadyAsync(CancellationToken ct)
    {
        string lastDiagnostics = "";

        for (int attempt = 0; attempt < 60; attempt++)
        {
            string ready = await _cdp!.EvaluateStringAsync("document.readyState") ?? "";
            bool runner = await _cdp.EvaluateBoolAsync("!!window.testRunner");

            // What the page is showing is the only useful clue when the runner
            // never appears, so it is kept for the failure line.
            lastDiagnostics = await _cdp.EvaluateStringAsync(
                "location.href + ' | ' + document.title + ' | ' + (document.body ? document.body.innerText.replace(/\\s+/g, ' ').slice(0, 220) : '')") ?? "";

            if (attempt % 5 == 0 && lastDiagnostics.Length > 0)
                _log.Add($"page state: {lastDiagnostics}");

            if (ready == "complete" && runner)
            {
                _log.Add("the page is up and the runner is connected");
                return;
            }

            await Task.Delay(500, ct);
        }

        throw new InvalidOperationException($"the PWA never reached a ready state with a connected runner; last page state: {lastDiagnostics}");
    }

    // ---------------------------------------------------------------- driving

    private readonly HashSet<string> _done = [];
    private int _lastStepIndex = -1;
    private string _lastStepKey = "";
    private string _pageUrl = "";

    private async Task DriveUntilFinishedAsync(PwaLiveOptions options, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(options.TimeoutMinutes);
        bool sawRunning = false;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            JsonElement? state = await ReadStateAsync();
            if (state is null)
            {
                await Task.Delay(500, ct);
                continue;
            }

            bool running = state.Value.TryGetProperty("running", out JsonElement runningElement) && runningElement.ValueKind == JsonValueKind.True;
            int index = state.Value.TryGetProperty("index", out JsonElement indexElement) && indexElement.TryGetInt32(out int parsedIndex) ? parsedIndex : -1;
            string id = state.Value.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() ?? "" : "";
            string status = state.Value.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.String ? statusElement.GetString() ?? "" : "";

            if (running) sawRunning = true;

            if (index >= 0 && (index != _lastStepIndex || id != _lastStepKey))
            {
                if (_lastStepIndex >= 0) await ScreenshotAsync($"{_lastStepIndex:D2}-{_lastStepKey}-{_lastStepStatus}.png");
                _lastStepIndex = index;
                _lastStepKey = id;
                _lastStepStatus = status;
                _done.Clear();
                _log.Add($"step {index} ({id}) started");
                await ScreenshotAsync($"{index:D2}-{id}-start.png");

                // The dialer step sends the phone to another app, and every
                // step after it measures a browser that has to be on screen
                // again. The call step itself is the one that opens that app.
                if (id != "call") await EnsureBrowserForegroundAsync(ct);
            }
            else if (index >= 0 && status != _lastStepStatus)
            {
                _lastStepStatus = status;
                _log.Add($"step {index} ({id}) became {status}");
                await ScreenshotAsync($"{index:D2}-{id}-{status}.png");
            }

            if (!running)
            {
                if (!sawRunning)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                // The runner says it is not running. That is either the end of
                // the suite or a page that was reloaded under it (opening the
                // dialer, a browser restart). The difference is whether the
                // tests in this page instance ever ran: a fresh page has all of
                // them pending, and the progress the pc kept is what the run is
                // resumed from.
                string suiteJson = await _cdp!.EvaluateStringAsync("JSON.stringify(window.testRunner.buildSuiteResult())") ?? "";
                var statuses = ParseStatuses(suiteJson);
                bool anyTerminal = statuses.Values.Any(s => s is "passed" or "failed" or "skipped");

                if (anyTerminal)
                {
                    _log.Add("the suite finished");
                    return;
                }

                _log.Add("the page lost its run; resuming from the progress the server kept");
                await PreparePageAsync(_pageUrl, options.Serial, ct);
                await WaitForPageReadyAsync(ct);
                await _cdp.TryEvaluateAsync("window.testRunner.startSuite({ resume: true })", awaitPromise: false);
                await Task.Delay(1500, ct);
                continue;
            }

            if (index >= 0 && status == "running")
            {
                try
                {
                    await DriveStepAsync(index, id, ct);
                }
                catch (Exception ex)
                {
                    _log.Add($"step {index} ({id}) driver error: {ex.Message}");
                }
            }

            await Task.Delay(250, ct);
        }

        _log.Add("the harness gave up waiting for the suite to finish");
    }

    private string _lastStepStatus = "";

    private async Task<JsonElement?> ReadStateAsync()
    {
        return await _cdp!.TryEvaluateAsync(
            "(() => { const r = window.testRunner; if (!r) return null; const i = r.currentTestIndex; const t = (i >= 0 && r.tests) ? r.tests[i] : null; return { running: r.isRunning, index: i, id: t ? t.id : null, status: t ? t.status : null }; })()");
    }

    private async Task DriveStepAsync(int index, string id, CancellationToken ct)
    {
        switch (id)
        {
            case "touch": await DriveTouchAsync(ct); break;
            case "forcetouch": await DriveForceTouchAsync(ct); break;
            case "display": await DriveDisplayAsync(ct); break;
            case "rotation": await DriveRotationAsync(ct); break;
            case "speaker": await DriveSpeakerAsync(ct); break;
            case "microphone": await DriveMicrophoneAsync(ct); break;
            case "call": await DriveCallAsync(ct); break;
            case "camera": await DriveCameraAsync(ct); break;
            case "sensor": await DriveSensorAsync(ct); break;
            case "location": await DriveLocationAsync(ct); break;
            case "vibration": await DriveVibrationAsync(ct); break;
            case "powerlock": await DrivePowerLockAsync(ct); break;
            case "cosmetic": await DriveCosmeticAsync(ct); break;
        }
    }

    // ------------------------------------------------------------- step logic

    private async Task DriveTouchAsync(CancellationToken ct)
    {
        if (!_done.Add("touch:sweep")) return;

        JsonElement? cells = await _cdp!.TryEvaluateAsync(
            "Array.from(document.querySelectorAll('#touch-grid .touch-cell')).map(c => { const r = c.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })");

        if (cells is null || cells.Value.ValueKind != JsonValueKind.Array) return;

        var points = cells.Value.EnumerateArray()
            .Where(p => p.ValueKind == JsonValueKind.Object)
            .Select(p => (X: p.GetProperty("x").GetDouble(), Y: p.GetProperty("y").GetDouble()))
            .ToList();

        if (points.Count == 0) return;

        _log.Add($"touch: sweeping {points.Count} cells");

        var finger = new Dictionary<string, object?> { ["x"] = points[0].X, ["y"] = points[0].Y, ["id"] = 0, ["radiusX"] = 2, ["radiusY"] = 2 };
        await _cdp.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchStart", finger), ct);

        foreach (var point in points.Skip(1))
        {
            finger["x"] = point.X;
            finger["y"] = point.Y;
            await _cdp.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchMove", finger), ct);
        }

        await _cdp.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchEnd", null), ct);
        // The step settles on its own 100 ms timer.
        await Task.Delay(800, ct);
    }

    private static Dictionary<string, object?> TouchEvent(string type, Dictionary<string, object?>? point) => new()
    {
        ["type"] = type,
        ["touchPoints"] = point is null ? Array.Empty<object>() : new object[] { point },
    };

    private async Task DriveForceTouchAsync(CancellationToken ct)
    {
        if (!_done.Add("forcetouch:press")) return;

        // The step only accepts real pressure, so the simulated finger carries
        // one: a person leaning on the glass is what the check is for.
        JsonElement? point = await PointAsync("#pressure-area");
        if (point is null) return;

        var finger = new Dictionary<string, object?>
        {
            ["x"] = point.Value.GetProperty("x").GetDouble(),
            ["y"] = point.Value.GetProperty("y").GetDouble(),
            ["id"] = 0,
            ["radiusX"] = 12,
            ["radiusY"] = 12,
            ["force"] = 0.9,
        };

        await _cdp!.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchStart", finger), ct);
        await Task.Delay(200, ct);
        await _cdp.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchMove", finger), ct);
        await Task.Delay(200, ct);
        await _cdp.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchEnd", null), ct);

        // If the browser did not honour the pressure, skip rather than fail the
        // phone for a limitation of the harness.
        await Task.Delay(1200, ct);
        string status = await _cdp.EvaluateStringAsync("window.testRunner.tests[1].status") ?? "";
        if (status == "running")
        {
            await TapAsync("#skip-pressure", ct);
            _log.Add("forcetouch: the browser reported no pressure; skipped through the step's own skip button");
        }
    }

    private async Task DriveDisplayAsync(CancellationToken ct)
    {
        if (!_done.Contains("display:brightness") && await WaitForSelectorAsync("#brightness-ok", 3000, ct))
        {
            _done.Add("display:brightness");
            await TapAsync("#brightness-ok", ct);
            _log.Add("display: brightness confirmed");
        }

        // The five colours follow one another and the overlay says which is up.
        if (_displayColorIndex < 5 && !_done.Contains($"display:color-{_displayColorIndex}"))
        {
            string colour = await _cdp!.EvaluateStringAsync(
                "(() => { const el = document.querySelector('.display-test-fullscreen'); return el ? getComputedStyle(el).backgroundColor : ''; })()") ?? "";

            if (colour.Length > 0)
            {
                await Task.Delay(250, ct);
                string shot = await ScreenshotAsync($"display-color-{_displayColorIndex}.png");
                var (red, green, blue) = AverageCentre(shot);
                bool matches = ExpectedDisplayColour(_displayColorIndex, red, green, blue);

                _log.Add($"display: colour {_displayColorIndex} measured rgb({red},{green},{blue}) from '{colour}', {(matches ? "good" : "defective")}");

                _done.Add($"display:color-{_displayColorIndex}");
                _displayColorIndex++;
                await TapAsync(matches ? "#color-ok" : "#color-defect", ct);
                await Task.Delay(400, ct);
            }
        }
    }

    private int _displayColorIndex;

    /// <summary>The colours in the order DisplayInspection walks them.</summary>
    private static bool ExpectedDisplayColour(int index, int red, int green, int blue) => index switch
    {
        0 => red > 200 && green > 200 && blue > 200, // white
        1 => red > 180 && green < 80 && blue < 80,   // red
        2 => red < 80 && green > 180 && blue < 80,   // green
        3 => red < 80 && green < 80 && blue > 180,   // blue
        4 => red < 60 && green < 60 && blue < 60,    // black
        _ => false,
    };

    private async Task DriveRotationAsync(CancellationToken ct)
    {
        if (_done.Contains("rotation:turned")) return;

        if (await WaitForSelectorAsync("#orientation-display", 3000, ct))
        {
            _done.Add("rotation:turned");
            _log.Add("rotation: turning the screen like a person would");
            await ShellAsync("settings put system accelerometer_rotation 0");
            await ShellAsync("settings put system user_rotation 1");
            await Task.Delay(1800, ct);
            await ShellAsync("settings put system user_rotation 0");
            await Task.Delay(1800, ct);
            await ShellAsync("settings put system accelerometer_rotation 1");
        }
    }

    private async Task DriveSpeakerAsync(CancellationToken ct)
    {
        // Earpiece first, then loudspeaker, the way the step walks them.
        if (!_done.Contains("speaker:earpiece-play") && await WaitForSelectorAsync("#play-earpiece-btn", 3000, ct))
        {
            _done.Add("speaker:earpiece-play");
            await TapAsync("#play-earpiece-btn", ct);
            await Task.Delay(1500, ct);
        }

        if (_done.Contains("speaker:earpiece-play") && !_done.Contains("speaker:earpiece-answer")
            && await WaitForSelectorAsync("#earpiece-feedback:not([hidden])", 3000, ct))
        {
            _done.Add("speaker:earpiece-answer");
            bool plays = ParseInt(await _cdp!.EvaluateStringAsync("String(window.testRunner.tests[4].plays.earpiece)")) > 0;
            bool problem = !string.IsNullOrEmpty(await _cdp.EvaluateStringAsync("String(window.testRunner.tests[4].audioProblem || '')"));
            _log.Add($"speaker: earpiece played {plays}, audio problem {problem}");
            await TapAsync(plays && !problem ? "#earpiece-yes" : "#earpiece-no", ct);
            await Task.Delay(600, ct);
        }

        if (_done.Contains("speaker:earpiece-answer") && !_done.Contains("speaker:loud-play")
            && await WaitForSelectorAsync("#loudspeaker-section:not(.is-locked)", 3000, ct)
            && await WaitForSelectorAsync("#play-loud-btn", 3000, ct))
        {
            _done.Add("speaker:loud-play");
            await TapAsync("#play-loud-btn", ct);
            await Task.Delay(1500, ct);
        }

        if (_done.Contains("speaker:loud-play") && !_done.Contains("speaker:loud-answer")
            && await WaitForSelectorAsync("#loud-feedback:not([hidden])", 3000, ct))
        {
            _done.Add("speaker:loud-answer");
            bool plays = ParseInt(await _cdp!.EvaluateStringAsync("String(window.testRunner.tests[4].plays.loudspeaker)")) > 0;
            _log.Add($"speaker: loudspeaker played {plays}");
            await TapAsync(plays ? "#loud-yes" : "#loud-no", ct);
        }
    }

    private async Task DriveMicrophoneAsync(CancellationToken ct)
    {
        // Multiple inputs are picked by a person; the first listed one is what
        // they would pick unless they know better.
        if (await WaitForSelectorAsync("#mic-select", 1500, ct) && !_done.Contains("mic:select"))
        {
            _done.Add("mic:select");
            await _cdp!.TryEvaluateAsync(
                "(() => { const s = document.querySelector('#mic-select'); if (!s) return false; const opt = Array.from(s.options).find(o => !o.disabled && o.value); if (!opt) return false; s.value = opt.value; s.dispatchEvent(new Event('change', { bubbles: true })); return true; })()");
            await Task.Delay(300, ct);
            await TapAsync("#mic-select-confirm", ct);
            _log.Add("microphone: picked the first input");
            return;
        }

        if (!_done.Contains("mic:answer") && await WaitForSelectorAsync("#mic-playback", 2500, ct))
        {
            _done.Add("mic:answer");

            // The peak is shown on the card and kept by the step's meter; the
            // answer follows the measurement rather than the harness guessing.
            string peakText = await _cdp!.EvaluateStringAsync(
                "(() => { const t = document.querySelector('.step-lead'); const m = t ? t.textContent.match(/(\\d+)\\s*%/) : null; return m ? m[1] : ''; })()") ?? "";
            int peak = ParseInt(peakText);
            _log.Add($"microphone: recorded peak {peak}%");
            await TapAsync(peak >= 20 ? "#mic-yes" : "#mic-no", ct);
            return;
        }

        if (!_done.Contains("mic:trouble") && await WaitForSelectorAsync("#mic-retry", 1500, ct))
        {
            _done.Add("mic:trouble");
            await TapAsync("#mic-retry", ct);
            _log.Add("microphone: retried after a refused prompt");
        }
    }

    private async Task DriveCallAsync(CancellationToken ct)
    {
        if (_done.Add("call:open"))
        {
            if (await WaitForSelectorAsync("#make-call-btn", 3000, ct))
            {
                await TapAsync("#make-call-btn", ct);
                await Task.Delay(1500, ct);

                // This phone has more than one app that answers a tel: link, so
                // Android asks which one to use. A person dismisses that chooser
                // and is back on the page; that is what the question on the card
                // is about.
                await DismissChooserAsync(ct);

                // The click can be swallowed by the chooser appearing over it.
                // Asking the anchor for its click is what a person does when
                // they tap it a second time.
                bool feedbackVisible = await _cdp!.EvaluateBoolAsync(
                    "(() => { const f = document.querySelector('#call-feedback'); return !!f && getComputedStyle(f).display !== 'none'; })()");

                if (!feedbackVisible)
                {
                    await _cdp.TryEvaluateAsync("document.querySelector('#make-call-btn').click()");
                    await Task.Delay(1200, ct);
                    await DismissChooserAsync(ct);
                }

                _log.Add("call: dial link pressed");
            }
        }

        if (!_done.Contains("call:answer") && await WaitForSelectorAsync("#btn-call-yes", 2000, ct))
        {
            bool visible = await _cdp!.EvaluateBoolAsync(
                "(() => { const f = document.querySelector('#call-feedback'); return !!f && getComputedStyle(f).display !== 'none'; })()");
            if (!visible) return;

            _done.Add("call:answer");
            await TapAsync("#btn-call-yes", ct);

            // Back to the browser before the next step measures it.
            await EnsureBrowserForegroundAsync(ct);
        }
    }

    /// <summary>Dismisses Android's "open with" chooser when it is on top.</summary>
    private async Task DismissChooserAsync(CancellationToken ct)
    {
        var (top, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell dumpsys activity activities");
        bool chooser = top.Contains("intentresolver", StringComparison.OrdinalIgnoreCase)
            || top.Contains("ChooserActivity", StringComparison.OrdinalIgnoreCase);

        if (!chooser) return;

        await ShellAsync("input keyevent 4");
        _log.Add("call: the phone asked which app opens the dialer; dismissed the chooser");
        await Task.Delay(800, ct);
    }

    private async Task DriveCameraAsync(CancellationToken ct)
    {
        if (!_done.Contains("camera:trouble") && await WaitForSelectorAsync("#btn-camera-retry", 1500, ct))
        {
            _done.Add("camera:trouble");
            await TapAsync("#btn-camera-retry", ct);
            _log.Add("camera: retried after a prompt the browser refused");
            return;
        }

        // One screenshot and one measured frame per lens while it is live, so
        // the run carries a picture of what the lens showed and not only the
        // verdict the harness gave it.
        if (await WaitForSelectorAsync("#video-container:not([hidden])", 300, ct))
        {
            string label = await _cdp!.EvaluateStringAsync(
                "(document.querySelector('#cam-instructions') ? document.querySelector('#cam-instructions').textContent.trim() : '')") ?? "";

            if (label.Length > 0 && _cameraLensesSeen.Add(label))
            {
                _cameraLensCount++;
                await ScreenshotAsync($"camera-live-{_cameraLensCount}.png");
                double frameBrightness = await VideoFrameBrightnessAsync();
                _log.Add($"camera: lens {_cameraLensCount} live ({label}), frame brightness {frameBrightness}");
            }
        }

        for (int index = 0; index < 6; index++)
        {
            if (_done.Contains($"camera:photo-{index}")) continue;

            if (await WaitForSelectorAsync($"#btn-use-photo-{index}", 800, ct))
            {
                _done.Add($"camera:photo-{index}");

                await SavePhotoAsync(index);
                double brightness = await PhotoBrightnessAsync(index);
                _log.Add($"camera: photo {index} brightness {brightness}");

                if (brightness >= 12)
                {
                    await TapAsync($"#btn-use-photo-{index}", ct);
                }
                else
                {
                    await TapAsync($"#btn-camera-defect-{index}", ct);
                    await Task.Delay(300, ct);
                    await TapAsync($"#btn-reject-dark-{index}", ct);
                }

                await Task.Delay(400, ct);
            }
        }
    }

    private readonly HashSet<string> _cameraLensesSeen = [];
    private int _cameraLensCount;

    /// <summary>Writes the photo the step captured to a file, so it can be looked at.</summary>
    private async Task SavePhotoAsync(int index)
    {
        string dataUrl = await _cdp!.EvaluateStringAsync($"(() => {{ const img = document.getElementById('photo-{index}'); return img ? img.src : ''; }})()") ?? "";
        if (!dataUrl.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)) return;

        int comma = dataUrl.IndexOf(',');
        if (comma < 0) return;

        try
        {
            byte[] bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            string path = Path.Combine(_screenshotDirectory, $"camera-photo-{index}.jpg");
            await File.WriteAllBytesAsync(path, bytes);
            _log.Add($"camera: photo {index} saved to {Path.GetFileName(path)}");
        }
        catch (FormatException)
        {
            _log.Add($"camera: photo {index} was not a readable image");
        }
    }

    /// <summary>The brightest channel in the middle of the live video frame.</summary>
    private async Task<double> VideoFrameBrightnessAsync()
    {
        string value = await _cdp!.EvaluateStringAsync(
            "(() => { const v = document.getElementById('live-video'); if (!v || !v.videoWidth) return '-1'; const c = document.createElement('canvas'); c.width = 32; c.height = 32; const x = c.getContext('2d'); x.drawImage(v, 0, 0, 32, 32); const d = x.getImageData(0, 0, 32, 32).data; let m = 0; for (let k = 0; k < d.length; k += 4) m = Math.max(m, d[k], d[k + 1], d[k + 2]); return String(m); })()") ?? "-1";
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : -1;
    }

    /// <summary>The brightest channel in the middle of one captured photo.</summary>
    private async Task<double> PhotoBrightnessAsync(int index)
    {
        string value = await _cdp!.EvaluateStringAsync(
            "(() => { const img = document.getElementById('photo-" + index + "'); if (!img) return '-1'; if (!img.complete) return '-1'; const c = document.createElement('canvas'); c.width = 32; c.height = 32; const ctx = c.getContext('2d'); ctx.drawImage(img, 0, 0, 32, 32); const d = ctx.getImageData(0, 0, 32, 32).data; let m = 0; for (let k = 0; k < d.length; k += 4) m = Math.max(m, d[k], d[k + 1], d[k + 2]); return String(m); })()") ?? "-1";
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : -1;
    }

    private async Task DriveSensorAsync(CancellationToken ct)
    {
        // Android hands the motion sensors to the page without a prompt, so the
        // step is left to its own data path. When it offers the manual question
        // (the harness cannot physically tilt the phone), the rotation that the
        // rotation step just performed is what a person would answer about.
        if (!_done.Contains("sensor:manual") && await WaitForSelectorAsync("#sensor-manual-yes", 1500, ct))
        {
            _done.Add("sensor:manual");
            _log.Add("sensor: no motion data while the phone sits on the desk; confirming via the manual question");
            await TapAsync("#sensor-manual-yes", ct);
        }
    }

    private async Task DriveLocationAsync(CancellationToken ct)
    {
        if (!_done.Contains("location:request"))
        {
            if (await WaitForSelectorAsync("#btn-request-location", 5000, ct))
            {
                _done.Add("location:request");
                await TapAsync("#btn-request-location", ct);
                _locationRequestedAt = DateTime.UtcNow;
                _log.Add("location: asked for a fix");
            }
            return;
        }

        // A refusal is answered by a retry a person would make after allowing
        // the permission; the page's own grace window keeps it open.
        if (!_done.Contains("location:retry")
            && DateTime.UtcNow - _locationRequestedAt > TimeSpan.FromSeconds(6)
            && await WaitForSelectorAsync("#btn-retry-location", 500, ct))
        {
            _done.Add("location:retry");
            await TapAsync("#btn-retry-location", ct);
            _log.Add("location: retried");
        }
    }

    private DateTime _locationRequestedAt;

    private async Task DriveVibrationAsync(CancellationToken ct)
    {
        if (!_done.Contains("vibration:pulse") && await WaitForSelectorAsync("#btn-vibe-pulse", 3000, ct))
        {
            _done.Add("vibration:pulse");
            await TapAsync("#btn-vibe-pulse", ct);
            await Task.Delay(900, ct);
        }

        if (_done.Contains("vibration:pulse") && !_done.Contains("vibration:answer") && await WaitForSelectorAsync("#btn-vibe-yes", 3000, ct))
        {
            _done.Add("vibration:answer");
            bool accepted = await _cdp!.EvaluateBoolAsync(
                "!!(window.testRunner.tests[10].details && window.testRunner.tests[10].details.browserAccepted === true)");
            _log.Add($"vibration: browser accepted the pattern: {accepted}");
            await TapAsync(accepted ? "#btn-vibe-yes" : "#btn-vibe-no", ct);
        }
    }

    private async Task DrivePowerLockAsync(CancellationToken ct)
    {
        if (!_done.Contains("powerlock:cycle"))
        {
            _done.Add("powerlock:cycle");
            _log.Add("powerlock: pressing the power button like a person would");

            await Task.Delay(1500, ct);
            await ShellAsync("input keyevent 26");
            await Task.Delay(2500, ct);
            await ShellAsync("input keyevent 224");
            await Task.Delay(500, ct);
            await ShellAsync("input keyevent 82");
            await Task.Delay(500, ct);
            await ShellAsync("input swipe 540 1900 540 700 200");

            // Give the page a moment to see the visibility cycle before any
            // question is answered on its behalf.
            await Task.Delay(2000, ct);
            return;
        }

        // The question only appears when no cycle was seen, or for the
        // biometric half. The step's own fields say which half is which.
        bool powerAnswered = await _cdp!.EvaluateBoolAsync("!!window.testRunner.tests[11].powerAnswer");
        int optionCount = ParseInt(await _cdp.EvaluateStringAsync("String(document.querySelectorAll('#test-container .powerlock-option').length)"));
        if (optionCount == 0) return;

        if (!powerAnswered && !_done.Contains("powerlock:power-answer"))
        {
            _done.Add("powerlock:power-answer");
            _log.Add("powerlock: no cycle seen; answering the power question");
            await TapOptionAsync(0, ct);
            return;
        }

        if (powerAnswered && !_done.Contains("powerlock:biometric-answer"))
        {
            _done.Add("powerlock:biometric-answer");
            bool available = await _cdp.EvaluateBoolAsync("!!window.testRunner.tests[11].biometricAvailable");
            _log.Add($"powerlock: biometric authenticator available: {available}");
            // The harness cannot put a finger on the sensor. A person who
            // cannot test it either picks the code answer, which keeps the row
            // as skipped instead of claiming a pass.
            await TapOptionAsync(1, ct);
        }
    }

    private async Task DriveCosmeticAsync(CancellationToken ct)
    {
        // Every question is a person's judgement about the outside of the
        // phone. "Good" and "Works" are what a phone in this state gets; the
        // step advances once per answered question.
        if (_done.Contains("cosmetic:done")) return;

        if (await WaitForSelectorAsync(".cosmetic__option--good, .cosmetic__option--works", 400, ct))
        {
            await TapAsync(".cosmetic__option--good, .cosmetic__option--works", ct);
            _cosmeticTaps++;
            await Task.Delay(500, ct);
            if (_cosmeticTaps > 8) _done.Add("cosmetic:done");
        }
        else
        {
            _done.Add("cosmetic:done");
        }
    }

    private int _cosmeticTaps;

    private async Task TapOptionAsync(int optionIndex, CancellationToken ct)
    {
        JsonElement? point = await _cdp!.TryEvaluateAsync(
            "(() => { const b = document.querySelectorAll('#test-container .powerlock-option'); if (b.length <= " + optionIndex + ") return null; const el = b[" + optionIndex + "]; el.scrollIntoView({ block: 'center' }); const r = el.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })()");

        if (point is null || point.Value.ValueKind != JsonValueKind.Object) return;

        await TapPointAsync(point.Value.GetProperty("x").GetDouble(), point.Value.GetProperty("y").GetDouble(), ct);
    }

    // ------------------------------------------------------------- page tools

    private async Task<bool> WaitForSelectorAsync(string selector, int timeoutMs, CancellationToken ct)
    {
        // Visibility, not existence. A card that is in the DOM but hidden (the
        // sensor fallback before it is offered, the call feedback before the
        // link was pressed) has a zero-sized rectangle, and a tap aimed at its
        // centre would land nowhere.
        string expression =
            "(() => { const el = document.querySelector(" + JsonSerializer.Serialize(selector) + "); if (!el) return false; const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; })()";

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await _cdp!.EvaluateBoolAsync(expression)) return true;
            await Task.Delay(120, ct);
        }
        return false;
    }

    private async Task<bool> TapAsync(string selector, CancellationToken ct)
    {
        JsonElement? point = await PointAsync(selector);
        if (point is null || point.Value.ValueKind != JsonValueKind.Object) return false;

        await TapPointAsync(point.Value.GetProperty("x").GetDouble(), point.Value.GetProperty("y").GetDouble(), ct);
        return true;
    }

    private async Task TapPointAsync(double x, double y, CancellationToken ct)
    {
        var finger = new Dictionary<string, object?> { ["x"] = x, ["y"] = y, ["id"] = 0, ["radiusX"] = 2, ["radiusY"] = 2 };
        await _cdp!.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchStart", finger), ct);
        await Task.Delay(50, ct);
        await _cdp.SendAsync("Input.dispatchTouchEvent", TouchEvent("touchEnd", null), ct);
    }

    private async Task<JsonElement?> PointAsync(string selector)
    {
        string expression =
            "(() => { const el = document.querySelector(" + JsonSerializer.Serialize(selector) + "); if (!el) return null; el.scrollIntoView({ block: 'center', inline: 'center' }); const r = el.getBoundingClientRect(); if (r.width === 0 || r.height === 0) return null; return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })()";
        return await _cdp!.TryEvaluateAsync(expression);
    }

    // ------------------------------------------------------------------ adb

    private async Task ShellAsync(string command)
    {
        var (stdout, stderr, exit) = await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell {command}");
        if (exit != 0) _log.Add($"adb shell '{command}' exited {exit}: {stderr.Trim()}");
    }

    private async Task<string> SetLocationServicesAsync(bool enable)
    {
        var (before, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell settings get secure location_mode");
        string current = before.Trim();

        if (enable && current != "3")
        {
            await ShellAsync("settings put secure location_mode 3");
            _log.Add($"location services were '{current}', switched on for the run");
        }

        return current;
    }

    private async Task RestoreLocationServicesAsync(string before)
    {
        if (before.Length == 0 || before == "3") return;
        await ShellAsync($"settings put secure location_mode {before}");
        _log.Add($"location services restored to '{before}'");
    }

    private async Task<string> ScreenshotAsync(string name)
    {
        string path = Path.Combine(_screenshotDirectory, name);
        string remote = $"/data/local/tmp/pg-{Guid.NewGuid():N}.png";
        try
        {
            await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell screencap -p {remote}");
            await ToolRunner.ExecuteAsync("adb", $"-s {_serial} pull {remote} \"{path}\"");
            return File.Exists(path) ? path : "";
        }
        finally
        {
            await ToolRunner.ExecuteAsync("adb", $"-s {_serial} shell rm -f {remote}");
        }
    }

    private async Task<string> ReadProgressAsync(int port, string sessionId, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return await http.GetStringAsync($"http://127.0.0.1:{port}/api/pwa/progress?sessionId={Uri.EscapeDataString(sessionId)}", ct);
        }
        catch (Exception ex)
        {
            return $"{{\"error\":\"{ex.Message}\"}}";
        }
    }

    // --------------------------------------------------------------- parsing

    private static IReadOnlyDictionary<string, string> ParseStatuses(string suiteJson)
    {
        var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(suiteJson)) return statuses;

        try
        {
            using var document = JsonDocument.Parse(suiteJson);
            if (document.RootElement.TryGetProperty("tests", out JsonElement tests))
            {
                foreach (JsonElement test in tests.EnumerateArray())
                {
                    string id = test.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() ?? "" : "";
                    string status = test.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString() ?? "" : "";
                    if (id.Length > 0 && !statuses.ContainsKey(id)) statuses[id] = status;
                }
            }
        }
        catch (JsonException)
        {
            // An unparsable suite is reported by the empty status map.
        }

        return statuses;
    }

    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private static int ParseInt(string text) => int.TryParse(text.Trim(), out int value) ? value : 0;

    private static (int Red, int Green, int Blue) AverageCentre(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (0, 0, 0);

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) return (0, 0, 0);

        int half = Math.Min(bitmap.Width, bitmap.Height) / 8;
        int centreX = bitmap.Width / 2;
        int centreY = bitmap.Height / 2;

        long red = 0, green = 0, blue = 0;
        int samples = 0;

        for (int y = centreY - half; y < centreY + half; y += 4)
        {
            for (int x = centreX - half; x < centreX + half; x += 4)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) continue;
                var pixel = bitmap.GetPixel(x, y);
                red += pixel.Red;
                green += pixel.Green;
                blue += pixel.Blue;
                samples++;
            }
        }

        return samples == 0 ? (0, 0, 0) : ((int)(red / samples), (int)(green / samples), (int)(blue / samples));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cdp is not null) { await _cdp.DisposeAsync(); _cdp = null; }
        if (_reverse is not null) { await _reverse.DisposeAsync(); _reverse = null; }
        if (_server is not null) { await _server.DisposeAsync(); _server = null; }
    }

    /// <summary>The adb reverse tunnel, removed again when the run ends.</summary>
    private sealed class AdbReverseLease : IAsyncDisposable
    {
        private readonly string _serial;
        private readonly int _port;

        private AdbReverseLease(string serial, int port)
        {
            _serial = serial;
            _port = port;
        }

        public static async Task<AdbReverseLease> OpenAsync(string serial, int port, Action<string> log)
        {
            await ToolRunner.ExecuteAsync("adb", $"-s {serial} reverse --remove tcp:{port}");
            var (_, stderr, exit) = await ToolRunner.ExecuteAsync("adb", $"-s {serial} reverse tcp:{port} tcp:{port}");

            if (exit != 0)
                throw new InvalidOperationException($"adb reverse failed: {stderr.Trim()}");

            log($"adb reverse tcp:{port} opened; the phone sees the server as localhost");
            return new AdbReverseLease(serial, port);
        }

        public async ValueTask DisposeAsync() =>
            await ToolRunner.ExecuteAsync("adb", $"-s {_serial} reverse --remove tcp:{_port}");
    }
}
