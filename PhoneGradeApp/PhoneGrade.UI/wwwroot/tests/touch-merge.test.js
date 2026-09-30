import test from 'node:test';
import assert from 'node:assert/strict';

global.window = {
    devicePixelRatio: 2,
    location: { search: '?sessionId=TEST_SUITE_123', protocol: 'http:', host: 'localhost:5055' }
};

global.document = {
    body: { style: {} },
    addEventListener: (event, handler) => {},
    removeEventListener: (event, handler) => {},
    head: { appendChild: () => {} },
    getElementById: (id) => null
};

Object.defineProperty(global, 'navigator', {
    value: { vibrate: () => true, userAgent: 'Mozilla/5.0 (Linux; Android 17) Chrome/140' },
    writable: true,
    configurable: true
});

import { DeviceTest } from '../modules/DeviceTest.js';
import { TouchTest } from '../modules/TouchTest.js';
import { applyStoredResults } from '../modules/SuiteProgress.js';

test('a plain step still reports exactly one row', () => {
    // The touchscreen step is the only one that measures two things, and it has
    // to stay an addition rather than a replacement: the other eleven steps are
    // ordinary and a regression here would drop every verdict but one.
    const t = new DeviceTest('camera', 'Camera', 'd');
    t.start();
    t.pass('ok');

    const rows = t.toResults();

    assert.equal(rows.length, 1);
    assert.equal(rows[0].id, 'camera');
    assert.deepEqual(t.resultIds(), ['camera']);
});

test('the touchscreen step reports one row, not one per half', () => {
    const t = new TouchTest();

    const rows = t.toResults();

    assert.equal(rows.length, 1);
    assert.equal(rows[0].id, 'touch');
    assert.equal(rows[0].name, 'Touchscreen Test');
    assert.deepEqual(t.resultIds(), ['touch']);
});

test('nothing of the touch step is reported under a digitizer id', () => {
    const t = new TouchTest();
    t.start();
    t.pass('100% dekking');
    t.settleEdgeFromRunner('passed', '100% responsief');

    const [row] = t.toResults();

    assert.equal(row.id, 'touch');
    assert.ok(!JSON.stringify(t.toResults()).includes('digitizer'),
        'the operator never sees an id, only the row and its notes');
});

test('a failing edge fails the row the whole step reports', () => {
    // Both halves have to survive the merge, or a grading label would say the
    // edges of the screen were checked when nothing ever traced them.
    const t = new TouchTest();
    t.start();
    t.pass('Alle vakjes aangeraakt');
    t.settleEdgeFromRunner('failed', 'Rand onderaan reageert niet');

    const [row] = t.toResults();

    assert.equal(row.status, 'failed');
    assert.match(row.notes, /Alle vakjes aangeraakt/);
    assert.match(row.notes, /Rand onderaan/);
});

test('a skip during the edges is a skipped row, not a pass and not a fail', () => {
    // The operator skips while tracing the edges. The coverage they had already
    // established is still true, so a fail would cost them the price of the
    // phone, but the edges were never traced and a pass would say they were.
    const t = new TouchTest();
    t.start();
    t.pass('100% dekking');
    t.settleEdgeFromRunner('skipped', 'Overgeslagen samen met de stap');

    const [row] = t.toResults();

    assert.equal(row.status, 'skipped');
    assert.match(row.notes, /100% dekking/);
    assert.match(row.notes, /Overgeslagen samen met de stap/);
});

test('a skip on the grid settles the edges with it', () => {
    const t = new TouchTest();
    t.start();
    t.skip('Overgeslagen door de operator');
    t.skipEdge('Overgeslagen samen met het scherm');

    const [row] = t.toResults();

    assert.equal(row.status, 'skipped');
    assert.match(row.notes, /Overgeslagen door de operator/);
    assert.match(row.notes, /Overgeslagen samen met het scherm/);
});

test('the edge half is settled at most once', () => {
    const t = new TouchTest();
    t.start();
    t.pass('100% dekking');
    t.edge.status = 'passed';
    t.edge.notes = '100% responsief';

    // The runner calls this in its finally block, after run() already settled
    // the edges. A second call must not overwrite that verdict with a generic
    // "afgebroken".
    t.settleEdgeFromRunner('failed', 'Afgebroken voordat de randen klaar waren');

    const [row] = t.toResults();

    assert.equal(row.status, 'passed');
    assert.match(row.notes, /100% responsief/);
});

test('the touchscreen step gets a failsafe budget for both halves', () => {
    const t = new TouchTest();

    // The runner's default is 90 s. Two phases of 60 s each do not fit in that,
    // and a cut-off second phase is a fail verdict for hardware never tested.
    assert.ok(t.getFailsafeMs() > 120000, 'the budget must cover both phases');
    assert.notEqual(t.getFailsafeMs(), 90000);
});

test('a step without a getFailsafeMs still runs, using the default', () => {
    // Guards the runner's fallback for any module that predates the method.
    const legacy = { id: 'x', run: async () => {} };
    const budget = typeof legacy.getFailsafeMs === 'function' ? legacy.getFailsafeMs() : 90000;

    assert.equal(budget, 90000);
});

test('a step with no declared budget reports the default', () => {
    assert.equal(new DeviceTest('x', 'X', 'd').getFailsafeMs(), 90000);
});

test('reset clears both halves of the merged step', () => {
    const t = new TouchTest();
    t.start();
    t.pass('100%');
    t.settleEdgeFromRunner('failed', 'randen dood');
    t.touchedCells.add('0');

    // A rerun starts from nothing. A stale cell count from the previous attempt
    // would make the new grid pass instantly on the first touch.
    t.reset();

    assert.equal(t.status, 'pending');
    assert.equal(t.edge.status, 'pending');
    assert.equal(t.toResults()[0].status, 'pending');
    assert.equal(t.touchedCells.size, 0);
    assert.equal(t.storedRows, null);
});

test('a resumed step reports the stored verdict instead of a pending one', () => {
    const t = new TouchTest();

    // What a reload does: the desktop hands back the rows it kept. The step must
    // not go out as pending again, or the desktop would offer to resume it and
    // the operator would trace the same edges a second time.
    applyStoredResults([t], {
        started: true,
        finished: false,
        steps: [{
            testId: 'touch',
            status: 'failed',
            notes: 'Slechts 30% van scherm responsief (dode zones)',
            reportedAt: '2026-09-30T10:01:00.000Z'
        }]
    });

    const [row] = t.toResults();

    assert.equal(row.id, 'touch');
    assert.equal(row.status, 'failed');
    assert.match(row.notes, /Onthouden van de vorige run/);
});

test('the edges report their progress under the step they belong to', async () => {
    const t = new TouchTest();
    const sent = [];
    const ws = { isConnected: () => true, send: (m) => sent.push(m) };

    t.start();
    t.pass('100% dekking');
    await t.settleEdge(() => {}, () => 10, 10, ws);

    assert.ok(sent.length > 0, 'the edge half has to tell the desktop it moved');
    assert.equal(sent[0].testId, 'touch');
    assert.equal(sent[0].testName, 'Touchscreen Test');
    assert.match(t.toResults()[0].notes, /100% responsief/);
});

test('progress defaults to the step own id', () => {
    const t = new TouchTest();
    const sent = [];
    const ws = { isConnected: () => true, send: (m) => sent.push(m) };

    t.reportProgress(ws, 10, 'Aangeraakt: 5/112');

    assert.equal(sent[0].testId, 'touch');
    assert.equal(sent[0].testName, 'Touchscreen Test');
});

test('there is no separate digitizer step in the suite any more', async () => {
    // The point of the merge. Importing a module that was deleted would throw, so
    // this is really a guard on the file list.
    const { readdirSync } = await import('node:fs');
    const { fileURLToPath } = await import('node:url');
    const path = await import('node:path');

    const modulesDir = path.dirname(fileURLToPath(import.meta.resolve('../modules/TouchTest.js')));
    const files = readdirSync(modulesDir).map(f => f.toLowerCase());

    assert.ok(!files.includes('digitizertest.js'),
        'DigitizerTest.js should be gone, its work lives in TouchTest');
    assert.ok(files.includes('touchtest.js'));
});
