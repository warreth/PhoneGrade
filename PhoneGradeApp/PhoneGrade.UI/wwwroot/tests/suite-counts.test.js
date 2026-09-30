import test from 'node:test';
import assert from 'node:assert/strict';
import { countResults } from '../modules/SuiteCounts.js';

test('a skipped step is counted as skipped and not folded into a pass', () => {
    const counts = countResults([
        { id: 'touch', status: 'passed' },
        { id: 'display', status: 'passed' },
        { id: 'gps', status: 'skipped' },
        { id: 'camera', status: 'failed' }
    ]);

    assert.deepEqual(counts, { total: 4, passed: 2, failed: 1, skipped: 1 });
});

test('a run with no failures but a skipped step does not read as a clean sweep', () => {
    const counts = countResults([
        { id: 'touch', status: 'passed' },
        { id: 'display', status: 'passed' },
        { id: 'gps', status: 'skipped' }
    ]);

    assert.equal(counts.failed, 0);
    assert.equal(counts.skipped, 1);
    assert.notEqual(counts.passed, counts.total);
});

test('the total is the number of rows, whatever state they ended in', () => {
    const counts = countResults([
        { id: 'never-settled', status: 'pending' },
        { id: 'still-going', status: 'running' },
        { id: 'touch', status: 'passed' }
    ]);

    assert.deepEqual(counts, { total: 3, passed: 1, failed: 0, skipped: 0 });
});

test('a missing suite counts as nothing instead of throwing', () => {
    const empty = { total: 0, passed: 0, failed: 0, skipped: 0 };

    assert.deepEqual(countResults([]), empty);
    assert.deepEqual(countResults(undefined), empty);
    assert.deepEqual(countResults(null), empty);
    assert.deepEqual(countResults('not a suite'), empty);
});

test('a row without a status is left to the total rather than guessed at', () => {
    const counts = countResults([{ id: 'odd' }, { status: 'passed' }]);

    assert.deepEqual(counts, { total: 2, passed: 1, failed: 0, skipped: 0 });
});
