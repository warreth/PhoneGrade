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

class WebSocketClient {
    constructor() {
        this.socket = null;
        this.sessionId = this.getUrlParam('sessionId') || 'UNKNOWN';
        this.connected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;
        this.messageQueue = [];
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
            this.send({ type: 'init', sessionId: this.sessionId });
            
            while (this.messageQueue.length > 0) {
                this.send(this.messageQueue.shift());
            }
        };

        this.socket.onclose = () => {
            this.connected = false;
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
            
            this.wsClient.send({
                type: 'test_complete',
                sessionId: this.wsClient.sessionId,
                testId: test.id,
                testName: test.name,
                status: test.status
            });
        } catch (error) {
            test.fail('Exception: ' + error.message);
            console.error('Test error:', error);
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

    stopSuite() {
        this.isRunning = false;
    }

    updateTestHeader(test) {
        const nameEl = document.getElementById('current-test-name');
        const descEl = document.getElementById('current-test-description');
        const counterEl = document.getElementById('test-counter');
        const currentNum = document.getElementById('current-test-num');
        const totalTests = document.getElementById('total-tests');
        const progressFill = document.getElementById('test-progress-fill');

        if (nameEl) nameEl.textContent = test.name;
        if (descEl) descEl.textContent = test.description;
        if (currentNum) currentNum.textContent = this.currentTestIndex + 1;
        if (totalTests) totalTests.textContent = this.tests.length;
        
        const progress = ((this.currentTestIndex + 1) / this.tests.length) * 100;
        if (progressFill) {
            progressFill.style.width = progress + '%';
        }
    }

    updateTestListUI() {
        const testList = document.getElementById('test-list');
        if (!testList) return;

        testList.innerHTML = this.tests.map((test, index) => {
            let statusClass = 'pending';
            let icon = '○';
            
            if (test.status === 'running') {
                statusClass = 'running';
                icon = '↻';
            } else if (test.status === 'passed') {
                statusClass = 'passed';
                icon = '✓';
            } else if (test.status === 'failed') {
                statusClass = 'failed';
                icon = '✗';
            } else if (test.status === 'skipped') {
                statusClass = 'skipped';
                icon = '−';
            }

            return `
                <div class="test-item ${statusClass}" role="listitem">
                    <div class="test-status-icon">${icon}</div>
                    <div class="test-name">${test.name.replace(' Test', '')}</div>
                </div>
            `;
        }).join('');
    }

    showResultsScreen(suiteResult) {
        this.showScreen('results-screen');

        const passed = suiteResult.tests.filter(t => t.status === 'passed').length;
        const failed = suiteResult.tests.filter(t => t.status === 'failed').length;
        const skipped = suiteResult.tests.filter(t => t.status === 'skipped').length;
        const allPassed = failed === 0 && passed > 0;

        const statusIcon = document.getElementById('results-status-icon');
        const resultsTitle = document.getElementById('results-title');
        
        if (statusIcon) {
            statusIcon.textContent = allPassed ? '✓' : '⚠';
            statusIcon.style.color = allPassed ? 'var(--color-success)' : 'var(--color-warning)';
        }
        
        if (resultsTitle) {
            resultsTitle.textContent = allPassed ? 'All Tests Passed!' : 'Tests Complete';
        }

        document.getElementById('total-count').textContent = suiteResult.tests.length;
        document.getElementById('passed-count').textContent = passed;
        document.getElementById('failed-count').textContent = failed;
        document.getElementById('skipped-count').textContent = skipped;

        const resultsDetails = document.getElementById('results-details');
        resultsDetails.innerHTML = suiteResult.tests.map(t => `
            <div class="result-item ${t.status}" role="listitem">
                <div class="result-title">
                    ${t.name}
                    <span class="result-status-badge ${t.status}">${t.status}</span>
                </div>
                ${t.notes ? '<div class="result-notes">' + t.notes + '</div>' : ''}
                <div class="result-duration">${t.durationMs}ms</div>
            </div>
        `).join('');

        document.getElementById('export-results-btn').onclick = () => {
            const blob = new Blob([JSON.stringify(suiteResult, null, 2)], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `phonegrade-results-${Date.now()}.json`;
            a.click();
            URL.revokeObjectURL(url);
        };

        document.getElementById('restart-btn').onclick = () => {
            this.tests.forEach(t => {
                t.status = 'pending';
                t.notes = '';
                t.startTime = null;
                t.endTime = null;
            });
            this.currentTestIndex = 0;
            this.showScreen('welcome-screen');
        };
    }

    showScreen(screenId) {
        document.querySelectorAll('.screen').forEach(screen => {
            screen.classList.remove('screen-active');
        });
        const screen = document.getElementById(screenId);
        if (screen) {
            screen.classList.add('screen-active');
        }
    }
}

function detectDevice() {
    const ua = navigator.userAgent;
    let device = 'Unknown Device';
    let browser = 'Unknown Browser';

    if (/iPhone/.test(ua)) device = 'iPhone';
    else if (/iPad/.test(ua)) device = 'iPad';
    else if (/Android/.test(ua)) device = 'Android';
    else if (/Macintosh/.test(ua)) device = 'Mac';
    else if (/Windows/.test(ua)) device = 'Windows PC';

    if (/Safari/.test(ua) && !/Chrome/.test(ua)) browser = 'Safari';
    else if (/Chrome/.test(ua)) browser = 'Chrome';
    else if (/Firefox/.test(ua)) browser = 'Firefox';
    else if (/Edge/.test(ua)) browser = 'Edge';

    return { device, browser };
}

document.addEventListener('DOMContentLoaded', () => {
    const { device, browser } = detectDevice();
    
    document.getElementById('device-type').textContent = device;
    document.getElementById('browser-info').textContent = browser;

    const params = new URLSearchParams(window.location.search);
    const sessionId = params.get('sessionId') || 'DEMO';
    
    const sessionInfo = document.getElementById('session-info');
    if (sessionInfo) {
        sessionInfo.textContent = `Session: ${sessionId}`;
    }

    const wsClient = new WebSocketClient();
    const testRunner = new TestRunner(wsClient);
    
    window.testRunner = testRunner;
    window.wsClient = wsClient;

    wsClient.connect();

    const startBtn = document.getElementById('start-suite-btn');
    if (startBtn) {
        startBtn.onclick = () => {
            testRunner.startSuite();
        };
    }

    const skipBtn = document.getElementById('skip-test-btn');
    if (skipBtn) {
        skipBtn.onclick = () => {
            if (testRunner.isRunning && testRunner.currentTestIndex < testRunner.tests.length) {
                const currentTest = testRunner.tests[testRunner.currentTestIndex];
                currentTest.skip('Skipped by user');
                testRunner.currentTestIndex++;
                testRunner.runNextTest();
            }
        };
    }
});

document.addEventListener('touchmove', (e) => {
    if (e.target.closest('.touch-grid')) {
        e.preventDefault();
    }
}, { passive: false });