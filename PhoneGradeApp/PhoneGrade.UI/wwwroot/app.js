import { DeviceTest } from './modules/DeviceTest.js';
import { TouchTest } from './modules/TouchTest.js';
import { DisplayTest } from './modules/DisplayTest.js';
import { MicrophoneTest } from './modules/MicrophoneTest.js';
import { SpeakerTest } from './modules/SpeakerTest.js';
import { CameraTest } from './modules/CameraTest.js';
import { SensorTest } from './modules/SensorTest.js';

/**
 * WebSocket client for communicating with the desktop app's Kestrel server.
 */
class WebSocketClient {
    constructor(url) {
        this.url = url;
        this.socket = null;
        this.sessionId = this.getUrlParam('sessionId') || 'UNKNOWN';
        this.connected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;
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
            this.updateStatus('connected', 'Verbonden met test server');
            this.send({ type: 'init', sessionId: this.sessionId });
        };

        this.socket.onclose = () => {
            this.connected = false;
            this.updateStatus('disconnected', 'Verbinding verbroken');
            
            if (this.reconnectAttempts < this.maxReconnectAttempts) {
                this.reconnectAttempts++;
                setTimeout(() => this.connect(), 1000 * this.reconnectAttempts);
            }
        };

        this.socket.onerror = (error) => {
            this.updateStatus('error', 'WebSocket fout');
            console.error('WebSocket error:', error);
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
        }
    }

    updateStatus(status, message) {
        const statusEl = document.getElementById('status');
        if (statusEl) {
            statusEl.className = 'status ' + status;
            statusEl.textContent = message;
        }
    }

    handleMessage(data) {
        switch (data.type) {
            case 'pong':
                // Keep-alive response
                break;
            case 'test_start':
                // Desktop requests a specific test to start
                this.testRunner.startTest(data.testId);
                break;
            case 'stop_suite':
                // Desktop requests to stop the test suite
                this.testRunner.stopSuite();
                break;
            default:
                console.log('Unknown message type:', data.type);
        }
    }
}

/**
 * Test runner orchestrator.
 */
class TestRunner {
    constructor(wsClient) {
        this.wsClient = wsClient;
        this.tests = [
            new TouchTest(),
            new DisplayTest(),
            new MicrophoneTest(),
            new SpeakerTest(),
            new CameraTest(),
            new SensorTest()
        ];
        this.currentTestIndex = -1;
        this.completedTests = [];
        this.isRunning = false;
    }

    async startSuite() {
        this.isRunning = true;
        this.currentTestIndex = 0;
        
        // Show test list
        this.updateTestListUI();
        
        // Start first test
        await this.startNextTest();
    }

    async startNextTest() {
        if (this.currentTestIndex >= this.tests.length) {
            await this.finishSuite();
            return;
        }

        this.currentTestIndex++;
        await this.runCurrentTest();
    }

    async runCurrentTest() {
        if (this.currentTestIndex >= this.tests.length) return;

        const test = this.tests[this.currentTestIndex];
        this.updateTestListUI();
        
        // Show current test UI
        const currentTestDiv = document.getElementById('current-test');
        currentTestDiv.innerHTML = '';
        currentTestDiv.className = 'current-test';
        currentTestDiv.innerHTML = `
            <h3>${test.name}</h3>
            <p class="test-instructions">${test.description}</p>
            <div id="test-container"></div>
        `;
        
        const container = currentTestDiv.querySelector('#test-container');

        try {
            await test.run(this.wsClient, container);
            this.completedTests.push(test);
            await this.startNextTest();
        } catch (error) {
            test.fail('Exception: ' + error.message);
            this.completedTests.push(test);
            await this.startNextTest();
        }
    }

    startTest(testId) {
        const testIndex = this.tests.findIndex(t => t.id === testId);
        if (testIndex !== -1) {
            this.currentTestIndex = testIndex;
            this.runCurrentTest();
        }
    }

    stopSuite() {
        this.isRunning = false;
    }

    async finishSuite() {
        this.isRunning = false;
        
        // Send final results to desktop
        const suiteResult = {
            sessionId: this.wsClient.sessionId,
            deviceUdid: this.wsClient.sessionId,
            userAgent: navigator.userAgent,
            platform: this.getPlatform(),
            startedAt: new Date(Date.now() - this.totalDuration).toISOString(),
            completedAt: new Date().toISOString(),
            tests: this.completedTests.map(t => t.toJSON())
        };

        this.wsClient.send({
            type: 'suite_complete',
            sessionId: this.wsClient.sessionId,
            payload: suiteResult
        });

        // Show results screen
        this.showResultsScreen(suiteResult);
        
        // Update test list to show all completed
        this.updateTestListUI();
    }

    getPlatform() {
        const ua = navigator.userAgent;
        if (/iPhone|iPad/.test(ua)) return 'iOS';
        if (/Android/.test(ua)) return 'Android';
        if (/Macintosh/.test(ua)) return 'macOS';
        if (/Windows/.test(ua)) return 'Windows';
        return 'Unknown';
    }

    showResultsScreen(suiteResult) {
        const testScreen = document.getElementById('test-screen');
        const resultsScreen = document.getElementById('results-screen');
        
        testScreen.classList.remove('active');
        resultsScreen.classList.add('active');

        const resultsSummary = document.getElementById('results-summary');
        const resultsDetails = document.getElementById('results-details');

        const passed = suiteResult.tests.filter(t => t.status === 'passed').length;
        const failed = suiteResult.tests.filter(t => t.status === 'failed').length;
        const skipped = suiteResult.tests.filter(t => t.status === 'skipped').length;

        resultsSummary.innerHTML = `
            <div class="summary-stat">
                <span class="summary-label">Totaal tests</span>
                <span class="summary-value">${suiteResult.tests.length}</span>
            </div>
            <div class="summary-stat">
                <span class="summary-label">Geslaagd</span>
                <span class="summary-value passed">${passed}</span>
            </div>
            <div class="summary-stat">
                <span class="summary-label">Gefaald</span>
                <span class="summary-value failed">${failed}</span>
            </div>
            <div class="summary-stat">
                <span class="summary-label">Overgeslagen</span>
                <span class="summary-value">${skipped}</span>
            </div>
            <div class="summary-stat">
                <span class="summary-label">Duur</span>
                <span class="summary-value">${suiteResult.tests.reduce((sum, t) => sum + t.durationMs, 0)} ms</span>
            </div>
        `;

        resultsDetails.innerHTML = suiteResult.tests.map(t => `<div class="result-item ${t.status}">
            <div class="result-title">${t.name}: <strong>${t.status}</strong></div>
            ${t.notes ? '<div class="result-notes">' + t.notes + '</div>' : ''}
            <div class="result-notes" style="margin-top: 4px;">${t.durationMs} ms</div>
        </div>`).join('');

        document.getElementById('restart-btn').onclick = () => {
            resultsScreen.classList.remove('active');
            document.getElementById('welcome-screen').classList.add('active');
        };
    }

    updateTestListUI() {
        const testList = document.getElementById('test-list');
        if (!testList) return;

        const currentTest = this.currentTestIndex < this.tests.length ? this.tests[this.currentTestIndex] : null;

        testList.innerHTML = this.tests.map((test, index) => {
            let statusClass = 'pending';
            let icon = '●';
            
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
                icon = '○';
            }

            return `
                <div class="test-item">
                    <div class="test-status-icon ${statusClass}">${icon}</div>
                    <div class="test-info">
                        <div class="test-name">${test.name}</div>
                        <div class="test-description">${test.description}</div>
                    </div>
                </div>
            `;
        }).join('');

        // Highlight current test
        if (currentTest) {
            const items = testList.querySelectorAll('.test-item');
            if (items[this.currentTestIndex]) {
                items[this.currentTestIndex].style.background = 'rgba(0, 255, 136, 0.1)';
            }
        }
    }
}

// Initialize app
let wsClient, testRunner;

document.addEventListener('DOMContentLoaded', () => {
    const startBtn = document.getElementById('start-suite-btn');
    
    if (startBtn) {
        startBtn.onclick = async () => {
            document.getElementById('welcome-screen').classList.remove('active');
            document.getElementById('test-screen').classList.add('active');

            wsClient = new WebSocketClient();
            testRunner = new TestRunner(wsClient);
            wsClient.testRunner = testRunner;
            
            wsClient.connect();
            
            // Start suite after connection established
            setTimeout(() => {
                testRunner.startSuite();
            }, 500);
        };
    }
});

// Add touch gesture handlers for better touch experience
document.addEventListener('touchmove', (e) => {
    if (e.target.closest('.touch-grid')) {
        e.preventDefault();
    }
}, { passive: false });