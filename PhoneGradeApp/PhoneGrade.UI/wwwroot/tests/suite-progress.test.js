import test from 'node:test';
import assert from 'node:assert/strict';

import {
    indexSteps,
    firstPendingIndex,
    applyStoredResults,
    canResume,
    describeResume
} from '../modules/SuiteProgress.js';

function makeTests(...ids) {
    return ids.map(id => ({ id, name: id.toUpperCase(), status: 'pending', notes: '', details: {} }));
}

/** A snapshot shaped exactly like the one the desktop returns. */
function snapshot(steps, extra = {}) {
    return {
        sessionId: '38091FDJG00EMF',
        started: true,
        finished: false,
        currentTestId: null,
        currentTestName: null,
        totalTests: 12,
        steps: steps.map((s, i) => ({
            testId: s[0],
            testName: s[0].toUpperCase(),
            status: s[1] || 'passed',
            reportedAt: s[2] || `2026-09-30T10:0${i}:00.000Z`
        })),
        ...extra
    };
}

test('firstPendingIndex continues at the first test with no stored verdict', () => {
    const tests = makeTests('touch', 'force', 'display', 'rotation');

    // The run got through the first two, so the third is where it picks up.
    const progress = snapshot([['touch'], ['force', 'failed']]);
    assert.equal(firstPendingIndex(tests, progress), 2);
});

test('firstPendingIndex starts at the top when nothing is stored', () => {
    const tests = makeTests('touch', 'force');
    assert.equal(firstPendingIndex(tests, { started: false, steps: [] }), 0);
    assert.equal(firstPendingIndex(tests, null), 0);
});

test('firstPendingIndex is past the end when everything is done', () => {
    const tests = makeTests('touch', 'force');
    const progress = snapshot([['touch'], ['force']]);
    assert.equal(firstPendingIndex(tests, progress), 2);
});

test('firstPendingIndex ignores a stored step for a test that no longer exists', () => {
    // A step can be dropped or renamed between two builds of the PWA. The run
    // then has a hole the phone does not know about, and starting at the first
    // known test is the only thing that gets through the suite.
    const tests = makeTests('touch', 'display');
    const progress = snapshot([['touch'], ['legacy-step']]);
    assert.equal(firstPendingIndex(tests, progress), 1);
});

test('firstPendingIndex matches ids regardless of case', () => {
    const tests = makeTests('Touch', 'Display');
    const progress = snapshot([['touch']]);
    assert.equal(firstPendingIndex(tests, progress), 1);
});

test('applyStoredResults copies the stored verdict rather than re-running it', () => {
    const tests = makeTests('touch', 'display');
    const progress = snapshot([['touch', 'passed'], ['display', 'failed']]);

    const applied = applyStoredResults(tests, progress);

    assert.deepEqual(applied, ['touch', 'display']);
    assert.equal(tests[0].status, 'passed');
    assert.equal(tests[1].status, 'failed');
    assert.match(tests[1].notes, /vorige run/);
    assert.match(tests[0].notes, /vorige run/);
});

test('applyStoredResults leaves untouched tests alone', () => {
    const tests = makeTests('touch', 'display');
    tests[0].status = 'failed';
    tests[0].notes = 'from this run';

    applyStoredResults(tests, snapshot([['display']]));

    assert.equal(tests[0].status, 'failed');
    assert.equal(tests[0].notes, 'from this run');
});

test('applyStoredResults survives a snapshot with junk in it', () => {
    const tests = makeTests('touch');
    const progress = { started: true, steps: [null, {}, { testId: 'touch', status: 'passed', reportedAt: 'x' }] };

    assert.deepEqual(applyStoredResults(tests, progress), ['touch']);
    assert.equal(tests[0].status, 'passed');
});

test('indexSteps keeps the newest verdict when a test appears twice', () => {
    // A retry produces exactly this: an earlier verdict still on the phone, and
    // the operator's rerun after it. The later one is the answer.
    const progress = {
        steps: [
            { testId: 'touch', status: 'failed', reportedAt: '2026-09-30T10:00:00.000Z' },
            { testId: 'touch', status: 'passed', reportedAt: '2026-09-30T10:05:00.000Z' }
        ]
    };

    assert.equal(indexSteps(progress).get('touch').status, 'passed');
});

test('indexSteps does not let an older duplicate win', () => {
    // The reverse order is what a replayed offline queue produces: the old result
    // arriving after the retry. Taking it would undo the operator's rerun.
    const progress = {
        steps: [
            { testId: 'touch', status: 'passed', reportedAt: '2026-09-30T10:05:00.000Z' },
            { testId: 'touch', status: 'failed', reportedAt: '2026-09-30T10:00:00.000Z' }
        ]
    };

    assert.equal(indexSteps(progress).get('touch').status, 'passed');
});

test('canResume only offers a run that is genuinely unfinished', () => {
    assert.equal(canResume(snapshot([['touch']])), true);

    // A finished suite is done. Offering to continue it would restart the last
    // step for no reason.
    assert.equal(canResume(snapshot([['touch']], { finished: true })), false);

    // No steps means nothing was ever done, so this is a first run.
    assert.equal(canResume(snapshot([])), false);
    assert.equal(canResume({ started: false, steps: [] }), false);
    assert.equal(canResume(null), false);
});

test('describeResume names the test the phone is on', () => {
    const text = describeResume(snapshot([['touch']], {
        totalTests: 12,
        currentTestName: 'Camera'
    }));

    assert.match(text, /1 van de 12/);
    assert.match(text, /Bezig met Camera/);
});

test('describeResume says nothing when there is nothing to continue', () => {
    assert.equal(describeResume(snapshot([['touch']], { finished: true })), '');
    assert.equal(describeResume(null), '');
});

test('describeResume falls back to the step count when the total is unknown', () => {
    // An older desktop did not send a total. Guessing 12 there would put a
    // number on screen that does not match the list next to it.
    const text = describeResume({
        started: true,
        finished: false,
        totalTests: 0,
        steps: [
            { testId: 'touch', status: 'passed', reportedAt: '2026-09-30T10:00:00.000Z' },
            { testId: 'display', status: 'passed', reportedAt: '2026-09-30T10:01:00.000Z' }
        ]
    });

    assert.match(text, /2 van de 2/);
});
