import { RemoteConsoleLogger } from './RemoteConsoleLogger.js';

class RestApiClient {
    constructor() {
        this.sessionId = this.getUrlParam('sessionId') || 'UNKNOWN';
        this.connected = false;
        this.baseUrl = `${window.location.protocol}//${window.location.host}`;
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
                
                // 4. Start polling for server messages (optional, only if server needs to push commands)
                this.startPolling();
            } else {
                throw new Error(`Handshake failed: ${resp.status}`);
            }
        } catch (e) {
            this.connected = false;
            this.updateConnectionStatus('error', `Connection Error: ${e.message}`);
            // Retry after 2s
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
                    // Server can send commands via status endpoint if needed
                    if (data.command) {
                        this.handleMessage(data.command);
                    }
                }
            } catch (e) {
                // Silent fail on polling errors
            }
        }, 2000);
    }

    async sendClientTelemetry() {
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

        const telemetry = {
            userAgent: ua,
            browser: browser,
            browserVersion: browserVersion,
            os: os,
            osVersion: osVersion,
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

    disconnect() {
        this.connected = false;
        if (this.pollInterval) {
            clearInterval(this.pollInterval);
            this.pollInterval = null;
        }
    }
}
