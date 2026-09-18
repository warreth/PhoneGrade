import { ViewportLocker } from './ViewportLocker.js';
import { HapticFeedback } from './HapticFeedback.js';

/**
 * Base class for all device hardware tests.
 * Provides common lifecycle methods, WebSocket communication, viewport locking, and haptic feedback.
 */
export class DeviceTest {
    constructor(id, name, description) {
        this.id = id;
        this.name = name;
        this.description = description;
        this.status = 'pending'; // pending, running, passed, failed, skipped
        this.notes = '';
        this.startTime = null;
        this.endTime = null;
        this.details = {};
        
        // Shared utilities
        this.viewportLocker = new ViewportLocker();
        this.haptic = new HapticFeedback();
    }

    /**
     * Called when the test should start. Override in subclasses.
     * @param {WebSocketClient} wsClient - WebSocket client for streaming updates
     * @param {HTMLElement} container - DOM container for test UI
     * @returns {Promise<void>}
     */
    async run(wsClient, container) {
        throw new Error('DeviceTest.run() must be overridden in subclass');
    }

    /**
     * Report progress to the desktop app via WebSocket.
     * @param {WebSocketClient} wsClient
     * @param {number} progress - 0-100 percentage
     * @param {string} message - Optional status message
     */
    reportProgress(wsClient, progress, message = '') {
        if (wsClient && wsClient.isConnected()) {
            wsClient.send({
                type: 'test_progress',
                testId: this.id,
                testName: this.name,
                progress: Math.round(progress),
                message: message
            });
        }
    }

    /**
     * Mark test as started and lock viewport.
     */
    start() {
        this.status = 'running';
        this.startTime = Date.now();
        this.viewportLocker.lock();
        
        // Failsafe timeout for all tests (90 seconds max)
        this._globalTimeout = setTimeout(() => {
            if (this.status === 'running') {
                this.fail('Test timed out (90s limit reached)');
                console.warn(`Test ${this.id} timed out.`);
            }
        }, 90000);
    }

    /**
     * Mark test as passed.
     * @param {string} notes - Optional notes about the test
     */
    pass(notes = '') {
        if (this._globalTimeout) clearTimeout(this._globalTimeout);
        this.status = 'passed';
        this.endTime = Date.now();
        this.notes = notes;
        this.viewportLocker.unlock();
        this.haptic.success();
    }

    /**
     * Mark test as failed.
     * @param {string} notes - Explanation of what failed
     */
    fail(notes = '') {
        if (this._globalTimeout) clearTimeout(this._globalTimeout);
        this.status = 'failed';
        this.endTime = Date.now();
        this.notes = notes;
        this.viewportLocker.unlock();
        this.haptic.error();
    }

    /**
     * Mark test as skipped.
     * @param {string} reason - Why the test was skipped
     */
    skip(reason = '') {
        if (this._globalTimeout) clearTimeout(this._globalTimeout);
        this.status = 'skipped';
        this.endTime = Date.now();
        this.notes = reason;
        this.viewportLocker.unlock();
    }

    /**
     * Get the duration in milliseconds.
     */
    getDuration() {
        if (this.startTime && this.endTime) {
            return this.endTime - this.startTime;
        }
        return 0;
    }

    /**
     * Serialize test result for transmission to desktop app.
     */
    toJSON() {
        return {
            id: this.id,
            name: this.name,
            status: this.status,
            notes: this.notes,
            durationMs: this.getDuration(),
            details: this.details
        };
    }
}
