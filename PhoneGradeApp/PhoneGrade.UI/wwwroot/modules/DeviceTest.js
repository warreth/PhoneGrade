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
     *
     * The id and name are overridable because a step may report progress for one
     * row while it is working on another. The touchscreen step measures the grid
     * and then the outer edges, and the operator watching the PC should be told
     * which of the two is moving.
     * @param {WebSocketClient} wsClient
     * @param {number} progress - 0-100 percentage
     * @param {string} message - Optional status message
     * @param {string} testId
     * @param {string} testName
     */
    reportProgress(wsClient, progress, message = '', testId = this.id, testName = this.name) {
        if (wsClient && wsClient.isConnected()) {
            wsClient.send({
                type: 'test_progress',
                testId: testId,
                testName: testName,
                progress: Math.round(progress),
                message: message
            });
        }
    }

    /**
     * How long the runner may wait for this step before failing it.
     *
     * The runner races every run() against a failsafe so a step that never
     * settles cannot stall the suite. A step that measures more than one thing
     * needs a longer budget, and it is better for the step to say how much of it
     * it needs than for the runner to have to guess or for a phase to be quietly
     * cut short. 90 s suits a single measurement.
     */
    getFailsafeMs() {
        return 90000;
    }

    /**
     * Called by the runner once run() has finished, however it finished: normal
     * completion, an exception, a skip, or the 90 s failsafe. Subclasses that
     * hold hardware resources release them here so an abandoned step cannot
     * leave a camera or a torch switched on.
     */
    dispose() {
    }

    /**
     * Mark test as started and lock viewport.
     * The time limit is enforced by TestRunner, which races every run() against
     * a failsafe so a test that never settles cannot stall the suite.
     */
    start() {
        this.status = 'running';
        this.startTime = Date.now();
        this.viewportLocker.lock();
    }

    /**
     * Mark test as passed.
     * @param {string} notes - Optional notes about the test
     */
    pass(notes = '') {
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
     * Puts the test back to its untouched state.
     *
     * Used when the operator starts the suite over on purpose. Without it the
     * previous verdicts stayed on the objects, so a step that was about to fail
     * would be reported with the old status until it actually settled, and the
     * run would look finished before it had begun.
     */
    reset() {
        this.status = 'pending';
        this.notes = '';
        this.startTime = null;
        this.endTime = null;
        this.details = {};
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

    /**
     * The result rows this step produces.
     *
     * One row per verdict, and by default that is one. A step that measures two
     * things reports two, which is why this is a list: the runner, the desktop
     * progress store and the results screen all work in rows, and folding a second
     * verdict into the first would leave a grading label claiming something was
     * checked when it was not, or the other way round.
     */
    toResults() {
        return [this.toJSON()];
    }

    /**
     * The ids this step reports its rows under, in the same order as toResults().
     *
     * Paired with toResults so the resume logic can tell whether a step is
     * settled: a two-row step is only done when both of its ids are stored.
     */
    resultIds() {
        return [this.id];
    }
}
