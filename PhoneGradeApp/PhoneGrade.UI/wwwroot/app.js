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

class WebSocketClient {
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

        try {
            await test.run(this.wsClient, container);
        } catch (error) {
            test.fail('Exception: ' + error.message);
            console.error('Test error:', error);
        } finally {
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
        const resultsEl = document.getElementById('test-results');
        if (resultsEl) {
            const passedCount = suiteResult.tests.filter(t => t.status === 'passed').length;
            const totalCount = suiteResult.tests.length;
            resultsEl.innerHTML = `<h2>Test Suite Complete</h2><p>${passedCount}/${totalCount} tests passed</p>`;
        }
    }
}

// Initialize on page load
window.addEventListener('DOMContentLoaded', () => {
    const wsClient = new WebSocketClient();
    wsClient.connect();
    
    window.testRunner = new TestRunner(wsClient);
    
    const startBtn = document.getElementById('start-test-btn');
    if (startBtn) {
        startBtn.addEventListener('click', () => {
            window.testRunner.startSuite();
        });
    }
});
