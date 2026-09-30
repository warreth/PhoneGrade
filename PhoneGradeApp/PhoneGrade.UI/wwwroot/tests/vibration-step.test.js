import test from 'node:test';
import assert from 'node:assert/strict';

/**
 * A container that keeps whatever was clicked.
 *
 * This step wires its buttons with onclick rather than addEventListener, so the
 * fake only has to hold the elements the markup named and hand them back with
 * their onclick slot open.
 */
function fakeContainer() {
    return {
        html: '',
        els: new Map(),

        set innerHTML(v) {
            this.html = v || '';
            this.els.clear();
            for (const match of this.html.matchAll(/id="([^"]+)"/g)) {
                this.els.set(match[1], { id: match[1], className: '', textContent: '' });
            }
        },

        get innerHTML() { return this.html; },

        querySelector(sel) {
            if (!sel.startsWith('#')) return null;
            return this.els.get(sel.slice(1)) || null;
        }
    };
}

/** Puts a navigator on the process with whatever the browser under test does. */
function setNavigator({ vibrate, userAgent = 'Mozilla/5.0 (Linux; Android 17) Chrome/140' } = {}) {
    Object.defineProperty(global, 'navigator', {
        value: { vibrate, userAgent },
        writable: true,
        configurable: true
    });
}

global.window = { devicePixelRatio: 1, location: { search: '', protocol: 'http:', host: 'localhost:5055' } };
global.document = {
    body: { style: {} },
    createElement: () => ({ style: {}, appendChild: () => {} }),
    addEventListener: () => {},
    removeEventListener: () => {},
    head: { appendChild: () => {} },
    getElementById: () => null
};

function fakeClient() {
    const sent = [];
    return { sent, isConnected: () => true, send: (m) => sent.push(m) };
}

import { VibrationTest } from '../modules/VibrationTest.js';

test('a browser with the vibration API gets a button and a line to answer on', async () => {
    const calls = [];
    setNavigator({ vibrate: (pattern) => { calls.push(pattern); return true; } });

    const t = new VibrationTest();
    const container = fakeContainer();
    const running = t.run(fakeClient(), container);

    assert.ok(container.querySelector('#btn-vibe-pulse'), 'the pulse button is the whole point of the API');
    assert.match(container.html, /Nog geen signaal verstuurd\./,
        'nothing has been asked of the browser yet');
    assert.equal(t.details.browserApi, 'navigator.vibrate');

    container.querySelector('#btn-vibe-yes').onclick();
    await running;
});

test('the pattern the browser is given is something it can act on', async () => {
    const calls = [];
    setNavigator({ vibrate: (pattern) => { calls.push(pattern); return true; } });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    container.querySelector('#btn-vibe-pulse').onclick();

    assert.equal(calls.length, 1, 'one press, one request');
    assert.ok(Array.isArray(calls[0]), 'the API takes a pattern, not a bare number');
    assert.ok(calls[0].length > 1, 'an empty pattern tells the browser to stop, not to buzz');
    assert.ok(calls[0].every(step => typeof step === 'number' && step > 0));
});

test('an accepted request is written on the card and kept on the row', async () => {
    setNavigator({ vibrate: () => true });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    container.querySelector('#btn-vibe-pulse').onclick();

    const echo = container.querySelector('#vibe-echo');
    assert.equal(t.details.browserAccepted, true);
    assert.equal(t.details.vibrationPulsePressed, true);
    assert.match(echo.className, /is-accepted/);
    assert.match(echo.textContent, /geaccepteerd/);
    assert.equal(t.status, 'running',
        'the browser answering is not the verdict; the operator still has not');
});

test('an accepted request is not written as a vibration that already happened', async () => {
    // The boolean answers true for a well formed pattern even when the device
    // has nothing to buzz with, and silent mode or do not disturb stops a real
    // motor just the same. The card used to promise the vibration was already
    // under way, which is the one thing the browser cannot know.
    setNavigator({ vibrate: () => true });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    container.querySelector('#btn-vibe-pulse').onclick();

    const echo = container.querySelector('#vibe-echo');
    assert.match(echo.textContent, /geaccepteerd/);
    assert.doesNotMatch(echo.textContent, /voelbaar moeten zijn/);
    assert.match(echo.textContent, /stille modus/);
    assert.match(echo.textContent, /Niet storen/);
    assert.equal(t.details.browserAccepted, true,
        'the answer the browser gave itself is still recorded unchanged');
});

test('the card says what to check before the operator answers no', () => {
    // A phone on silent or on do not disturb does not buzz for a page either,
    // and "no vibration" was being read as a dead motor while it was a setting.
    setNavigator({ vibrate: () => true });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    assert.match(container.html, /stille modus of Niet storen uit/);
    assert.match(container.html, /druk nog eens op de knop/);
});

test('a refused request is told apart from a dead motor', async () => {
    // The whole reason the boolean is read. Before this the call was fire and
    // forget: the operator pressed it, nothing happened, and "no response" was
    // evidence about the motor when it was evidence about the browser.
    setNavigator({ vibrate: () => false });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    container.querySelector('#btn-vibe-pulse').onclick();

    const echo = container.querySelector('#vibe-echo');
    assert.equal(t.details.browserAccepted, false);
    assert.match(echo.className, /is-refused/);
    assert.match(echo.textContent, /weigerde/);
    assert.equal(t.status, 'running',
        'a browser that will not vibrate does not fail the phone it is running on');
});

test('a browser that throws about vibrating is reported, not swallowed', async () => {
    setNavigator({
        vibrate: () => { throw new DOMException('not allowed', 'NotAllowedError'); }
    });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    assert.doesNotThrow(() => container.querySelector('#btn-vibe-pulse').onclick());

    assert.equal(t.details.browserAccepted, false);
    assert.equal(t.details.vibrationError, 'NotAllowedError');
    assert.match(container.querySelector('#vibe-echo').textContent, /weigerde/);
});

test('a browser with no vibration API is handed over as a manual check', () => {
    // iOS Safari and Firefox on Android both ship without navigator.vibrate.
    // Saying so is what stops a step the page cannot drive from being graded as
    // hardware it never touched.
    setNavigator({ vibrate: undefined });

    const t = new VibrationTest();
    const container = fakeContainer();
    t.run(fakeClient(), container);

    assert.equal(container.querySelector('#btn-vibe-pulse'), null,
        'a button that would do nothing is a button that lies');
    assert.equal(container.querySelector('#vibe-echo'), null);
    assert.equal(t.details.browserApi, 'handmatig');
    assert.match(container.html, /Handmatige controle/);
    assert.match(container.html, /stille modus schakelaar/);
    assert.match(container.html, /Deze browser kan de trilmotor niet aansturen/);
});

test('Ja passes and Nee fails, whichever way the browser answered', async () => {
    setNavigator({ vibrate: () => true });

    const yes = new VibrationTest();
    const yesContainer = fakeContainer();
    const yesRun = yes.run(fakeClient(), yesContainer);
    yesContainer.querySelector('#btn-vibe-yes').onclick();
    await yesRun;

    assert.equal(yes.status, 'passed');
    assert.match(yes.notes, /Trilmotor/);

    setNavigator({ vibrate: () => false });

    const no = new VibrationTest();
    const noContainer = fakeContainer();
    const noRun = no.run(fakeClient(), noContainer);
    // The browser refused, and the operator still says the motor is fine.
    noContainer.querySelector('#btn-vibe-pulse').onclick();
    noContainer.querySelector('#btn-vibe-yes').onclick();
    await noRun;

    assert.equal(no.status, 'passed');
    assert.equal(no.details.browserAccepted, false,
        'the refusal stays on the row even when the step passes on the operator\'s word');
});

test('the row reports both questions when it is settled', async () => {
    setNavigator({ vibrate: () => true });

    const t = new VibrationTest();
    const container = fakeContainer();
    const client = fakeClient();
    const running = t.run(client, container);

    container.querySelector('#btn-vibe-pulse').onclick();
    container.querySelector('#btn-vibe-no').onclick();
    await running;

    assert.equal(t.status, 'failed');
    assert.deepEqual(
        { api: t.details.browserApi, accepted: t.details.browserAccepted, pressed: t.details.vibrationPulsePressed },
        { api: 'navigator.vibrate', accepted: true, pressed: true });

    assert.ok(client.sent.some(m => m.type === 'test_progress' && m.message === 'Trillsignaal geaccepteerd door de browser'));
    assert.ok(client.sent.some(m => m.type === 'test_progress' && m.message === 'Trilmotor defect'));
});
