import test from 'node:test';
import assert from 'node:assert/strict';

import {
    indexSteps,
    resultIdsOf,
    isStepSettled,
    firstPendingIndex,
    applyStoredResults
} from '../modules/SuiteProgress.js';

/** A single-row step, like most of the suite. */
function simple(id) {
    return { id, name: id, status: 'pending', notes: '', details: {} };
}

/** The touchscreen step, which reports a grid and an edge row from one run. */
function touchStep() {
    return {
        id: 'touch',
        name: 'Touchscreen Test',
        status: 'pending',
        notes: '',
        details: {},
        resultIds: () => ['touch', 'digitizer'],
    };
}

function row(testId, status, reportedAt) {
    return { testId, testName: testId, status, reportedAt };
}

test('a step that reports one row is settled when that row is stored', () => {
    const stored = indexSteps({ steps: [row('touch', 'passed', '2026-09-30T10:00:00.000Z')] });

    assert.equal(isStepSettled(simple('touch'), stored), true);
    assert.equal(isStepSettled(simple('display'), stored), false);
});

test('a step that reports two rows is not settled on one of them', () => {
    // The touchscreen step measures the grid and then the outer edges. A store
    // holding only the grid row is a run that was cut short during the edges, and
    // treating it as done would skip the edges without ever having run them.
    const stored = indexSteps({ steps: [row('touch', 'passed', '2026-09-30T10:00:00.000Z')] });

    assert.equal(isStepSettled(touchStep(), stored), false);
});

test('a two-row step is settled once both of its rows are stored', () => {
    const stored = indexSteps({ steps: [
        row('touch', 'passed', '2026-09-30T10:00:00.000Z'),
        row('digitizer', 'failed', '2026-09-30T10:01:00.000Z')
    ] });

    assert.equal(isStepSettled(touchStep(), stored), true);
});

test('resultIdsOf falls back to the step id when a step does not declare any', () => {
    // An older module has no resultIds at all, and must still resume.
    assert.deepEqual(resultIdsOf(simple('camera')), ['camera']);
    assert.deepEqual(resultIdsOf({ id: 'camera', resultIds: () => [] }), ['camera']);
});

test('firstPendingIndex does not skip a half-recorded two-row step', () => {
    const tests = [touchStep(), simple('display')];
    const progress = { steps: [row('touch', 'passed', '2026-09-30T10:00:00.000Z')] };

    // The touchscreen step is index 0 and is not finished, so the run resumes
    // there. Starting at the display test would leave the edges untested.
    assert.equal(firstPendingIndex(tests, progress), 0);
});

test('firstPendingIndex moves past a fully recorded two-row step', () => {
    const tests = [touchStep(), simple('display')];
    const progress = { steps: [
        row('touch', 'passed', '2026-09-30T10:00:00.000Z'),
        row('digitizer', 'failed', '2026-09-30T10:01:00.000Z')
    ] };

    assert.equal(firstPendingIndex(tests, progress), 1);
});

test('applyStoredResults keeps both rows of a two-row step', () => {
    const step = touchStep();
    const progress = { steps: [
        row('touch', 'passed', '2026-09-30T10:00:00.000Z'),
        row('digitizer', 'failed', '2026-09-30T10:02:00.000Z')
    ] };

    applyStoredResults([step], progress);

    // The step takes the grid's verdict. The edge row is kept alongside it so
    // toResults can report it, and so a failing edge is not swallowed by a
    // passing grid.
    assert.equal(step.status, 'passed');
    assert.equal(step.storedRows.length, 2);
    assert.equal(step.storedRows[1].testId, 'digitizer');
    assert.equal(step.storedRows[1].status, 'failed');
});

test('applyStoredResults on a one-row store leaves the edge unknown', () => {
    const step = touchStep();

    applyStoredResults([step], { steps: [row('touch', 'passed', '2026-09-30T10:00:00.000Z')] });

    assert.equal(step.storedRows.length, 1);
});

test('a two-row step takes its status from its own row and not the last stored', () => {
    const step = touchStep();

    // The order in the store is not the order on screen. Whichever row came last
    // is not what the step's own verdict should become.
    applyStoredResults([step], { steps: [
        row('digitizer', 'failed', '2026-09-30T10:05:00.000Z'),
        row('touch', 'passed', '2026-09-30T10:00:00.000Z')
    ] });

    assert.equal(step.status, 'passed');
});
