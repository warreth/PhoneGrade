import test from 'node:test';
import assert from 'node:assert/strict';

/**
 * A fake DOM big enough for one step: elements hold text, classes and click
 * handlers, the container collects what was appended, and the document and
 * window record the listeners the step adds so a test can drive a visibility
 * cycle the way the browser would.
 */
function fakeElement(tag) {
    return {
        tag,
        className: '',
        textContent: '',
        innerHTML: '',
        type: '',
        children: [],
        handlers: {},
        appendChild(child) { this.children.push(child); return child; },
        addEventListener(name, fn) { this.handlers[name] = fn; },
        click() { if (this.handlers.click) this.handlers.click(); },
    };
}

function fakeContainer() {
    return {
        html: '',
        children: [],
        set innerHTML(v) { this.html = v || ''; this.children = []; },
        get innerHTML() { return this.html; },
        appendChild(child) { this.children.push(child); return child; },
    };
}

function allElements(root) {
    const out = [];
    const walk = (el) => {
        out.push(el);
        for (const child of el.children || []) walk(child);
    };
    for (const child of root.children) walk(child);
    return out;
}

function buttonWithText(root, text) {
    return allElements(root).find(el => el.tag === 'button' && el.textContent === text) || null;
}

const documentListeners = {};
const windowListeners = {};

global.document = {
    visibilityState: 'visible',
    body: { style: {} },
    createElement: (tag) => fakeElement(tag),
    addEventListener: (name, fn) => { documentListeners[name] = fn; },
    removeEventListener: (name) => { delete documentListeners[name]; },
    head: { appendChild: () => {} },
    getElementById: () => null,
};

global.window = {
    isSecureContext: true,
    devicePixelRatio: 1,
    location: { search: '', protocol: 'http:', host: 'localhost:5055' },
    addEventListener: (name, fn) => { windowListeners[name] = fn; },
    removeEventListener: (name) => { delete windowListeners[name]; },
};

Object.defineProperty(global, 'navigator', {
    value: { userAgent: 'Mozilla/5.0 (Linux; Android 17) Chrome/140', languages: ['en'] },
    writable: true,
    configurable: true,
});

function flush() {
    return new Promise((resolve) => setImmediate(resolve));
}

function goHidden() {
    document.visibilityState = 'hidden';
    documentListeners.visibilitychange();
}

function goVisible() {
    document.visibilityState = 'visible';
    documentListeners.visibilitychange();
}

function fakeClient() {
    const sent = [];
    return { sent, isConnected: () => true, send: (m) => sent.push(m) };
}

function resetEnvironment() {
    global.window.isSecureContext = true;
    global.window.PublicKeyCredential = undefined;
    document.visibilityState = 'visible';
    for (const name of Object.keys(documentListeners)) delete documentListeners[name];
    for (const name of Object.keys(windowListeners)) delete windowListeners[name];
}

import { PowerLockTest, VisibilityCycle, POWER_WAIT_MS, POWER_ROW, BIOMETRIC_ROW, powerStatus, biometricStatus } from '../modules/PowerLockTest.js';
import { setLocale } from '../modules/i18n.js';

// English labels throughout, so the assertions read what the keys say rather
// than what Dutch happens to make of them.
setLocale('en');

test('the cycle needs a hidden before a visible means anything', () => {
    const cycle = new VisibilityCycle();
    assert.equal(cycle.detected, false);

    cycle.visible();
    assert.equal(cycle.detected, false, 'a stray visible without a hidden is not a cycle');

    cycle.hidden();
    assert.equal(cycle.detected, false, 'hidden alone is half a cycle');
    cycle.visible();
    assert.equal(cycle.detected, true);
});

test('a second hidden does not restart the cycle', () => {
    const cycle = new VisibilityCycle();
    cycle.hidden(1000);
    cycle.hidden(2000);
    cycle.visible(5000);
    assert.equal(cycle.hiddenMs, 4000, 'the first hidden is the one that counts');
});

test('the verdicts read the answers plainly', () => {
    assert.equal(powerStatus('yes'), 'passed');
    assert.equal(powerStatus('no'), 'failed');
    assert.equal(biometricStatus('worked'), 'passed');
    assert.equal(biometricStatus('failed'), 'failed');
    assert.equal(biometricStatus('code'), 'skipped', 'a code is not a defect');
    assert.equal(biometricStatus(null), 'skipped');
});

test('a screen that goes away and comes back is the power button answering', async () => {
    resetEnvironment();
    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();

    goHidden();
    goVisible();
    await running;

    assert.equal(step.powerAnswer, 'yes');
    assert.equal(step.cycleDetected, true);
    assert.equal(step.status, 'passed');

    const power = step.toResults().find(r => r.id === POWER_ROW);
    assert.equal(power.status, 'passed');
    assert.equal(power.labelCode, 'PWR');
    assert.match(power.notes, /detected by the page/);
});

test('a page that never sees the cycle asks the operator instead', async (t) => {
    resetEnvironment();
    t.mock.timers.enable({ apis: ['setTimeout'] });

    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();

    assert.equal(buttonWithText(container, 'No, it did not'), null, 'no question while the wait is running');

    t.mock.timers.tick(POWER_WAIT_MS);
    await flush();

    const no = buttonWithText(container, 'No, it did not');
    assert.ok(no, 'the fallback question appears when the wait runs out');
    assert.ok(buttonWithText(container, 'Yes, it did'), 'and the confirming answer beside it');
    no.click();
    await running;

    assert.equal(step.powerAnswer, 'no');
    assert.equal(step.status, 'failed', 'an operator pressing a dead button is a finding');

    const power = step.toResults().find(r => r.id === POWER_ROW);
    assert.equal(power.status, 'failed');
});

test('with an authenticator, the unlock question follows the cycle', async () => {
    resetEnvironment();
    global.window.PublicKeyCredential = {
        isUserVerifyingPlatformAuthenticatorAvailable: async () => true,
    };

    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();
    await flush();

    goHidden();
    goVisible();
    await flush();

    const worked = buttonWithText(container, 'It unlocked with face or finger');
    assert.ok(worked, 'the unlock question is on screen');
    worked.click();
    await running;

    assert.equal(step.biometricAnswer, 'worked');
    assert.equal(step.status, 'passed');

    const rows = step.toResults();
    assert.deepEqual(step.resultIds(), rows.map(r => r.id), 'the resume contract lists the same rows');
    assert.equal(rows.find(r => r.id === BIOMETRIC_ROW).status, 'passed');
    assert.equal(rows.find(r => r.id === BIOMETRIC_ROW).labelCode, 'BIOM');
});

test('a code answer is unknown rather than a defect', async () => {
    resetEnvironment();
    global.window.PublicKeyCredential = {
        isUserVerifyingPlatformAuthenticatorAvailable: async () => true,
    };

    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();
    await flush();

    goHidden();
    goVisible();
    await flush();

    buttonWithText(container, 'It asked for a code or pattern').click();
    await running;

    assert.equal(step.status, 'passed', 'an empty enrolment is not a fault');
    const bio = step.toResults().find(r => r.id === BIOMETRIC_ROW);
    assert.equal(bio.status, 'skipped');
    assert.match(bio.notes, /not a finding/);
});

test('no authenticator means unknown with the reason', async () => {
    resetEnvironment();
    global.window.PublicKeyCredential = {
        isUserVerifyingPlatformAuthenticatorAvailable: async () => false,
    };

    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();
    await flush();

    goHidden();
    goVisible();
    await running;

    assert.equal(step.status, 'passed');
    const bio = step.toResults().find(r => r.id === BIOMETRIC_ROW);
    assert.equal(bio.status, 'skipped');
    assert.match(bio.notes, /no platform authenticator/);
});

test('an insecure page keeps the power check and explains the missing one', async () => {
    resetEnvironment();
    global.window.isSecureContext = false;

    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();
    await flush();

    goHidden();
    goVisible();
    await running;

    assert.equal(step.powerAnswer, 'yes', 'the visibility cycle works on any origin');
    const bio = step.toResults().find(r => r.id === BIOMETRIC_ROW);
    assert.equal(bio.status, 'skipped');
    assert.match(bio.notes, /does not trust/);

    const fixes = allElements(container).filter(el => el.tag === 'li');
    assert.equal(fixes.length, 2, 'the two settings that fix it are named');
});

test('the failsafe covers the wait and the two questions', () => {
    assert.equal(new PowerLockTest().getFailsafeMs(), POWER_WAIT_MS + 90000);
});

test('an interrupted run leaves both rows skipped, not failed', () => {
    resetEnvironment();
    const step = new PowerLockTest();
    step.start();

    const rows = step.toResults();
    assert.equal(rows.length, 2);
    for (const row of rows) {
        assert.equal(row.status, 'skipped', `${row.id} was never answered`);
        assert.equal(row.notes, 'Not answered');
    }
    assert.deepEqual(step.resultIds(), [POWER_ROW, BIOMETRIC_ROW]);
});

test('reset clears the answers so a rerun starts empty', async () => {
    resetEnvironment();
    const step = new PowerLockTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    await flush();

    step.reset();
    assert.equal(step.powerAnswer, null);
    assert.equal(step.biometricAnswer, null);
    assert.equal(step.status, 'pending');

    goHidden();
    goVisible();
    await running;
});
