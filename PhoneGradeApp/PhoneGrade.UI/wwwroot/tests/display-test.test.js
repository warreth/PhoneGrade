import test from 'node:test';
import assert from 'node:assert/strict';

// The display step is the only one that puts a full-screen element on
// document.body rather than inside the test container, so it needs a body that
// can be inspected and cleaned between tests.
function makeBody() {
    const children = [];
    return {
        children,
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

global.window = {
    devicePixelRatio: 2,
    location: { search: '?sessionId=TEST_SUITE_123', protocol: 'http:', host: 'localhost:5055' }
};

global.document = {
    body: makeBody(),
    createElement: (tag) => ({
        tag,
        className: '',
        style: { background: '' },
        children: [],
        set innerHTML(v) { this._html = v; },
        get innerHTML() { return this._html || ''; },
        appendChild(child) { this.children.push(child); return child; },
        querySelector: () => null
    }),
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
import { INSPECTION_COLORS } from '../modules/DisplayInspection.js';

test('a fresh display step has inspected nothing', () => {
    const d = new DisplayTest();

    assert.equal(d.status, 'pending');
    assert.equal(d.inspection.total, 12);
    assert.equal(d.details.patchesInspected, undefined);
    assert.equal(d.overlay, null);
});

test('a new step each time, so one inspection cannot leak into the next', () => {
    // The inspection is state on the test object. If it were shared, a second
    // device in the same session would inherit the first one's defects and be
    // graded faulty for pixels it does not have.
    const a = new DisplayTest();
    const b = new DisplayTest();

    a.inspection.verdicts.set('white:0', 'defect');

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
    d.inspection.verdicts.set('white:4', 'defect');
    d.inspection.verdicts.set('red:4', 'defect');
    d.status = 'failed';

    d.reset();

    // A rerun has to start from an empty inspection. A leftover defect would have
    // the second attempt fail on the first colour before the operator looked at
    // anything.
    assert.equal(d.inspection.verdicts.size, 0);
    assert.equal(d.status, 'pending');
});

test('the colours are the five a stuck subpixel shows on', () => {
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
