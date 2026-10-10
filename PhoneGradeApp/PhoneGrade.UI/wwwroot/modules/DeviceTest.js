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
     * Report progress to the desktop.
     *
     * The id and name can be overridden so a step that reports several rows can
     * say which of them is moving. The defaults are the step's own, which is
     * what every step in the suite uses.
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
     * Offer the operator a way to record that the thing being tested is not working.
     *
     * Added to the tests that otherwise leave somebody watching a countdown. It is the
     * same thing the microphone test has always done with its own button, moved into the
     * base class so a test does not have to invent one, and so every such test is
     * worded for its own hardware rather than for the runner.
     *
     * Pressing it settles the test as a failure, which is the point: a phone whose
     * rotation never fires is a finding, and an operator waiting thirty seconds for a
     * result they already know is not going to give one.
     *
     * @param {HTMLElement} container - Where to render the button
     * @param {string} label - What has stopped working, in the operator's words
     * @param {() => void} onPress - What to do when it is pressed
     */
    offerFaultButton(container, label, onPress) {
        const button = document.createElement('button');
        button.className = 'btn btn-danger step-block step-fault';
        button.textContent = label;

        // One press, one result. A second press after the test has settled would write
        // a second status onto a finished test, which is the sort of thing that shows
        // up as a result that disagrees with the report.
        button.addEventListener('click', () => {
            button.disabled = true;
            onPress();
        }, { once: true });

        container.appendChild(button);
        return button;
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
