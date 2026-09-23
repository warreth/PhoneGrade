

import { DeviceTest } from './modules/DeviceTest.js';
import { TouchTest } from './modules/TouchTest.js';
import { DigitizerTest } from './modules/DigitizerTest.js';
import { ForceTouchTest } from './modules/ForceTouchTest.js';
import { DisplayTest } from './modules/DisplayTest.js';
import { ScreenRotationTest } from './modules/ScreenRotationTest.js';
import { ScreenBrightnessTest } from './modules/ScreenBrightnessTest.js';
import { SpeakerTest } from './modules/SpeakerTest.js';
import { MicrophoneTest } from './modules/MicrophoneTest.js';
import { CallTest } from './modules/CallTest.js';
import { CameraTest } from './modules/CameraTest.js';
import { SensorTest } from './modules/SensorTest.js';
import { LocationTest } from './modules/LocationTest.js';
import { VibrationTest } from './modules/VibrationTest.js';
import { RemoteConsoleLogger } from './RemoteConsoleLogger.js';

// CapabilityScanner - detects missing browser APIs at PWA initialization
class CapabilityScanner {
    constructor() {
        this.mandatoryApis = [
            'DeviceMotionEvent',
            'DeviceOrientationEvent', 
            'geolocation',
            'getUserMedia',
            'vibrate',
            'wakeLock'
        ];
        this.missingApis = [];
    }

    scan() {
        const results = {
            DeviceMotionEvent: typeof DeviceMotionEvent !== 'undefined',
            DeviceOrientationEvent: typeof DeviceOrientationEvent !== 'undefined',
            geolocation: 'geolocation' in navigator,
            getUserMedia: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
            vibrate: typeof navigator.vibrate === 'function',
            wakeLock: 'wakeLock' in navigator
        };

        this.missingApis = Object.entries(results)
            .filter(([api, present]) => !present)
            .map(([api]) => api);

        return {
            allPresent: this.missingApis.length === 0,
            missing: this.missingApis,
            results
        };
    }

    async reportMissing(apiClient) {
        if (this.missingApis.length === 0) return;

        const ua = navigator.userAgent;
        let os = 'Unknown';
        let osVersion = '';

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

        for (const missingApi of this.missingApis) {
            try {
                await fetch(`${apiClient.baseUrl}/api/pwa/log-warning`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        sessionId: apiClient.sessionId,
                        missingApi,
                        userAgent: ua,
                        osVersion: `${os} ${osVersion}`.trim()
                    })
                });
            } catch (e) {
                console.warn('Failed to report missing API:', missingApi, e);
            }
        }
    }
}


class CapabilityScanner {
    constructor(apiClient) {
        this.apiClient = apiClient;
        this.requiredApis = {
            'DeviceMotionEvent': () => typeof DeviceMotionEvent !== 'undefined',
            'DeviceOrientationEvent': () => typeof DeviceOrientationEvent !== 'undefined',
            'navigator.geolocation': () => 'geolocation' in navigator,
            'navigator.mediaDevices.getUserMedia': () => navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function',
            'navigator.vibrate': () => typeof navigator.vibrate === 'function',
            'navigator.wakeLock': () => 'wakeLock' in navigator
        };
    }

    async scanAndReport() {
        const ua = navigator.userAgent;
        let osVersion = 'Unknown';
        if (/iPhone|iPad|iPod/.test(ua)) {
            osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || 'iOS Unknown';
        } else if (/Android/.test(ua)) {
            osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || 'Android Unknown';
        }

        for (const [apiName, checkFn] of Object.entries(this.requiredApis)) {
            if (!checkFn()) {
                console.warn(`[CapabilityScanner] Missing API: ${apiName}`);
                try {
                    await fetch(`${this.apiClient.baseUrl}/api/pwa/log-warning`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            sessionId: this.apiClient.sessionId,
                            missingApi: apiName,
                            userAgent: ua,
                            osVersion: osVersion
                        })
                    });
                } catch (e) {
                    console.error(`Failed to report missing API ${apiName}:`, e);
                }
            }
        }
    }
}

class RestApiClient {
    constructor() {
        this.sessionId = this.getUrlParam('sessionId') || 'UNKNOWN';
        this.connected = false;
        const host = window.location.hostname || '127.0.0.1';
        const port = window.location.port ? window.location.port : '5056';
        this.baseUrl = `${window.location.protocol}//${host}:${port}`;
        this.pollInterval = null;
        this.consoleLogger = null;
    }

    getUrlParam(name) {
        const params = new URLSearchParams(window.location.search);
        return params.get(name);
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
                this.updateConnectionStatus('connected', 'Connected');
                
                // 2. Send telemetry
                await this.sendClientTelemetry();
                
                // 3. Start remote console logging
                if (!this.consoleLogger) {
                    this.consoleLogger = new RemoteConsoleLogger(this);
                }
                
                // 4. Run capability scanner
                const scanner = new CapabilityScanner(this);
                await scanner.scanAndReport();
                
                // 5. Start polling for server messages
                this.startPolling();
            } else {
                throw new Error(`Handshake failed: ${resp.status}`);
            }
        } catch (e) {
            this.connected = false;
            this.updateConnectionStatus('error', `Connection Error: ${e.message}`);
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

    async send(message) {
        if (!this.connected) {
            console.warn('Not connected, dropping message:', message);
            return;
        }

        try {
            let endpoint = '/api/pwa/submit-step';
            if (message.type === 'suite_complete') {
                endpoint = '/api/pwa/submit';
            } else if (message.type === 'log_event') {
                endpoint = '/api/pwa/log';
            }

            await fetch(`${this.baseUrl}${endpoint}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(message)
            });
        } catch (e) {
            console.error('Failed to send message:', e);
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
        switch (data.type) {
            case 'auto_start_suite':
                if (window.testRunner) window.testRunner.startSuite();
                break;
            case 'test_start':
                if (window.testRunner) window.testRunner.startTest(data.testId);
                break;
            case 'stop_suite':
                if (window.testRunner) window.testRunner.stopSuite();
                break;
        }
    }

    disconnect() {
        this.connected = false;
        if (this.pollInterval) {
            clearInterval(this.pollInterval);
            this.pollInterval = null;
        }
    }
}

class OldWebSocketClient {
    constructor() {
        this.socket = null;
        this.sessionId = this.getUrlParam('sessionId') || 'UNKNOWN';
        this.connected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;
        this.messageQueue = [];
        this.consoleLogger = null;
    }

    getUrlParam(name) {
        const params = new URLSearchParams(window.location.search);
        return params.get(name);
    }

    connect() {
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = `${protocol}//${window.location.host}/ws/device-session?sessionId=${this.sessionId}`;

        this.socket = new WebSocket(wsUrl);

        this.socket.onopen = () => {
            this.connected = true;
            this.reconnectAttempts = 0;
            this.updateConnectionStatus('connected', 'Connected');
            
            // Send init with session ID
            this.send({ type: 'init', sessionId: this.sessionId });
            
            // Capture and send client telemetry immediately
            this.sendClientTelemetry();
            
            // Start remote console logging
            if (!this.consoleLogger) {
                this.consoleLogger = new RemoteConsoleLogger(this);
            }
            
            // Drain message queue
            while (this.messageQueue.length > 0) {
                this.send(this.messageQueue.shift());
            }

            if (this.heartbeatInterval) clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = setInterval(() => {
                if (this.isConnected()) {
                    this.send({ type: 'ping', sessionId: this.sessionId });
                }
            }, 10000);
        };

        this.socket.onclose = () => {
            this.connected = false;
            if (this.heartbeatInterval) clearInterval(this.heartbeatInterval);
            this.updateConnectionStatus('disconnected', 'Disconnected');
            
            if (this.reconnectAttempts < this.maxReconnectAttempts) {
                this.reconnectAttempts++;
                setTimeout(() => this.connect(), 1000 * this.reconnectAttempts);
            }
        };

        this.socket.onerror = () => {
            this.updateConnectionStatus('error', 'Connection Error');
        };

        this.socket.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                this.handleMessage(data);
            } catch (e) {
                console.error('Failed to parse message:', e);
            }
        };
    }

    sendClientTelemetry() {
        const ua = navigator.userAgent;
        let browser = 'Unknown';
        let browserVersion = '';
        let os = 'Unknown';
        let osVersion = '';

        // Parse browser
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

        // Parse OS
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

        const clientTelemetry = {
            userAgent: ua,
            browser: browser,
            browserVersion: browserVersion,
            os: os,
            osVersion: osVersion,
            screenWidth: window.screen.width,
            screenHeight: window.screen.height,
            pixelRatio: window.devicePixelRatio || 1.0,
            touchSupport: () => 'ontouchstart' in window || navigator.maxTouchPoints > 0,
            accelerometerSupport: () => typeof DeviceMotionEvent !== 'undefined',
            gyroscopeSupport: () => typeof DeviceOrientationEvent !== 'undefined',
            geolocationSupport: () => 'geolocation' in navigator,
            webAudioSupport: () => typeof AudioContext !== 'undefined' || typeof webkitAudioContext !== 'undefined',
            cameraSupport: () => navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function',
            microphoneSupport: () => navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function',
            vibrationSupport: () => typeof navigator.vibrate === 'function',
            language: navigator.language || 'Unknown',
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone
        };

        // Convert functions to boolean values
        const telemetry = {
            userAgent: clientTelemetry.userAgent,
            browser: clientTelemetry.browser,
            browserVersion: clientTelemetry.browserVersion,
            os: clientTelemetry.os,
            osVersion: clientTelemetry.osVersion,
            screenWidth: clientTelemetry.screenWidth,
            screenHeight: clientTelemetry.screenHeight,
            pixelRatio: clientTelemetry.pixelRatio,
            touchSupport: clientTelemetry.touchSupport(),
            accelerometerSupport: clientTelemetry.accelerometerSupport(),
            gyroscopeSupport: clientTelemetry.gyroscopeSupport(),
            geolocationSupport: clientTelemetry.geolocationSupport(),
            webAudioSupport: clientTelemetry.webAudioSupport(),
            cameraSupport: clientTelemetry.cameraSupport(),
            microphoneSupport: clientTelemetry.microphoneSupport(),
            vibrationSupport: clientTelemetry.vibrationSupport(),
            language: clientTelemetry.language,
            timezone: clientTelemetry.timezone
        };

        this.send({
            type: 'client_telemetry',
            sessionId: this.sessionId,
            clientTelemetry: telemetry
        });
    }

    isConnected() {
        return this.connected && this.socket && this.socket.readyState === WebSocket.OPEN;
    }

    send(message) {
        if (this.isConnected()) {
            this.socket.send(JSON.stringify(message));
        } else {
            this.messageQueue.push(message);
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
        switch (data.type) {
            case 'pong':
                break;
            case 'auto_start_suite':
                if (window.testRunner) {
                    window.testRunner.startSuite();
                }
                break;
            case 'test_start':
                if (window.testRunner) {
                    window.testRunner.startTest(data.testId);
                }
                break;
            case 'stop_suite':
                if (window.testRunner) {
                    window.testRunner.stopSuite();
                }
                break;
        }
    }
}

class TestRunner {
    constructor(wsClient) {
        this.wsClient = wsClient;
        this.tests = [
            new TouchTest(),
            new DigitizerTest(),
            new ForceTouchTest(),
            new DisplayTest(),
            new ScreenRotationTest(),
            new ScreenBrightnessTest(),
            new SpeakerTest(),
            new MicrophoneTest(),
            new CallTest(),
            new CameraTest(),
            new SensorTest(),
            new LocationTest(),
            new VibrationTest()
        ];
        this.currentTestIndex = -1;
        this.isRunning = false;
        this.startTime = null;
    }

    async startSuite() {
        this.isRunning = true;
        this.startTime = Date.now();
        this.currentTestIndex = 0;
        
        this.showScreen('test-screen');
        this.updateTestListUI();
        
        await this.runNextTest();
    }

    async runNextTest() {
        if (this.currentTestIndex >= this.tests.length) {
            await this.finishSuite();
            return;
        }

        const test = this.tests[this.currentTestIndex];
        test.start();
        
        this.updateTestListUI();
        this.updateTestHeader(test);
        
        const container = document.getElementById('test-container');
        container.innerHTML = '';
        
        // Hide test list and header while test is running
        document.body.classList.add('test-running');
        const testScreen = document.getElementById('test-screen');
        if (testScreen) testScreen.classList.add('test-running');

        try {
            await test.run(this.wsClient, container);
        } catch (error) {
            test.fail('Exception: ' + error.message);
            console.error('Test error:', error);
        } finally {
            // Remove test-running class
            document.body.classList.remove('test-running');
            if (testScreen) testScreen.classList.remove('test-running');
            
            // Guarantee immediate test_complete dispatch to host even on error/exception
            this.wsClient.send({
                type: 'test_complete',
                sessionId: this.wsClient.sessionId,
                testId: test.id,
                testName: test.name,
                status: test.status,
                notes: test.notes,
                durationMs: test.getDuration(),
                details: test.details
            });
        }

        this.currentTestIndex++;
        
        setTimeout(() => {
            this.runNextTest();
        }, 500);
    }

    async finishSuite() {
        this.isRunning = false;
        
        const suiteResult = {
            sessionId: this.wsClient.sessionId,
            deviceUdid: this.wsClient.sessionId,
            userAgent: navigator.userAgent,
            platform: this.getPlatform(),
            startedAt: new Date(this.startTime).toISOString(),
            completedAt: new Date().toISOString(),
            tests: this.tests.map(t => t.toJSON())
        };

        this.wsClient.send({
            type: 'suite_complete',
            sessionId: this.wsClient.sessionId,
            payload: suiteResult
        });

        this.showResultsScreen(suiteResult);
    }

    getPlatform() {
        const ua = navigator.userAgent;
        if (/iPhone|iPad|iPod/.test(ua)) return 'iOS';
        if (/Android/.test(ua)) return 'Android';
        if (/Macintosh/.test(ua)) return 'macOS';
        if (/Windows/.test(ua)) return 'Windows';
        return 'Unknown';
    }

    async retryTest(testIndex) {
        if (testIndex < 0 || testIndex >= this.tests.length) return;
        
        this.currentTestIndex = testIndex;
        this.showScreen('test-screen');
        
        const test = this.tests[testIndex];
        test.start();
        
        this.updateTestListUI();
        this.updateTestHeader(test);
        
        const container = document.getElementById('test-container');
        container.innerHTML = '';
        
        const testScreen = document.getElementById('test-screen');
        if (testScreen) testScreen.classList.add('test-running');

        try {
            await test.run(this.wsClient, container);
        } catch (error) {
            test.fail('Exception: ' + error.message);
            console.error('Test error:', error);
        } finally {
            if (testScreen) testScreen.classList.remove('test-running');
            
            this.wsClient.send({
                type: 'test_complete',
                sessionId: this.wsClient.sessionId,
                testId: test.id,
                testName: test.name,
                status: test.status,
                notes: test.notes,
                durationMs: test.getDuration(),
                details: test.details
            });
        }
        
        // Return to results after retry
        setTimeout(() => {
            this.showResultsScreen({
                sessionId: this.wsClient.sessionId,
                deviceUdid: this.wsClient.sessionId,
                userAgent: navigator.userAgent,
                platform: this.getPlatform(),
                startedAt: new Date(this.startTime).toISOString(),
                completedAt: new Date().toISOString(),
                tests: this.tests.map(t => t.toJSON())
            });
        }, 1000);
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

    updateTestHeader(test) {
        const header = document.getElementById('current-test-name');
        if (header) header.textContent = test.name;
    }

    showResultsScreen(suiteResult) {
        this.showScreen('results-screen');
        const passedCount = suiteResult.tests.filter(t => t.status === 'passed').length;
        const failedCount = suiteResult.tests.filter(t => t.status === 'failed').length;
        const totalCount = suiteResult.tests.length;

        const elTotal = document.getElementById('total-count');
        const elPassed = document.getElementById('passed-count');
        const elFailed = document.getElementById('failed-count');
        if (elTotal) elTotal.textContent = totalCount;
        if (elPassed) elPassed.textContent = passedCount;
        if (elFailed) elFailed.textContent = failedCount;

        const resultsDetails = document.getElementById('results-details');
        if (resultsDetails) {
            resultsDetails.innerHTML = '';
            suiteResult.tests.forEach((test, index) => {
                const item = document.createElement('div');
                item.className = 'result-item';
                item.style.cssText = 'display: flex; justify-content: space-between; align-items: center; padding: 12px; background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 8px; margin-bottom: 8px;';
                
                const canRetry = test.status === 'failed' || test.status === 'skipped';
                
                item.innerHTML = `
                    <span style="font-weight:600; flex: 1;">${test.name}</span>
                    <span class="result-badge ${test.status}" style="margin-right: 8px;">${test.status.toUpperCase()}</span>
                    ${canRetry ? '<button class="btn btn-secondary retry-test-btn" data-test-index="' + index + '" style="padding: 4px 12px; font-size: 12px; height: 32px;">Opnieuw</button>' : ''}
                `;
                resultsDetails.appendChild(item);
            });
            
            // Wire up retry buttons
            const retryButtons = resultsDetails.querySelectorAll('.retry-test-btn');
            retryButtons.forEach(btn => {
                btn.addEventListener('click', () => {
                    const testIndex = parseInt(btn.dataset.testIndex, 10);
                    if (window.testRunner) {
                        window.testRunner.retryTest(testIndex);
                    }
                });
            });
        }
    }
}

// Initialize on page load
window.addEventListener('DOMContentLoaded', async () => {
    const wsClient = new RestApiClient();
    
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
    
    const startBtn = document.getElementById('start-test-btn');
    if (startBtn) {
        startBtn.addEventListener('click', () => {
            window.testRunner.startSuite();
        });
    }

    // Hold-to-skip functionality (requires 2-second hold)
    const skipBtn = document.getElementById('skip-test-btn');
    if (skipBtn) {
        let holdTimer = null;
        let holdProgress = 0;
        let holdInterval = null;
        const holdDuration = 2000;
        const originalText = skipBtn.textContent;

        const startHold = () => {
            holdProgress = 0;
            skipBtn.textContent = 'Houd vast om over te slaan (0%)';
            skipBtn.style.background = '#94a3b8';
            
            holdTimer = setTimeout(() => {
                if (window.testRunner && window.testRunner.isRunning) {
                    const test = window.testRunner.tests[window.testRunner.currentTestIndex];
                    if (test) {
                        test.skip();
                    }
                }
                skipBtn.textContent = 'Overgeslagen';
                skipBtn.style.background = '#64748b';
                setTimeout(() => {
                    skipBtn.textContent = originalText;
                    skipBtn.style.background = '';
                }, 1000);
            }, holdDuration);

            holdInterval = setInterval(() => {
                holdProgress += 50;
                const pct = Math.round((holdProgress / holdDuration) * 100);
                skipBtn.textContent = 'Houd vast om over te slaan (' + pct + '%)';
            }, 50);
        };

        const cancelHold = () => {
            if (holdTimer) clearTimeout(holdTimer);
            if (holdInterval) clearInterval(holdInterval);
            holdTimer = null;
            holdInterval = null;
            holdProgress = 0;
            skipBtn.textContent = originalText;
            skipBtn.style.background = '';
        };

        skipBtn.addEventListener('mousedown', startHold);
        skipBtn.addEventListener('touchstart', startHold);
        skipBtn.addEventListener('mouseup', cancelHold);
        skipBtn.addEventListener('mouseleave', cancelHold);
        skipBtn.addEventListener('touchend', cancelHold);
        skipBtn.addEventListener('touchcancel', cancelHold);
    }
});
