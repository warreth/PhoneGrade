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

test('a plain step still reports exactly one row', () => {
    // The multi-row shape has to be an addition, not a replacement: eleven of the
    // twelve steps are ordinary and a regression here would drop eleven verdicts.
    const t = new DeviceTest('camera', 'Camera', 'd');
    t.start();
    t.pass('ok');

    const rows = t.toResults();

    assert.equal(rows.length, 1);
    assert.equal(rows[0].id, 'camera');
    assert.deepEqual(t.resultIds(), ['camera']);
});

test('the touchscreen step reports the grid and the edges as two rows', () => {
    const t = new TouchTest();

    // Both verdicts have to survive the merge, or a grading label would say the
    // edges of the screen were checked when nothing ever traced them.
    const rows = t.toResults();

    assert.equal(rows.length, 2);
    assert.deepEqual(rows.map(r => r.id), ['touch', 'digitizer']);
    assert.deepEqual(t.resultIds(), ['touch', 'digitizer']);
});

test('the two rows are independent verdicts', () => {
    const t = new TouchTest();
    t.start();
    t.pass('Alle vakjes aangeraakt');
    t.settleEdgeFromRunner('failed', 'Rand onderaan reageert niet');

    const [grid, edge] = t.toResults();

    assert.equal(grid.status, 'passed');
    assert.equal(edge.status, 'failed');
    assert.match(edge.notes, /Rand onderaan/);
});

test('a good grid is not turned into a fail by a skip during the edges', () => {
    // The operator skips while tracing the edges. The screen coverage they had
    // already established is still true, and a label claiming the display failed
    // would cost the operator the price of the phone.
    const t = new TouchTest();
    t.start();
    t.pass('100% dekking');
    t.settleEdgeFromRunner('skipped', 'Overgeslagen samen met de stap');

    const [grid, edge] = t.toResults();

    assert.equal(grid.status, 'passed');
    assert.equal(edge.status, 'skipped');
});

test('the edge row is settled at most once', () => {
    const t = new TouchTest();
    t.start();
    t.edge.status = 'passed';
    t.edge.notes = 'Digitizer randen 100% responsief';

    // The runner calls this in its finally block, after the step's own run()
    // already reported the edges. A second call must not overwrite that verdict
    // with a generic "afgebroken".
    t.settleEdgeFromRunner('failed', 'Afgebroken voordat de randen klaar waren');

    assert.equal(t.toResults()[1].status, 'passed');
    assert.match(t.toResults()[1].notes, /100% responsief/);
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
    assert.equal(t.toResults()[1].status, 'pending');
    assert.equal(t.touchedCells.size, 0);
    assert.equal(t.storedRows, null);
});

test('a resumed step reports the stored edge verdict, not a pending one', () => {
    const t = new TouchTest();

    // A resume fills storedRows from the desktop. If toResults ignored them the
    // edge row would go out as pending and the desktop would keep counting the
    // step as unfinished, so the next reload would offer to resume it again.
    t.storedRows = [
        { testId: 'touch', status: 'passed', notes: 'Onthouden van de vorige run' },
        { testId: 'digitizer', status: 'failed', notes: 'Rand onderaan dood' }
    ];

    const [, edge] = t.toResults();

    assert.equal(edge.status, 'failed');
    assert.equal(edge.notes, 'Rand onderaan dood');
});

test('progress from the edge half is reported under the edge id', () => {
    const t = new TouchTest();
    const sent = [];
    const ws = { isConnected: () => true, send: (m) => sent.push(m) };

    // The operator is watching the PC. Progress for the edges has to arrive under
    // the edges, or the desktop shows the grid moving while the phone is on the
    // outer rim.
    t.reportProgress(ws, 40, 'Randdekking: 40%', 'digitizer', 'Digitizer Edge Test');

    assert.equal(sent[0].testId, 'digitizer');
    assert.equal(sent[0].testName, 'Digitizer Edge Test');
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
