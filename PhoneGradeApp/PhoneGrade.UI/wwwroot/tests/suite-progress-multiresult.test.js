import test from 'node:test';
import assert from 'node:assert/strict';

import {
    indexSteps,
    resultIdsOf,
    isStepSettled,
    firstPendingIndex,
    applyStoredResults
} from '../modules/SuiteProgress.js';

/**
 * Steps that report more than one row.
 *
 * No step in the suite does this today: the touchscreen step used to report
 * its grid and its outer edges separately and now folds both into one row.
 * The store still has to handle it, because the contract is "every row this
 * step declares", and a step measuring two independent things can only be
 * resumed honestly when both of its verdicts are there.
 */
function twoRowStep() {
    return {
        id: 'measure',
        name: 'Two Measurements',
        status: 'pending',
        notes: '',
        details: {},
        resultIds: () => ['measure', 'measure-alt']
    };
}

/** A single-row step, like every step in the suite. */
function simple(id) {
    return { id, name: id, status: 'pending', notes: '', details: {} };
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
    // A store holding only the first row is a run that was cut short, and
    // treating it as done would skip the half that never finished.
    const stored = indexSteps({ steps: [row('measure', 'passed', '2026-09-30T10:00:00.000Z')] });

    assert.equal(isStepSettled(twoRowStep(), stored), false);
});

test('a two-row step is settled once both of its rows are stored', () => {
    const stored = indexSteps({ steps: [
        row('measure', 'passed', '2026-09-30T10:00:00.000Z'),
        row('measure-alt', 'failed', '2026-09-30T10:01:00.000Z')
    ] });

    assert.equal(isStepSettled(twoRowStep(), stored), true);
});

test('resultIdsOf falls back to the step id when a step does not declare any', () => {
    // An older module has no resultIds at all, and must still resume.
    assert.deepEqual(resultIdsOf(simple('camera')), ['camera']);
    assert.deepEqual(resultIdsOf({ id: 'camera', resultIds: () => [] }), ['camera']);
});

test('firstPendingIndex does not skip a half-recorded two-row step', () => {
    const tests = [twoRowStep(), simple('display')];
    const progress = { steps: [row('measure', 'passed', '2026-09-30T10:00:00.000Z')] };

    // The two-row step is index 0 and is not finished, so the run resumes
    // there. Starting at the display test would leave the second half untested.
    assert.equal(firstPendingIndex(tests, progress), 0);
});

test('firstPendingIndex moves past a fully recorded two-row step', () => {
    const tests = [twoRowStep(), simple('display')];
    const progress = { steps: [
        row('measure', 'passed', '2026-09-30T10:00:00.000Z'),
        row('measure-alt', 'failed', '2026-09-30T10:01:00.000Z')
    ] };

    assert.equal(firstPendingIndex(tests, progress), 1);
});

test('applyStoredResults keeps both rows of a two-row step', () => {
    const step = twoRowStep();
    const progress = { steps: [
        row('measure', 'passed', '2026-09-30T10:00:00.000Z'),
        row('measure-alt', 'failed', '2026-09-30T10:02:00.000Z')
    ] };

    applyStoredResults([step], progress);

    // The step takes its first row's verdict. The second row is kept alongside
    // it so toResults can report it, and so a failing half is not swallowed by
    // a passing one.
    assert.equal(step.status, 'passed');
    assert.equal(step.storedRows.length, 2);
    assert.equal(step.storedRows[1].testId, 'measure-alt');
    assert.equal(step.storedRows[1].status, 'failed');
});

test('applyStoredResults on a one-row store leaves the second row unknown', () => {
    const step = twoRowStep();

    applyStoredResults([step], { steps: [row('measure', 'passed', '2026-09-30T10:00:00.000Z')] });

    assert.equal(step.storedRows.length, 1);
});

test('a two-row step takes its status from its own row and not the last stored', () => {
    const step = twoRowStep();

    // The order in the store is not the order on screen. Whichever row came last
    // is not what the step's own verdict should become.
    applyStoredResults([step], { steps: [
        row('measure-alt', 'failed', '2026-09-30T10:05:00.000Z'),
        row('measure', 'passed', '2026-09-30T10:00:00.000Z')
    ] });

    assert.equal(step.status, 'passed');
});
