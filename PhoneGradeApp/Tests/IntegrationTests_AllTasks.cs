using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Web;
using Xunit;

namespace PhoneGrade.Tests;

public class IntegrationTests_AllTasks : IAsyncLifetime
{
    private TestRunnerServer _server;
    private HttpClient _client;

    public async Task InitializeAsync()
    {
        _server = new TestRunnerServer(6124);
        await _server.StartAsync();
        _client = new HttpClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_server != null)
        {
            await _server.DisposeAsync();
        }
    }

    // Task 1: PWA Capability Scanner & Desktop Logging
    [Fact]
    public async Task Task1_CapabilityScanner_LogsWarningWhenApiMissing()
    {
        // Arrange
        var sessionId = "TEST_CAP_001";
        var request = new
        {
            sessionId = sessionId,
            missingApi = "navigator.geolocation",
            userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)",
            osVersion = "iOS 17.0"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync($"http://localhost:{_server.BoundPort}/api/pwa/log-warning", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    // Task 2: Motion Sensor Permissions
    [Fact]
    public void Task2_MotionSensor_AsksTheOperatorInsteadOfSkipping()
    {
        var sensorTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/SensorTest.js");

        // The iOS prompt is still asked for, from a click, because that is the
        // only way it can be asked.
        Assert.Contains("requestPermission", sensorTestContent);

        // What it must not do is end the step by itself. A phone with a working
        // orientation sensor was recorded as untestable because the browser would
        // not hand over the numbers, and the operator never got to look at it.
        Assert.DoesNotContain("handleFallbackAndSkip", sensorTestContent);
        Assert.DoesNotContain("this.skip(", sensorTestContent);

        // So the sensors that cannot be read are offered as a hand-turned check,
        // and the verdict says in as many words that it was a hand-turned check.
        Assert.Contains("sensor-manual-yes", sensorTestContent);
        Assert.Contains("sensor-manual-no", sensorTestContent);
        Assert.Contains("niet rechtstreeks uitgelezen", sensorTestContent);
        Assert.Contains("rotationConfirmed", sensorTestContent);
    }

    // Task 3: GPS Error Recovery
    [Fact]
    public void Task3_GPS_ShowsRetryButtonOnPermissionDenied()
    {
        var locationTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/LocationTest.js");

        // The retry button must exist and must be wired up, and a denial must not
        // settle the step before the operator has been asked about it. The old step
        // called skip() on the refusal, so the suite moved on without anyone
        // getting a chance to grant it.
        Assert.Contains("btn-retry-location", locationTestContent);
        Assert.Contains("btnRetry.onclick", locationTestContent);
        Assert.Contains("geen toestemming", locationTestContent);
        Assert.DoesNotContain("'User denied location permission'", locationTestContent);

        // It does settle, though, and that is the half that was broken. The window
        // is opened once and a retry cannot reopen it, so a phone that keeps saying
        // no cannot hold the suite on this step by pressing retry, and the operator
        // can see how long the retry is still worth making.
        Assert.Contains("RetryDeadline", locationTestContent);
        Assert.Contains("btn-loc-give-up", locationTestContent);
        Assert.Contains("grace.remainingSeconds()", locationTestContent);

        // The reason the retry looked dead is now on the card. On Android the
        // browser keeps its own refusal, so only its own site permission changes
        // it; the quick-settings toggle alone returns the same refusal for ever.
        Assert.Contains("browserinstellingen", locationTestContent);

        // The runner's 90 s failsafe ended the step while the operator was still in
        // the settings, so the step asks for its own.
        Assert.Contains("getFailsafeMs", locationTestContent);
    }

    // Task 3: The steps that used to end themselves
    [Fact]
    public void Task3_PermissionSteps_DoNotReachAVerdictWithoutTheOperator()
    {
        // The rule behind the camera, motion, microphone and location fixes, in one
        // place. A refusal is a question, and only a genuinely absent API is a gap
        // the step may record on its own.
        foreach (var module in new[]
                 {
                     "CameraTest", "SensorTest", "MicrophoneTest", "LocationTest"
                 })
        {
            var content = RepoPath.Read($"PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/{module}.js");

            // Whatever the reason, the browser's own words are not the report. A
            // label reading "NotAllowedError: Permission denied" tells a technician
            // nothing they can act on.
            Assert.DoesNotContain("this.fail('Failed to access camera", content);
            Assert.DoesNotContain("'User denied location permission'", content);

            // And a step that measures more than one thing, or needs a person to
            // read a card and answer it, cannot have the single-measurement budget.
            Assert.Contains("getFailsafeMs", content);
        }
    }

    // Task 3: Why a media call failed, in one place
    [Fact]
    public void Task3_MediaFailures_AreClassifiedAndExplained()
    {
        var capability = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/MediaCapability.js");

        // Every step used to answer "something went wrong" the same way, which was
        // to give up. Sorting the answer into refused, absent, busy, unsupported
        // and unknown is what lets a step tell the operator which one it hit.
        Assert.Contains("CAPABILITY", capability);
        Assert.Contains("classifyMediaError", capability);
        Assert.Contains("explainMediaError", capability);
        Assert.Contains("NotAllowedError", capability);
        Assert.Contains("NotReadableError", capability);
        Assert.Contains("hasMediaDevices", capability);

        // A refusal and a camera another app was holding are both worth another
        // try, and only a refusal is reported as a reason not to retry.
        Assert.Contains("isFixable", capability);
    }

    // Task 3: The microphone
    [Fact]
    public void Task3_Microphone_DoesNotSwapInTheRecorderAndReleasesTheInput()
    {
        var micContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/MicrophoneTest.js");

        // The old step ran getUserMedia in a try and fell out of it into the
        // phone's own voice recorder. That replaced a measurement with a different
        // test, and a phone with a dead microphone could be passed by pressing
        // "yes, clear" on a recording that never had any sound in it. A refusal now
        // says so and offers a retry; the recorder is reached from the card, on
        // purpose, and only when there is no microphone API to measure with.
        Assert.Contains("mic-retry", micContent);
        Assert.Contains("showMicTrouble", micContent);
        Assert.Contains("mic-recorder", micContent);
        Assert.Contains("runRecorderFallback", micContent);
        Assert.Contains("checkMethod", micContent);

        // A skipped step used to leave the input open, so the phone went on showing
        // its microphone indicator for the rest of the run. dispose is what the
        // runner calls on every exit, so it is what has to let go.
        Assert.Contains("dispose()", micContent);
        Assert.Contains("stopCapture", micContent);
        Assert.Contains("close()", micContent);
    }

    // Task 3: The camera
    [Fact]
    public void Task3_Camera_JudgesEachCameraOnItsOwn()
    {
        var cameraContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");

        // A camera that will not open is offered back with the browser's reason and
        // a retry, and a failure to open the second camera must not throw away the
        // verdict on the first.
        Assert.Contains("waitForCameraTrouble", cameraContent);
        Assert.Contains("btn-camera-retry", cameraContent);
        Assert.Contains("btn-camera-reject", cameraContent);
        Assert.Contains("rearCamera", cameraContent);
        Assert.Contains("frontCamera", cameraContent);

        // A photo cannot be captured before the video has a frame, or a black
        // rectangle goes on the report as a photo a technician approved.
        Assert.Contains("videoWidth", cameraContent);

        // A camera with no torch is normal, so it is stated as a fact and the photo
        // is judged in whatever light there is. The old text was the generic
        // "inspect the photo carefully", which read as if something had gone wrong.
        Assert.Contains("torch-overlay", cameraContent);
        Assert.Contains("geen flits", cameraContent);
    }

    // Task 3: The cards these steps write
    [Fact]
    public void Task3_StepCards_LiveInTheSharedStylesheet()
    {
        var styles = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/styles.css");

        // Every step that shows a card shares these, instead of each one writing
        // its own inline styles and drifting a few pixels apart.
        foreach (var className in new[]
                 {
                     ".step-card", ".step-lead", ".step-stack", ".step-block", ".step-hint",
                     ".step-question", ".step-note", ".step-actions", ".sensor-bowl",
                     ".sensor-track", ".mic-meter", ".camera-view", ".loc-readout", ".fix-steps"
                 })
        {
            Assert.Contains(className, styles);
        }

        // The location step's spinner asked for a keyframe that was never defined,
        // so it never turned. It was a static ring on the one step where waiting is
        // the most likely outcome, and it read as a picture rather than a wait.
        Assert.Contains("@keyframes spin", styles);

        // The hidden attribute has to beat a class that sets a display, or the
        // parts of a card that are not reachable yet are on screen anyway. That is
        // a rule about the browser's own styling winning, so it has to be here
        // rather than in each step.
        Assert.Contains("[hidden]", styles);
        Assert.Contains("display: none !important", styles);
    }

    // Task 3: Display Brightness Integration
    [Fact]
    public async Task Task3_Display_MergesBrightnessCheck()
    {
        var displayTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");

        // The brightness gate is still there, and the operator is still told to
        // turn the brightness up first.
        Assert.Contains("showBrightnessCheck", displayTestContent);
        Assert.Contains("helderheid", displayTestContent);
        Assert.Contains("maximaal", displayTestContent);

        // What changed is that it can no longer condemn the display. A phone at a
        // third brightness is a brightness setting, not a broken panel, and
        // recording it as a display failure let a dim phone be graded faulty for
        // it. The only way forward is "yes"; the way out is the runner's skip.
        Assert.DoesNotContain("brightness-no", displayTestContent);
        Assert.DoesNotContain("Scherm niet helder genoeg om dode pixels te beoordelen", displayTestContent);
    }

    // Task 3: Display inspection is per patch, not one verdict for the whole panel
    [Fact]
    public async Task Task3_Display_RecordsAVerdictPerPatch()
    {
        var displayTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");
        var inspectionContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayInspection.js");

        // A stuck subpixel is smaller than the eye resolves at arm's length, so a
        // single pass/fail over the whole panel cannot find one. The step walks a
        // grid and records a verdict per patch per colour.
        Assert.Contains("patchRect", displayTestContent);
        Assert.Contains("setVerdict", displayTestContent);
        Assert.Contains("INSPECTION_COLORS", displayTestContent);

        // The counting has to exist and be the shared version, or the label would
        // still say nothing about where a defect is.
        Assert.Contains("describeDefects", inspectionContent);
        Assert.Contains("isDisplayFaulty", inspectionContent);
        Assert.Contains("inspectedCount", inspectionContent);
    }

    // Task 3: The display step is the only one that escapes the test container
    [Fact]
    public async Task Task3_Display_CleansUpItsFullScreenOverlay()
    {
        var displayTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/DisplayTest.js");

        // The overlay goes on document.body so it can sit above everything, which
        // means the runner clearing the container does not touch it. Without a
        // dispose, a skip or the failsafe in the middle of a colour left a
        // full-screen block of red over the next step.
        Assert.Contains("dispose()", displayTestContent);
        Assert.Contains("removeOverlay", displayTestContent);
        Assert.Contains("document.body.removeChild", displayTestContent);
    }

    // Task 3: The speaker tone can be played again and the verdict changed
    [Fact]
    public void Task3_Speaker_ToneIsReplayableAndTheVerdictIsNotFinal()
    {
        var speakerTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/SpeakerTest.js");

        // The old step disabled the play button and hid it when the tone finished,
        // and hid the verdict buttons the moment they were pressed. That left one
        // chance to get it right on something judged by listening, and a tone
        // missed while the volume was still coming up reads exactly like a dead
        // speaker.
        Assert.DoesNotContain("playEarpieceBtn.disabled = true", speakerTestContent);
        Assert.DoesNotContain("style.display = 'none'", speakerTestContent);

        // The replay path and the corrigible verdict are what replaced it.
        Assert.Contains("plays[section] += 1", speakerTestContent);
        Assert.Contains("feedback.hidden = false", speakerTestContent);
        Assert.Contains("settled", speakerTestContent);
    }

    // Task 3: The speaker tone is planned, not played blind
    [Fact]
    public void Task3_Speaker_ToneIsPlannedBeforeItIsPlayed()
    {
        var toneContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/SpeakerTone.js");

        // Frequencies and timings are the whole content of a speaker test. Spelled
        // out up front, they can be checked without a phone and a pair of ears.
        Assert.Contains("planChime", toneContent);
        Assert.Contains("isAudible", toneContent);
        Assert.Contains("isWithinRange", toneContent);
        Assert.Contains("describeOutcome", toneContent);

        // And the two verdicts stay apart on the label, because "the earpiece works
        // and the loudspeaker does not" is a phone someone can still take calls on.
        Assert.Contains("Hoofdluidspreker", toneContent);
        Assert.Contains("Oorluidspreker", toneContent);
    }

    // Task 3: The audio context cannot hang the step
    [Fact]
    public void Task3_Speaker_ResumeIsBounded()
    {
        var speakerTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/SpeakerTest.js");

        // Measured on a real Pixel over the secure origin: resume() does not always
        // settle. An unbounded await meant the status line never changed, no verdict
        // buttons appeared, and the operator pressed a dead button until the
        // failsafe ended the step a minute and a half later.
        Assert.Contains("RESUME_TIMEOUT_MS", speakerTestContent);
        Assert.Contains("Promise.race", speakerTestContent);

        // And a context that is not running is reported as a tone that was not
        // produced, rather than judged as a dead speaker.
        Assert.Contains("audioProblem", speakerTestContent);
        Assert.Contains("De browser gaf geen toon af", speakerTestContent);
    }

    // Task 3: iOS Fullscreen Banner
    [Fact]
    public async Task Task3_iOS_DisplaysFullscreenBanner()
    {
        var appJsContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        var indexHtml = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");

        // Verify standalone detection lives in the bootstrap...
        Assert.Contains("display-mode: standalone", appJsContent);
        Assert.Contains("ios-standalone-banner", appJsContent);

        // ...and the banner it reveals is declared in the markup.
        Assert.Contains("ios-standalone-banner", indexHtml);
        Assert.Contains("Add to Home Screen", indexHtml);
    }

    // Task 4: Camera WebRTC Stream
    [Fact]
    public async Task Task4_Camera_UsesWebRTCNotFileInput()
    {
        var cameraTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");
        
        // Verify file input is removed
        Assert.DoesNotContain("<input type='file'", cameraTestContent.Replace("\"", "'"));
        // Verify WebRTC is used
        Assert.Contains("getUserMedia", cameraTestContent);
        Assert.Contains("facingMode", cameraTestContent);
    }

    // Task 4: Photo Review Step
    [Fact]
    public void Task4_Camera_HasPhotoReviewWorkflow()
    {
        var cameraTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");

        // The review is a still the operator has to look at before the camera is
        // judged, in the operator's own language.
        Assert.Contains("btn-retake", cameraTestContent);
        Assert.Contains("btn-use-photo", cameraTestContent);
        Assert.Contains("Opnieuw maken", cameraTestContent);
        Assert.Contains("Foto goedkeuren", cameraTestContent);

        // The still is a real copy of a real frame, not a placeholder.
        Assert.Contains("canvas", cameraTestContent);
        Assert.Contains("drawImage", cameraTestContent);
    }

    // Task 4: Torch Fallback UI
    [Fact]
    public void Task4_Camera_HasTorchFallbackUI()
    {
        var cameraTestContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/modules/CameraTest.js");

        Assert.Contains("torch-overlay", cameraTestContent);
        Assert.Contains("geen flits", cameraTestContent);

        // The torch is a bonus, not the test. A camera that cannot switch it on is
        // photographed in whatever light there is rather than failed for it.
        Assert.Contains("torchActive", cameraTestContent);
    }

    // Task 5: Automatic Results Sync
    [Fact]
    public async Task Task5_Results_NoExportButton()
    {
        var indexHtml = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/index.html");
        
        // Verify Export button is removed
        Assert.DoesNotContain("export-results-btn", indexHtml);
    }

    [Fact]
    public async Task Task5_Results_AutoSyncToServer()
    {
        var appJsContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify auto-sync endpoints
        Assert.Contains("/api/pwa/submit-step", appJsContent);
        Assert.Contains("/api/pwa/submit", appJsContent);
    }

    [Fact]
    public async Task Task5_Results_OfflineFallback()
    {
        var appJsContent = RepoPath.Read("PhoneGradeApp/PhoneGrade.UI/wwwroot/app.js");
        
        // Verify offline queue
        Assert.Contains("pwa_offline_queue", appJsContent);
        Assert.Contains("localStorage", appJsContent);
        Assert.Contains("window.addEventListener('online'", appJsContent);
    }

    // Task 6: USB Event Watcher
    [Fact]
    public void Task6_USB_EventWatcherExistsAndIsWindows()
    {
        // The watcher must only report native monitoring on Windows, where WMI
        // is available. Everywhere else the polling loop stays the source of truth.
        UsbEventWatcher.StopMonitoring();
        UsbEventWatcher.StartMonitoring();

        Assert.Equal(OperatingSystem.IsWindows(), UsbEventWatcher.IsNativeMonitoringActive);

        // Subscribers must be reachable through the public surface the view model uses.
        int connected = 0;
        int disconnected = 0;
        EventHandler onConnected = (_, _) => connected++;
        EventHandler onDisconnected = (_, _) => disconnected++;
        UsbEventWatcher.UsbDeviceConnected += onConnected;
        UsbEventWatcher.UsbDeviceDisconnected += onDisconnected;
        try
        {
            UsbEventWatcher.RaiseConnected();
            UsbEventWatcher.RaiseDisconnected();
        }
        finally
        {
            UsbEventWatcher.UsbDeviceConnected -= onConnected;
            UsbEventWatcher.UsbDeviceDisconnected -= onDisconnected;
            UsbEventWatcher.StopMonitoring();
        }

        Assert.Equal(1, connected);
        Assert.Equal(1, disconnected);
    }

    // Task 6: ADB Diagnostic Warning
    [Fact]
    public void Task6_ADB_WarningCardInUI()
    {
        var content = RepoPath.Read("PhoneGradeApp", "PhoneGrade.UI", "Views", "MainWindow.axaml");

        // Verify warning card exists
        Assert.Contains("ShowAdbWarning", content);
        Assert.Contains("Android device connected", content);
        Assert.Contains("USB Debugging", content);
        Assert.Contains("RetryAdbDetectionCommand", content);
    }

    // Task 1: Grading Penalty Integration
    [Fact]
    public void Task1_Grading_PenalizesGradeAWithMissingApis()
    {
        var missingApiCheck = new ComponentStatus
        {
            Name = "API Missing: navigator.geolocation",
            Status = ComponentStatusType.Failed,
            Description = "Device is missing navigator.geolocation capability."
        };

        // A grade A device that lost a mandatory browser API drops to B.
        var penalised = GradePolicy.ApplyMissingApiPenalty("A", new[] { missingApiCheck });
        Assert.Equal("B", penalised);

        // The penalty only caps A; the grades below it are unaffected.
        Assert.Equal("B", GradePolicy.ApplyMissingApiPenalty("B", new[] { missingApiCheck }));
        Assert.Equal("C", GradePolicy.ApplyMissingApiPenalty("C", new[] { missingApiCheck }));

        // Without the missing-API check a grade A device keeps its grade.
        var healthyCheck = new ComponentStatus
        {
            Name = "Battery Health",
            Status = ComponentStatusType.Passed
        };
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", new[] { healthyCheck }));
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", Array.Empty<ComponentStatus>()));
    }

    [Fact]
    public void Task1_Grading_IgnoresNonFailedAndUnrelatedChecks()
    {
        // Only failed checks whose name carries the capability-scanner prefix
        // count, so an unrelated failed check cannot cap the grade.
        var unrelated = new ComponentStatus
        {
            Name = "Rear Camera",
            Status = ComponentStatusType.Failed
        };
        Assert.False(GradePolicy.HasMissingBrowserApis(new[] { unrelated }));

        // A detected-but-passed capability is not a penalty either.
        var reportedOk = new ComponentStatus
        {
            Name = "API Missing: navigator.wakeLock",
            Status = ComponentStatusType.Passed
        };
        Assert.False(GradePolicy.HasMissingBrowserApis(new[] { reportedOk }));
        Assert.Equal("A", GradePolicy.ApplyMissingApiPenalty("A", new[] { reportedOk }));
    }

    // Integration: All endpoints respond
    [Fact]
    public async Task Integration_AllEndpointsRespond()
    {
        var sessionId = "TEST_INTEGRATION";
        var endpoints = new[] 
        { 
            "/api/pwa/handshake",
            "/api/pwa/telemetry", 
            "/api/pwa/log-warning",
            "/api/pwa/submit-step",
            "/api/pwa/submit"
        };

        foreach (var endpoint in endpoints)
        {
            var request = new { sessionId = sessionId };
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            
            var response = await _client.PostAsync($"http://localhost:{_server.BoundPort}{endpoint}", content);
            Assert.True(response.IsSuccessStatusCode, $"Endpoint {endpoint} failed");
        }
    }
}
