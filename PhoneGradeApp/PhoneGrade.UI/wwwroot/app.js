

import { DeviceTest } from './modules/DeviceTest.js';
import { TouchTest } from './modules/TouchTest.js';
import { ForceTouchTest } from './modules/ForceTouchTest.js';
import { DisplayTest } from './modules/DisplayTest.js';
import { ScreenRotationTest } from './modules/ScreenRotationTest.js';
import { SpeakerTest } from './modules/SpeakerTest.js';
import { MicrophoneTest } from './modules/MicrophoneTest.js';
import { CallTest } from './modules/CallTest.js';
import { CameraTest } from './modules/CameraTest.js';
import { SensorTest } from './modules/SensorTest.js';
import { LocationTest } from './modules/LocationTest.js';
import { VibrationTest } from './modules/VibrationTest.js';
import { CosmeticTest } from './modules/CosmeticTest.js';
import { RemoteConsoleLogger } from './RemoteConsoleLogger.js';
import { CapabilityScanner } from './modules/CapabilityScanner.js';
import { applyStoredResults, firstPendingIndex, canResume, describeResume } from './modules/SuiteProgress.js';
import { baseUrlFrom } from './modules/serverUrl.js';
import { initLocale, locale, applyStaticText, t } from './modules/i18n.js';
import { runCommand } from './modules/CommandDispatch.js';
import { countResults } from './modules/SuiteCounts.js';
import { buildResultRow } from './modules/ResultRows.js';

class RestApiClient {
    constructor() {
        this.sessionId = this.getUrlParam('sessionId') || 'UNKNOWN';
        this.connected = false;
        this.baseUrl = baseUrlFrom(window.location);
        this.pollInterval = null;
        this.consoleLogger = null;
        this.queueSeq = 0;
    }

    getUrlParam(name) {
        const params = new URLSearchParams(window.location.search);
        return params.get(name);
    }

    /**
     * Works out which device this page belongs to.
     *
     * Launched from the home screen the manifest sends the browser to "/", with no
     * sessionId on it, and the page then has no idea which device it is grading.
     * The desktop knows, so it is asked. A copy is kept locally as well, because
     * the origin changes when the operator switches between the LAN address and
     * the localhost tunnel, and localStorage does not follow it.
     */
    async resolveSessionId() {
        const fromUrl = this.getUrlParam('sessionId');
        if (fromUrl) {
            this.sessionId = fromUrl;
            this.rememberSessionId(fromUrl);
            return fromUrl;
        }

        const remembered = this.rememberedSessionId();
        if (remembered) this.sessionId = remembered;

        try {
            const resp = await fetch(`${this.baseUrl}/api/pwa/active-session`);
            if (resp.ok) {
                const data = await resp.json();
                if (data && data.sessionId && data.sessionId !== 'UNKNOWN') {
                    this.sessionId = data.sessionId;
                    this.rememberSessionId(data.sessionId);
                }
            }
        } catch (e) {
            // Offline or the desktop is gone. The local copy is the fallback.
        }

        return this.sessionId;
    }

    rememberSessionId(sessionId) {
        try { localStorage.setItem('pwa_session_id', sessionId); } catch (e) { /* private mode */ }
    }

    rememberedSessionId() {
        try { return localStorage.getItem('pwa_session_id'); } catch (e) { return null; }
    }

    /** Asks the desktop how far this device got on the previous run. */
    async getProgress() {
        try {
            const resp = await fetch(`${this.baseUrl}/api/pwa/progress?sessionId=${encodeURIComponent(this.sessionId)}`);
            if (!resp.ok) return null;
            return await resp.json();
        } catch (e) {
            return null;
        }
    }

    /** Throws away the stored progress so "Run Again" starts from the first test. */
    async resetProgress() {
        try {
            await fetch(`${this.baseUrl}/api/pwa/progress/reset`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ sessionId: this.sessionId })
            });
        } catch (e) {
            // Not fatal: the phone still runs from scratch either way.
        }
    }

    async connect() {
        try {
            // 1. Handshake with server
            const resp = await fetch(`${this.baseUrl}/api/pwa/handshake`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ 
                    sessionId: this.sessionId,
                    device: navigator.userAgent 
                })
            });
            
            if (resp.ok) {
                this.connected = true;
                this.updateConnectionStatus('connected', t('runner.connected'));
                
                // 2. Send telemetry
                await this.sendClientTelemetry();
                
                // 3. Start remote console logging
                if (!this.consoleLogger) {
                    this.consoleLogger = new RemoteConsoleLogger(this);
                }
                
                // 4. Start polling for server messages
                //    Capability scanning is owned by the bootstrap below so the
                //    missing-API report is sent exactly once per page load.
                this.startPolling();
            } else {
                throw new Error(`Handshake failed: ${resp.status}`);
            }
        } catch (e) {
            this.connected = false;
            this.updateConnectionStatus('error', t('runner.connectionError', { message: e.message }));
            setTimeout(() => this.connect(), 2000);
        }
    }

    startPolling() {
        if (this.pollInterval) clearInterval(this.pollInterval);
        this.pollInterval = setInterval(async () => {
            try {
                const resp = await fetch(`${this.baseUrl}/api/pwa/status?sessionId=${this.sessionId}`);
                if (resp.ok) {
                    const data = await resp.json();
                    if (data.command) {
                        this.handleMessage(data.command);
                    }
                }
            } catch (e) {
                // Silent fail on polling
            }
        }, 2000);
    }

    async sendClientTelemetry() {
        const ua = navigator.userAgent;
        let browser = 'Unknown';
        let browserVersion = '';
        let os = 'Unknown';
        let osVersion = '';

        if (/Chrome/.test(ua) && !/Edge/.test(ua)) {
            browser = 'Chrome';
            browserVersion = ua.match(/Chrome\/(\d+)/)?.[1] || '';
        } else if (/Safari/.test(ua) && !/Chrome/.test(ua)) {
            browser = 'Safari';
            browserVersion = ua.match(/Version\/(\d+)/)?.[1] || '';
        } else if (/Firefox/.test(ua)) {
            browser = 'Firefox';
            browserVersion = ua.match(/Firefox\/(\d+)/)?.[1] || '';
        } else if (/Edg/.test(ua)) {
            browser = 'Edge';
            browserVersion = ua.match(/Edg\/(\d+)/)?.[1] || '';
        }

        if (/iPhone|iPad|iPod/.test(ua)) {
            os = 'iOS';
            osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || '';
        } else if (/Android/.test(ua)) {
            os = 'Android';
            osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || '';
        } else if (/Macintosh/.test(ua)) {
            os = 'macOS';
            osVersion = ua.match(/Mac OS X ([\d_]+)/)?.[1]?.replace(/_/g, '.') || '';
        } else if (/Windows/.test(ua)) {
            os = 'Windows';
        }

        const telemetry = {
            userAgent: ua,
            browser,
            browserVersion,
            os,
            osVersion,
            screenWidth: window.screen.width,
            screenHeight: window.screen.height,
            pixelRatio: window.devicePixelRatio || 1.0,
            touchSupport: 'ontouchstart' in window || navigator.maxTouchPoints > 0,
            accelerometerSupport: typeof DeviceMotionEvent !== 'undefined',
            gyroscopeSupport: typeof DeviceOrientationEvent !== 'undefined',
            geolocationSupport: 'geolocation' in navigator,
            webAudioSupport: typeof AudioContext !== 'undefined' || typeof webkitAudioContext !== 'undefined',
            cameraSupport: navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function',
            microphoneSupport: navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function',
            vibrationSupport: typeof navigator.vibrate === 'function',
            language: navigator.language || 'Unknown',
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone
        };

        try {
            await fetch(`${this.baseUrl}/api/pwa/telemetry`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    type: 'client_telemetry',
                    sessionId: this.sessionId,
                    clientTelemetry: telemetry
                })
            });
        } catch (e) {
            console.error('Failed to send telemetry:', e);
        }
    }

    isConnected() {
        return this.connected;
    }

    /**
     * Posts a message to the desktop host.
     * Returns true when the server acknowledged it. Failures are appended to the
     * offline queue unless the caller is already replaying that queue, which is
     * what keeps syncOfflineQueue() from re-queueing items it is about to retry.
     */
    async send(message, { enqueueOnFailure = true } = {}) {
        try {
            let endpoint = '/api/pwa/submit-step';
            if (message.type === 'suite_complete') {
                endpoint = '/api/pwa/submit';
            } else if (message.type === 'log_event') {
                endpoint = '/api/pwa/log';
            }

            // The stamp is what lets the desktop tell a fresh result from one
            // replayed out of the queue, so it is set here rather than at each call
            // site. An existing one is left alone: a replayed message already has it.
            if (!message.clientTimestamp) {
                message = { ...message, clientTimestamp: new Date().toISOString() };
            }

            const response = await fetch(`${this.baseUrl}${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(message)
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            this.updateConnectionStatus('connected', t('runner.connected'));
            return true;
        } catch (e) {
            console.warn('Failed to send message, saving to offline queue:', e);
            if (enqueueOnFailure) {
                this.saveToOfflineQueue(message);
                this.updateConnectionStatus('offline', t('runner.offlineSavedLocally'));
            }
            return false;
        }
    }

    /**
     * Queues a message for later.
     *
     * Each entry gets an id of its own. Without one, two results for the same test
     * are indistinguishable, so a retry that queued a new verdict and the earlier
     * one still waiting would be replayed in an arbitrary order and the older
     * verdict could land last and win.
     */
    saveToOfflineQueue(message) {
        try {
            const queue = JSON.parse(localStorage.getItem('pwa_offline_queue') || '[]');
            this.queueSeq++;
            queue.push({
                id: `${Date.now()}-${this.queueSeq}`,
                queuedAt: new Date().toISOString(),
                message
            });
            // Bound the queue so a long outage cannot fill localStorage.
            while (queue.length > 200) queue.shift();
            localStorage.setItem('pwa_offline_queue', JSON.stringify(queue));
        } catch (e) {
            console.error('Failed to save to offline queue:', e);
        }
    }

    async syncOfflineQueue() {
        try {
            const queue = JSON.parse(localStorage.getItem('pwa_offline_queue') || '[]');
            if (queue.length === 0) return;

            console.log(`Syncing ${queue.length} offline messages...`);
            const failed = [];

            for (const item of queue) {
                // send() reports failure instead of throwing, so the outcome has to
                // be read from its return value; otherwise the queue looks empty
                // even when every message was rejected.
                const delivered = await this.send(item.message, { enqueueOnFailure: false });
                if (!delivered) failed.push(item);
            }

            if (failed.length === 0) {
                localStorage.removeItem('pwa_offline_queue');
                this.updateConnectionStatus('connected', t('runner.connected'));
                console.log('All offline messages synced successfully');
            } else {
                localStorage.setItem('pwa_offline_queue', JSON.stringify(failed));
                this.updateConnectionStatus('offline', t('runner.resultsWaitingToSync', { count: failed.length }));
                console.log(`${failed.length} messages still pending`);
            }
        } catch (e) {
            console.error('Failed to sync offline queue:', e);
        }
    }

    updateConnectionStatus(status, message) {
        const statusEl = document.getElementById('connection-status');
        if (statusEl) {
            statusEl.className = 'connection-status ' + status;
            const textEl = statusEl.querySelector('.status-text');
            if (textEl) {
                textEl.textContent = message;
            }
        }
    }

    handleMessage(data) {
        runCommand(data, window.testRunner);
    }

    disconnect() {
        this.connected = false;
        if (this.pollInterval) {
            clearInterval(this.pollInterval);
            this.pollInterval = null;
        }
    }
}

class TestRunner {
    constructor(wsClient) {
        this.wsClient = wsClient;
        this.tests = [
            // The touchscreen step covers the grid and the outer edges of the
            // screen and reports them as one row.
            new TouchTest(),
            new ForceTouchTest(),
            new DisplayTest(),
            new ScreenRotationTest(),
            new SpeakerTest(),
            new MicrophoneTest(),
            new CallTest(),
            new CameraTest(),
            new SensorTest(),
            new LocationTest(),
            new VibrationTest(),
            // Last on purpose. It asks the operator to look at the phone in
            // their hand, which only makes sense once every measurement is
            // done and the phone is free to be turned over.
            new CosmeticTest()
        ];
        this.currentTestIndex = -1;
        this.isRunning = false;
        this.startTime = null;
    }

    /**
     * Starts the suite, optionally picking up from what the desktop already has.
     *
     * A phone reloads for all sorts of reasons: a locked screen, a dropped tab, a
     * launch from the home screen. Restarting from the first step every time means
     * walking the operator through the whole thing again, so the stored verdicts
     * are copied onto the matching tests and the run continues at the first one
     * that has none.
     */
    async startSuite({ resume = null } = {}) {
        this.isRunning = true;
        this.startTime = Date.now();
        this.currentTestIndex = 0;

        if (resume) this.applyStoredResults(resume);

        this.showScreen('test-screen');
        this.updateTestListUI();
        this.updateTestCounter();

        await this.runNextTest();
    }

    /**
     * Copies stored verdicts onto the tests that have the same id, then moves the
     * cursor to the first test without one.
     */
    applyStoredResults(progress) {
        applyStoredResults(this.tests, progress);
        this.currentTestIndex = firstPendingIndex(this.tests, progress);
        return this.currentTestIndex;
    }

    /**
     * Runs one test by id, on the desktop's request.
     *
     * The server sends this when an operator clicks a test on the PC, so an id
     * that is not in the list is ignored rather than throwing: the list on the
     * desktop may be a round trip out of date.
     */
    async startTest(testId) {
        if (!testId) return;

        const index = this.tests.findIndex(t => t.id === testId);
        if (index < 0) {
            console.warn(`Ignoring request for unknown test ${testId}`);
            return;
        }

        this.isRunning = true;
        if (!this.startTime) this.startTime = Date.now();
        this.currentTestIndex = index;

        await this.runOne(index, { then: 'results' });
    }

    /** Stops the run and shows what came in, which is what the desktop asks for. */
    stopSuite() {
        this.isRunning = false;
        this.showResultsScreen(this.buildSuiteResult());
    }

    /**
     * Runs a single test, but never waits on it forever.
     *
     * Several tests only settle from a user gesture (permission prompts, capture
     * buttons, GPS fix). If that gesture never happens the suite used to stall on
     * `await test.run(...)` and no result ever reached the desktop. A 90 s
     * failsafe releases it.
     *
     * The operator's own way out is the button each such test draws for itself, and it
     * is the button that produced the result rather than merely abandoning it. A global
     * one covering the whole suite was tried and removed: it could not be worded for a
     * specific piece of hardware, and it blurred the difference between a device that
     * is broken and a test nobody ran.
     */
    async runTestSafely(test, container) {
        // Each step states how long it needs rather than sharing one ceiling. A
        // step that measures two things needs longer, and a fixed 90 s would cut
        // its second half off and report a fail for hardware it never finished.
        const budgetMs = typeof test.getFailsafeMs === 'function' ? test.getFailsafeMs() : 90000;
        const budgetSeconds = Math.round(budgetMs / 1000);

        const guarded = Promise.race([
            test.run(this.wsClient, container),
            new Promise((resolve) => {
                this._runFailsafe = setTimeout(() => {
                    if (test.status === 'running') {
                        test.fail(t('runner.testTimedOut', { seconds: budgetSeconds }));
                        console.warn(`Test ${test.id} timed out.`);
                    }
                    resolve();
                }, budgetMs);
            }),
        ]);

        try {
            await guarded;
        } finally {
            clearTimeout(this._runFailsafe);
            this._runFailsafe = null;
            if (test.status === 'running') {
                test.fail(t('runner.testEndedWithoutResult'));
            }

            try {
                test.dispose();
            } catch (e) {
                console.warn(`Cleanup failed for test ${test.id}:`, e);
            }
        }
    }

    /**
     * Runs the test at the given index, reports it, and leaves the runner in a
     * defined state either way.
     *
     * Both the suite walk and a single test asked for from the desktop came down
     * to the same body, duplicated once and drifting apart: the suite version
     * toggled the body class, the single version did not, and only one of them
     * reported the counter. `then` decides where control goes afterwards.
     */
    async runOne(index, { then = 'next' } = {}) {
        const test = this.tests[index];
        if (!test) return;

        test.start();
        this.updateTestListUI();
        this.updateTestHeader(test);
        this.updateTestCounter();

        const container = document.getElementById('test-container');
        if (container) container.innerHTML = '';

        // Hide test list and header while test is running
        document.body.classList.add('test-running');
        const testScreen = document.getElementById('test-screen');
        if (testScreen) testScreen.classList.add('test-running');

        try {
            this.wsClient.send({
                type: 'test_start',
                sessionId: this.wsClient.sessionId,
                testId: test.id,
                testName: test.name
            });

            await this.runTestSafely(test, container);
        } catch (error) {
            test.fail(t('runner.exception', { message: error.message }));
            console.error('Test error:', error);
        } finally {
            document.body.classList.remove('test-running');
            if (testScreen) testScreen.classList.remove('test-running');

            // One test_complete per result row, even on an exception. A step that
            // reports two rows has to report both here: sending only the first
            // would leave the second unrecorded on the desktop, and the progress
            // store would keep offering to resume a step that had already run.
            for (const result of test.toResults()) {
                this.wsClient.send({
                    type: 'test_complete',
                    sessionId: this.wsClient.sessionId,
                    testId: result.id,
                    testName: result.name,
                    status: result.status,
                    notes: result.notes,
                    durationMs: result.durationMs,
                    details: result.details,
                    // Only the questionnaire sets one. It rides along so the
                    // label can print a short code while the report keeps the
                    // full question, and nothing else changes shape.
                    labelCode: result.labelCode
                });
            }

            this.updateTestListUI();
        }

        this.currentTestIndex = index + 1;

        if (then === 'next') {
            setTimeout(() => this.runNextTest(), 500);
        }
    }

    async runNextTest() {
        if (!this.isRunning) return;

        if (this.currentTestIndex >= this.tests.length) {
            await this.finishSuite();
            return;
        }

        await this.runOne(this.currentTestIndex, { then: 'next' });
    }

    async finishSuite() {
        this.isRunning = false;

        const suiteResult = this.buildSuiteResult();

        this.wsClient.send({
            type: 'suite_complete',
            sessionId: this.wsClient.sessionId,
            payload: suiteResult
        });

        this.showResultsScreen(suiteResult);
    }

    /**
     * The whole run as the desktop wants it.
     *
     * Three places needed this and each built it by hand, so a field added to the
     * payload only reached one of them. Built once here, from the same tests, in
     * the same shape, every time.
     */
    buildSuiteResult() {
        return {
            sessionId: this.wsClient.sessionId,
            deviceUdid: this.wsClient.sessionId,
            userAgent: navigator.userAgent,
            platform: this.getPlatform(),
            startedAt: this.startTime ? new Date(this.startTime).toISOString() : new Date().toISOString(),
            completedAt: new Date().toISOString(),
            // Flattened, not mapped one-to-one. A step that measures two things
            // contributes two rows, so a grading label that showed only the first
            // would silently drop a verdict.
            tests: this.tests.flatMap(t => t.toResults())
        };
    }

    getPlatform() {
        const ua = navigator.userAgent;
        if (/iPhone|iPad|iPod/.test(ua)) return 'iOS';
        if (/Android/.test(ua)) return 'Android';
        if (/Macintosh/.test(ua)) return 'macOS';
        if (/Windows/.test(ua)) return 'Windows';
        return 'Unknown';
    }

    /**
     * Reruns one step.
     *
     * Indexed by id, not by position. The results screen lists rows and a step
     * can produce more than one of them, so row 2 is not step 2 any more: a
     * positional lookup would rerun whichever step happened to land there.
     */
    async retryTest(testId) {
        const index = this.tests.findIndex(t => t.resultIds().includes(testId));
        if (index < 0) return;

        this.showScreen('test-screen');
        await this.runOne(index, { then: 'results' });

        // Return to results after retry
        setTimeout(() => this.showResultsScreen(this.buildSuiteResult()), 1000);
    }

    showScreen(screenId) {
        document.querySelectorAll('.screen').forEach(el => el.style.display = 'none');
        const screen = document.getElementById(screenId);
        if (screen) screen.style.display = 'block';
    }

    updateTestListUI() {
        const testList = document.getElementById('test-list');
        if (!testList) return;
        
        testList.innerHTML = '';
        this.tests.forEach((test, index) => {
            const li = document.createElement('li');
            li.className = 'test-item ' + test.status;
            li.textContent = test.name;
            if (index === this.currentTestIndex) li.classList.add('active');
            testList.appendChild(li);
        });
    }

    /** Keeps the "3/12" readout in step with the list next to it. */
    updateTestCounter() {
        const current = document.getElementById('current-test-num');
        const total = document.getElementById('total-tests');
        if (total) total.textContent = this.tests.length;
        if (current) current.textContent = Math.min(this.currentTestIndex + 1, this.tests.length);
    }

    updateTestHeader(test) {
        const header = document.getElementById('current-test-name');
        if (header) header.textContent = test.name;
    }

    showResultsScreen(suiteResult) {
        this.showScreen('results-screen');
        const counts = countResults(suiteResult.tests);
        const total = counts.total;

        const elTotal = document.getElementById('total-count');
        const elPassed = document.getElementById('passed-count');
        const elFailed = document.getElementById('failed-count');
        const elSkipped = document.getElementById('skipped-count');
        if (elTotal) elTotal.textContent = total;
        if (elPassed) elPassed.textContent = counts.passed;
        if (elFailed) elFailed.textContent = counts.failed;
        if (elSkipped) elSkipped.textContent = counts.skipped;

        const resultsDetails = document.getElementById('results-details');
        if (resultsDetails) {
            resultsDetails.innerHTML = '';
            suiteResult.tests.forEach((test) => {
                resultsDetails.appendChild(buildResultRow(test));
            });

            // Wire up retry buttons
            const retryButtons = resultsDetails.querySelectorAll('.retry-test-btn');
            retryButtons.forEach(btn => {
                btn.addEventListener('click', () => {
                    if (window.testRunner) {
                        window.testRunner.retryTest(btn.dataset.testId);
                    }
                });
            });
        }
    }

    /**
     * Shows how much of a previous run is already settled, so the operator can
     * pick up instead of starting over.
     *
     * Nothing is shown for a session with no stored steps, which is the ordinary
     * first run and needs no explanation.
     */
    showResumeBanner(progress) {
        const banner = document.getElementById('resume-banner');
        if (!banner) return;

        if (!canResume(progress)) {
            banner.style.display = 'none';
            return;
        }

        const text = banner.querySelector('.resume-text');
        if (text) text.textContent = describeResume(progress);

        banner.style.display = 'flex';
    }
}

// Initialize on page load
window.addEventListener('DOMContentLoaded', async () => {
    // 0. Pick the language and fill the shell with it. The desktop passes its
    //    own on the query string; failing that the browser's is used, and
    //    failing that Dutch. This runs before anything is shown so the English
    //    written into index.html never gets a turn.
    const nav = window.navigator;
    initLocale(window.location.search, (nav && nav.language) || '');
    applyStaticText(document);
    console.log('[i18n] language:', locale());

    // 1. Check iOS standalone mode
    const isIos = /iPad|iPhone|iPod/.test(navigator.userAgent) && !window.MSStream;
    const isStandalone = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    if (isIos && !isStandalone) {
        const banner = document.getElementById('ios-standalone-banner');
        if (banner) banner.style.display = 'block';
    }
    const wsClient = new RestApiClient();

    // A launch from the home screen arrives without a sessionId, so it is worked
    // out before anything is sent. Everything below is tagged with it.
    await wsClient.resolveSessionId();

    // Run capability scan before connection
    const scanner = new CapabilityScanner();
    const scanResult = scanner.scan();
    console.log('[CapabilityScanner] Scan results:', scanResult);
    
    await wsClient.connect();
    
    // Report missing APIs to desktop host
    if (scanResult.missing.length > 0) {
        await scanner.reportMissing(wsClient);
    }
    
    window.testRunner = new TestRunner(wsClient);

    // Ask the desktop how far this device got. The page may be a reload in the
    // middle of a run, or a fresh launch of a device that is already half done.
    const storedProgress = await wsClient.getProgress();
    if (storedProgress) window.testRunner.showResumeBanner(storedProgress);

    let resumeFrom = storedProgress;

    const startBtn = document.getElementById('start-test-btn');
    if (startBtn) {
        startBtn.addEventListener('click', async () => {
            // Starting over on purpose clears the stored run, otherwise the next
            // reload would offer to resume the one that was just abandoned.
            if (resumeFrom) {
                await wsClient.resetProgress();
                resumeFrom = null;
                window.testRunner.tests.forEach(t => t.reset());
            }
            window.testRunner.startSuite();
        });
    }

    const resumeBtn = document.getElementById('resume-btn');
    if (resumeBtn) {
        resumeBtn.addEventListener('click', () => {
            const progress = resumeFrom;
            resumeFrom = null;
            window.testRunner.startSuite({ resume: progress });
        });
    }

    const restartBtn = document.getElementById('restart-btn');
    if (restartBtn) {
        restartBtn.addEventListener('click', async () => {
            await wsClient.resetProgress();
            window.testRunner.tests.forEach(t => t.reset());
            resumeFrom = null;
            window.testRunner.startSuite();
        });
    }

    // Network event listeners for offline sync
    window.addEventListener('online', async () => {
        console.log('Network restored, syncing offline queue...');
        await wsClient.syncOfflineQueue();
    });

    window.addEventListener('offline', () => {
        console.log('Network lost, messages will be queued locally');
        wsClient.updateConnectionStatus('offline', t('runner.offlineSavedLocally'));
    });

    // Flush anything queued while the kiosk was offline. The 'online' listener
    // only fires on a transition, so a queue left behind by a previous run would
    // otherwise sit in localStorage until the network drops and comes back.
    await wsClient.syncOfflineQueue();

});
