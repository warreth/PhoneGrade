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
    // The ordinary case. Every step but the touchscreen measures one thing, and
    // the row plumbing they all share is what would break first if the
    // touchscreen's own reporting ever spread beyond it.
    const t = new DeviceTest('camera', 'Camera', 'd');
    t.start();
    t.pass('ok');

    const rows = t.toResults();

    assert.equal(rows.length, 1);
    assert.equal(rows[0].id, 'camera');
    assert.deepEqual(t.resultIds(), ['camera']);
});

test('the touchscreen step reports one row', () => {
    const t = new TouchTest();

    const rows = t.toResults();

    assert.equal(rows.length, 1);
    assert.equal(rows[0].id, 'touch');
    assert.equal(rows[0].name, 'Touchscreen Test');
    assert.deepEqual(t.resultIds(), ['touch']);
});

test('nothing of the touch step is reported under a digitizer id', () => {
    // The digitizer was a step of its own, then a second row under this step.
    // Neither may come back as an id the operator never chose to see, so the
    // row is read once the step has settled.
    const t = new TouchTest();
    t.start();
    t.pass('100% dekking');

    const [row] = t.toResults();

    assert.equal(row.id, 'touch');
    assert.ok(!JSON.stringify(t.toResults()).includes('digitizer'),
        'the operator never sees an id, only the row and its notes');
});

test('the touchscreen asks for no budget of its own', () => {
    // One round of colouring, so the runner's default is the right figure. A
    // budget of its own is the fingerprint of a second round: the two rounds
    // used to need 135 s between them, and the runner would happily pay that
    // again the moment somebody put the border back.
    const touch = new TouchTest();
    const ordinary = new DeviceTest('x', 'X', 'd');

    assert.equal(touch.getFailsafeMs(), ordinary.getFailsafeMs());
});

test('the border round is gone rather than merely left unrun', async () => {
    // The round asked the operator to colour a 40 pixel band along the border,
    // most of which the outer cells had already put them over, which is what
    // made the digitizer read as a test of its own. Nothing at runtime reports
    // whether run() stopped after the grid, so the method that used to follow it
    // is watched at the source, the same way the deleted module is watched
    // below.
    const { readFileSync } = await import('node:fs');
    const { fileURLToPath } = await import('node:url');

    const source = readFileSync(fileURLToPath(import.meta.resolve('../modules/TouchTest.js')), 'utf8');

    assert.ok(!source.includes('runEdgePhase'),
        'run() must settle after the grid instead of opening a second round');
    assert.ok(!source.includes('settleEdgeFromRunner'),
        'a border verdict would put a second half back into the row');
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

test('reset clears the step', () => {
    const t = new TouchTest();
    t.start();
    t.pass('100%');
    t.touchedCells.add('0');

    // A rerun starts from nothing. A stale cell count from the previous attempt
    // would make the new grid pass instantly on the first touch.
    t.reset();

    assert.equal(t.status, 'pending');
    assert.equal(t.toResults()[0].status, 'pending');
    assert.equal(t.touchedCells.size, 0);
    assert.equal(t.storedRows, null);
});

test('a resumed step reports the stored verdict instead of a pending one', () => {
    const t = new TouchTest();

    // What a reload does: the desktop hands back the rows it kept. The step must
    // not go out as pending again, or the desktop would offer to resume it and
    // the operator would colour the same boxes a second time.
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

test('progress defaults to the step own id', () => {
    const t = new TouchTest();
    const sent = [];
    const ws = { isConnected: () => true, send: (m) => sent.push(m) };

    t.reportProgress(ws, 10, 'Aangeraakt: 5/112');

    assert.equal(sent[0].testId, 'touch');
    assert.equal(sent[0].testName, 'Touchscreen Test');
});

test('there is no separate digitizer step in the suite any more', async () => {
    // Why the row above is only ever one. Importing a module that was deleted
    // would throw, so this is really a guard on the file list.
    const { readdirSync } = await import('node:fs');
    const { fileURLToPath } = await import('node:url');
    const path = await import('node:path');

    const modulesDir = path.dirname(fileURLToPath(import.meta.resolve('../modules/TouchTest.js')));
    const files = readdirSync(modulesDir).map(f => f.toLowerCase());

    assert.ok(!files.includes('digitizertest.js'),
        'DigitizerTest.js should be gone');
    assert.ok(files.includes('touchtest.js'));
});
