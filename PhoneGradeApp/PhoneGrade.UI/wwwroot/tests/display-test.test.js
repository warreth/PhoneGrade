import test from 'node:test';
import assert from 'node:assert/strict';

// The display step is the only one that puts a full-screen element on
// document.body rather than inside the test container, so it needs a body that
// can be inspected and cleaned between tests.
function makeBody() {
    const children = [];
    return {
        children,
        style: {},
        contains: (el) => children.includes(el),
        appendChild: (el) => { children.push(el); return el; },
        removeChild: (el) => {
            const i = children.indexOf(el);
            if (i >= 0) children.splice(i, 1);
            return el;
        }
    };
}

/**
 * A container just real enough to click on.
 *
 * The cards and the colour bar are written as markup and then wired up by id, so
 * the test needs the two halves of that: the html the step produced, and the
 * elements it looks up afterwards. Element ids are read straight out of the
 * markup, which means a test can ask for an id that does not exist and get null
 * rather than a silent pass.
 */
function fakeContainer() {
    const container = {
        html: '',
        listeners: new Map(),

        set innerHTML(v) {
            this.html = v || '';
            // Each render starts from a clean slate. The buttons are recreated
            // every time, so a listener left over from a previous card would be
            // called on an element that is no longer on screen.
            this.listeners.clear();
            for (const match of this.html.matchAll(/id="([^"]+)"/g)) {
                this.listeners.set(match[1], { type: match[1], click: [] });
            }
        },

        get innerHTML() { return this.html; },

        querySelector(sel) {
            if (!sel.startsWith('#')) return null;
            return this.listeners.get(sel.slice(1)) || null;
        }
    };

    // The steps wire up with addEventListener, so each fake element needs one.
    container.querySelector = (function (original) {
        return function (sel) {
            const el = original.call(this, sel);
            if (el && !el.addEventListener) {
                el.addEventListener = (type, handler) => {
                    (el[type] || (el[type] = [])).push(handler);
                };
                el.fire = (type) => {
                    for (const handler of el[type] || []) handler({ stopPropagation: () => {} });
                };
            }
            return el;
        };
    })(container.querySelector);

    return container;
}

/** The same contract as fakeContainer, for the overlay the step makes itself. */
function fakeElement(tag) {
    const el = fakeContainer();
    el.tag = tag;
    el.className = '';
    el.style = {};
    el.children = [];
    el.appendChild = (child) => { el.children.push(child); return child; };
    return el;
}

function fakeClient() {
    const sent = [];
    return {
        sent,
        isConnected: () => true,
        send: (message) => sent.push(message)
    };
}

/** The colour on screen right now, or null when the step has cleared it. */
function currentOverlay() {
    return document.body.children[document.body.children.length - 1] || null;
}

function colorOn(overlay) {
    const match = /<span class="display-color-name">([^<]+)<\/span>/.exec(overlay.innerHTML);
    return match ? match[1] : null;
}

const tick = () => new Promise(resolve => setTimeout(resolve, 0));

/** Runs the step as far as the colour bar and leaves it waiting there. */
async function start(d) {
    const container = fakeContainer();
    const client = fakeClient();
    const running = d.run(client, container);

    container.querySelector('#brightness-ok').fire('click');
    await tick();

    return { container, client, running };
}

global.window = {
    devicePixelRatio: 2,
    location: { search: '?sessionId=TEST_SUITE_123', protocol: 'http:', host: 'localhost:5055' }
};

global.document = {
    body: makeBody(),
    createElement: (tag) => fakeElement(tag),
    addEventListener: () => {},
    removeEventListener: () => {},
    head: { appendChild: () => {} },
    getElementById: () => null
};

Object.defineProperty(global, 'navigator', {
    value: { vibrate: () => true, userAgent: 'Mozilla/5.0 (Linux; Android 17) Chrome/140' },
    writable: true,
    configurable: true
});

import { DisplayTest } from '../modules/DisplayTest.js';
import { INSPECTION_COLORS, colorName } from '../modules/DisplayInspection.js';

test('a fresh display step has judged nothing', () => {
    const d = new DisplayTest();

    assert.equal(d.status, 'pending');
    assert.equal(d.inspection.verdicts.size, 0);
    assert.equal(d.details.defectiveColors, undefined);
    assert.equal(d.overlay, null);
});

test('a new step each time, so one inspection cannot leak into the next', () => {
    // The inspection is state on the test object. If it were shared, a second
    // device in the same session would inherit the first one's fault and be
    // graded faulty for a panel it does not have.
    const a = new DisplayTest();
    const b = new DisplayTest();

    a.inspection.verdicts.set('white', 'defective');

    assert.equal(b.inspection.verdicts.size, 0);
});

test('dispose takes the full-screen overlay off the body', () => {
    const d = new DisplayTest();
    const overlay = document.createElement('div');
    document.body.appendChild(overlay);
    d.overlay = overlay;

    d.dispose();

    // The overlay lives on document.body, not in the test container, so the
    // runner clearing the container does not touch it. A skip or the failsafe in
    // the middle of a colour used to leave a full-screen block of red over the
    // next step with no way past it.
    assert.equal(document.body.contains(overlay), false);
    assert.equal(d.overlay, null);
});

test('dispose is safe with no overlay and when called twice', () => {
    const d = new DisplayTest();

    assert.doesNotThrow(() => d.dispose());
    assert.doesNotThrow(() => d.dispose());
});

test('reset clears the inspection as well as the verdict', () => {
    const d = new DisplayTest();
    d.inspection.verdicts.set('white', 'defective');
    d.inspection.verdicts.set('red', 'defective');
    d.status = 'failed';

    d.reset();

    // A rerun has to start from an empty inspection. A leftover fault would have
    // the second attempt fail on the first colour before the operator looked at
    // anything.
    assert.equal(d.inspection.verdicts.size, 0);
    assert.equal(d.status, 'pending');
});

test('the colours are the five a panel fault shows up under', () => {
    const d = new DisplayTest();

    assert.deepEqual(d.colors.map(c => c.id), INSPECTION_COLORS.map(c => c.id));
    assert.deepEqual(d.colors.map(c => c.id), ['white', 'red', 'green', 'blue', 'black']);
});

test('the brightness card no longer offers to fail the display', () => {
    // A phone at a third brightness is not a phone with a broken display. The old
    // card made "no" a display failure, so a dim phone could be graded faulty for
    // a setting and the owner would be the one paying. The only way on now is
    // "yes", and the way out is the runner's own skip button.
    const d = new DisplayTest();
    const container = fakeContainer();

    d.showBrightnessCheck(container);

    const html = container.html;
    assert.match(html, /brightness-ok/);
    assert.match(html, /brightness-retry/);
    assert.doesNotMatch(html, /brightness-no/, 'there must be no way to fail on brightness');
    assert.doesNotMatch(html, /btn-danger/, 'no danger button on the brightness card');
});

test('the brightness card asks again rather than recording anything', () => {
    // "Nee" used to be terminal. Now the retry button puts the card back up with
    // nothing recorded, so the operator can go and turn the brightness up and come
    // back to the same question instead of starting the whole step again.
    const d = new DisplayTest();
    const container = fakeContainer();

    let settled = false;
    d.showBrightnessCheck(container).then(() => { settled = true; });

    const retry = container.querySelector('#brightness-retry');
    assert.ok(retry, 'there must be a retry button');

    retry.fire('click');
    assert.equal(settled, false, 'retrying must not settle the step');
    assert.ok(container.querySelector('#brightness-ok'), 'the card must be on screen again');

    container.querySelector('#brightness-ok').fire('click');
    return Promise.resolve().then(() => assert.equal(settled, true));
});

test('the brightness card names what is about to be checked', () => {
    // The operator has to be able to leave the screen, fix the brightness and come
    // back knowing what they were asked to do. Five colours on an unspecified
    // "display" step does not tell them how long they are away for.
    const d = new DisplayTest();
    const container = fakeContainer();

    d.showBrightnessCheck(container);

    assert.match(container.html, /helderheid/i);
    assert.match(container.html, /maximaal/);
});

test('every colour fills the screen and offers Goed and Slecht', async () => {
    const d = new DisplayTest();
    const { running } = await start(d);

    for (const color of INSPECTION_COLORS) {
        const overlay = currentOverlay();
        assert.ok(overlay, `${colorName(color)} must be on screen`);
        assert.equal(overlay.style.background, color.hex, 'the whole screen is the colour being judged');
        assert.equal(colorOn(overlay), colorName(color));
        assert.match(overlay.innerHTML, /id="color-ok"[^>]*>Goed</);
        assert.match(overlay.innerHTML, /id="color-defect"[^>]*>Slecht</);
        assert.equal(document.body.children.length, 1, 'the previous colour must be off the screen');

        overlay.querySelector('#color-ok').fire('click');
        await tick();
    }

    await running;

    assert.equal(d.status, 'passed');
    assert.equal(document.body.children.length, 0, 'no colour may be left on the screen');
});

test('Slecht records the colour and every colour is still shown', async () => {
    const d = new DisplayTest();
    const { running } = await start(d);

    const answers = ['#color-ok', '#color-defect', '#color-ok', '#color-ok', '#color-ok'];
    const seen = [];

    for (const id of answers) {
        const overlay = currentOverlay();
        assert.ok(overlay, 'a colour must be on screen');
        seen.push(colorOn(overlay));
        overlay.querySelector(id).fire('click');
        await tick();
    }

    await running;

    assert.deepEqual(seen, ['Wit', 'Rood', 'Groen', 'Blauw', 'Zwart']);
    assert.equal(d.status, 'failed');
    assert.deepEqual(d.details.defectiveColors, ['Rood']);
    assert.equal(d.notes, 'Afwijking gemeld bij: Rood');
    assert.match(JSON.stringify(d.details), /Rood/);
});

test('Vorige goes back without losing the verdict already given', async () => {
    const d = new DisplayTest();
    const { running } = await start(d);

    let overlay = currentOverlay();
    assert.equal(colorOn(overlay), 'Wit');
    assert.equal(overlay.querySelector('#color-prev'), null,
        'the first colour is the start of the run, there is nothing before it');

    overlay.querySelector('#color-defect').fire('click');
    await tick();

    overlay = currentOverlay();
    assert.equal(colorOn(overlay), 'Rood');
    assert.ok(overlay.querySelector('#color-prev'), 'from the second colour on there is a way back');

    overlay.querySelector('#color-prev').fire('click');
    await tick();

    overlay = currentOverlay();
    assert.equal(colorOn(overlay), 'Wit');
    assert.match(overlay.innerHTML, /id="color-defect" class="[^"]*is-active/,
        'the verdict they gave before is still the verdict on screen');

    // Changed their mind while standing in front of it again.
    overlay.querySelector('#color-ok').fire('click');
    await tick();

    // Whatever is left, taken until the screen goes clean.
    while (currentOverlay()) {
        currentOverlay().querySelector('#color-ok').fire('click');
        await tick();
    }

    await running;

    assert.equal(d.status, 'passed');
    assert.deepEqual(d.details.defectiveColors, [],
        'the colour ruled out on the second look must not still be on the report');
});

test('the bar reports each colour to the desktop as it is judged', async () => {
    const d = new DisplayTest();
    const { client, running } = await start(d);

    for (let i = 0; i < INSPECTION_COLORS.length; i++) {
        const id = i === 1 ? '#color-defect' : '#color-ok';
        currentOverlay().querySelector(id).fire('click');
        await tick();
    }

    await running;

    const reported = client.sent.filter(m => m.type === 'test_progress');
    assert.ok(reported.some(m => m.testName === 'Scherm & dode pixels (Rood)'));
    assert.ok(reported.some(m => m.message === 'Rood: afwijking gemeld'));
    assert.ok(reported.some(m => m.message === 'Wit: goed'),
        'a colour judged Goed is reported the same way, or the bar would stall');
    assert.equal(reported[reported.length - 1].progress, 100);
});
