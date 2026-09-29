import test from 'node:test';
import assert from 'node:assert/strict';

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

global.window = {
    devicePixelRatio: 2,
    location: { search: '?sessionId=TEST_SUITE_123', protocol: 'http:', host: 'localhost:5055' }
};

global.document = {
    body: makeBody(),
    createElement: (tag) => ({
        tag, className: '', style: {},
        set innerHTML(v) { this._html = v; }, get innerHTML() { return this._html || ''; },
        appendChild(c) { return c; }, querySelector: () => null
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

/**
 * A container just real enough to click on.
 *
 * The step writes its markup and then wires the buttons up by id, so a test has to
 * be able to read the ids back out of what was written and press them. Ids that
 * are not in the markup come back null rather than a silent pass, which is the
 * point: a button that has been renamed is a test failure, not a test that stops
 * testing anything.
 */
function fakeContainer() {
    const container = {
        html: '',
        nodes: new Map(),

        set innerHTML(v) {
            this.html = v || '';
            this.nodes.clear();

            // Each element is read out of the markup with the attributes it was
            // written with, not with sensible defaults. A step that renders a
            // section already locked, or with its verdict buttons already hidden,
            // has to be seen as it actually arrives, because that initial state is
            // part of what the operator sees before they touch anything.
            for (const match of this.html.matchAll(/<(\w+)([^>]*?)\bid="([^"]+)"([^>]*)>/g)) {
                const [, tag, before, id, after] = match;
                const attrs = `${before} ${after}`;
                const node = makeNode(tag, id);

                const classMatch = attrs.match(/class="([^"]*)"/);
                if (classMatch) {
                    for (const c of classMatch[1].split(/\s+/).filter(Boolean)) node.classes.add(c);
                    node.className = [...node.classes].join(' ');
                }

                node.hidden = /\bhidden\b/.test(attrs);
                node.disabled = /\bdisabled\b/.test(attrs);
                node.textContent = '';

                this.nodes.set(id, node);
            }
        },

        get innerHTML() { return this.html; },

        querySelector(sel) {
            return sel.startsWith('#') ? (this.nodes.get(sel.slice(1)) || null) : null;
        }
    };

    return container;
}

function makeNode(tag, id) {
    const node = {
        tag,
        id,
        textContent: '',
        className: '',
        hidden: false,
        disabled: false,
        classes: new Set(),
        listeners: {},

        addEventListener(type, handler) {
            (this.listeners[type] || (this.listeners[type] = [])).push(handler);
        },

        fire(type) {
            for (const handler of this.listeners[type] || []) handler({});
        }
    };

    node.classList = {
        add: (c) => { node.classes.add(c); node.className = [...node.classes].join(' '); },
        remove: (c) => { node.classes.delete(c); node.className = [...node.classes].join(' '); },
        toggle: (c, on) => { if (on) node.classes.add(c); else node.classes.delete(c); node.className = [...node.classes].join(' '); },
        contains: (c) => node.classes.has(c)
    };

    return node;
}

/** A step that plays a tone straight away, so the tests are not waiting on a clock. */
function makeSpeaker(step) {
    let played = 0;
    step.playTone = async () => { played += 1; return true; };
    step.played = () => played;
    return step;
}

/** Renders the step's own markup, so the wiring has ids to find. */
function rendered(step) {
    const container = fakeContainer();
    container.innerHTML = step.markup();
    return container;
}

const { SpeakerTest } = await import('../modules/SpeakerTest.js');
const { describeOutcome } = await import('../modules/SpeakerTone.js');

test('a fresh step has judged nothing and played nothing', () => {
    const s = new SpeakerTest();

    // null, not false. A section nobody answered is not a section that failed,
    // which is what kept a step abandoned halfway from reporting a dead speaker.
    assert.equal(s.earpieceWorking, null);
    assert.equal(s.loudspeakerWorking, null);
    assert.deepEqual(s.plays, { earpiece: 0, loudspeaker: 0 });
});

test('the tone can be played more than once before answering', async () => {
    // The reported reason for this change. The old play button disabled itself and
    // hid when the tone finished, so a tone missed while the volume was still
    // coming up was answered from, and the answer was final.
    const s = makeSpeaker(new SpeakerTest());
    const container = rendered(s);

    const section = s.askAbout(container, 'earpiece');
    const play = container.querySelector('#play-earpiece-btn');

    play.fire('click');
    await Promise.resolve();
    play.fire('click');
    await Promise.resolve();
    play.fire('click');
    await Promise.resolve();

    assert.equal(s.plays.earpiece, 3);
    assert.equal(s.played(), 3);
    assert.match(container.querySelector('#earpiece-status').textContent, /3 keer afgespeeld/);

    // Still unanswered, and still able to be.
    assert.equal(s.earpieceWorking, null);
    assert.equal(container.querySelector('#earpiece-feedback').hidden, false);
    void section;
});

test('the play button is never disabled or hidden', () => {
    const s = new SpeakerTest();
    const container = rendered(s);

    s.askAbout(container, 'earpiece');
    const play = container.querySelector('#play-earpiece-btn');

    // The step does not own the play button's appearance any more. It is the
    // operator's to press as often as they like, so nothing here may take it away
    // or fade it out.
    assert.equal(play.disabled, false);
    assert.doesNotMatch(container.html, /style="display: none"/);
    assert.doesNotMatch(container.html, /display:\s*none/);
});

test('the verdict buttons stay on screen after a verdict is given', async () => {
    // A verdict given by mistake used to be permanent: the buttons hid themselves
    // and the only way back was to rerun the whole step.
    const s = makeSpeaker(new SpeakerTest());
    const container = rendered(s);

    const section = s.askAbout(container, 'earpiece');
    container.querySelector('#play-earpiece-btn').fire('click');
    await Promise.resolve();

    container.querySelector('#earpiece-no').fire('click');
    await section;

    assert.equal(s.earpieceWorking, false);
    assert.equal(container.querySelector('#earpiece-feedback').hidden, false,
        'the buttons have to survive the verdict so it can be changed');

    // And the change takes.
    container.querySelector('#earpiece-yes').fire('click');
    assert.equal(s.earpieceWorking, true);
    assert.equal(container.querySelector('#earpiece-yes').classes.has('is-active'), true);
    assert.equal(container.querySelector('#earpiece-no').classes.has('is-active'), false);
});

test('the second speaker stays locked until the first is judged', () => {
    // The phone is held against the ear for one and flat on a table for the other.
    // Doing both at once means one is always judged in the wrong position.
    const s = new SpeakerTest();
    const container = rendered(s);

    s.askAbout(container, 'earpiece');

    assert.equal(container.querySelector('#loudspeaker-section').classes.has('is-locked'), true);
});

test('a tone the browser refused is not recorded as a verdict', async () => {
    // If the audio context will not start, the honest note is that no tone was
    // played. The old step asked "did you hear it?" and offered no verdict buttons,
    // and a loudspeaker blamed for a browser policy is a wrong label on a phone that
    // is fine.
    const s = new SpeakerTest();
    s.playTone = async () => false;

    const container = rendered(s);
    s.askAbout(container, 'earpiece');
    container.querySelector('#play-earpiece-btn').fire('click');
    await Promise.resolve();
    await Promise.resolve();

    assert.equal(s.plays.earpiece, 0, 'a failed tone is not a play');
    assert.equal(s.audioProblem, 'De browser gaf geen toon af');
    assert.equal(s.earpieceWorking, null);
    assert.equal(container.querySelector('#earpiece-feedback').hidden, true,
        'nothing to judge, so no buttons');
    assert.match(container.querySelector('#earpiece-status').textContent, /Geen toon/);
});

test('a tone that failed once can be tried again', async () => {
    const s = new SpeakerTest();
    let attempts = 0;
    s.playTone = async () => { attempts += 1; return attempts > 1; };

    const container = rendered(s);
    s.askAbout(container, 'earpiece');

    container.querySelector('#play-earpiece-btn').fire('click');
    await Promise.resolve(); await Promise.resolve();
    assert.equal(container.querySelector('#earpiece-feedback').hidden, true);

    container.querySelector('#play-earpiece-btn').fire('click');
    await Promise.resolve(); await Promise.resolve();

    assert.equal(s.plays.earpiece, 1);
    assert.equal(s.audioProblem, null, 'the earlier failure is cleared once a tone lands');
    assert.equal(container.querySelector('#earpiece-feedback').hidden, false);
});

test('a context that never wakes comes back as no tone, not as nothing', async () => {
    // Measured on a real Pixel over the secure origin. resume() does not always
    // settle: when the click carries no user activation the promise just hangs, and
    // the old unbounded await meant the status line never changed, no verdict
    // buttons appeared, and the operator pressed a dead button until the failsafe
    // ended the step a minute and a half later.
    const s = new SpeakerTest();
    SpeakerTest.RESUME_TIMEOUT_MS = 20;

    s.audioContext = {
        state: 'suspended',
        currentTime: 0,
        sampleRate: 48000,
        // Never settles, exactly like the real refusal.
        resume: () => new Promise(() => {}),
        close: () => {}
    };

    const started = await s.playTone(0.45);

    assert.equal(started, false);
    SpeakerTest.RESUME_TIMEOUT_MS = 1000;
});

test('a context that refuses with an error also comes back as no tone', async () => {
    const s = new SpeakerTest();
    SpeakerTest.RESUME_TIMEOUT_MS = 20;

    s.audioContext = {
        state: 'suspended',
        currentTime: 0,
        sampleRate: 48000,
        resume: () => Promise.reject(new Error('not allowed')),
        close: () => {}
    };

    const started = await s.playTone(0.45);

    // A rejected resume must not escape as an exception either. The runner would
    // catch it and mark the whole step failed, which blames the speaker for a
    // browser policy.
    assert.equal(started, false);
    SpeakerTest.RESUME_TIMEOUT_MS = 1000;
});

test('a running context plays and reports that it played', async () => {
    const s = new SpeakerTest();
    const scheduled = [];
    let closed = 0;

    s.audioContext = {
        state: 'running',
        currentTime: 4.5,
        sampleRate: 48000,
        destination: {},
        createOscillator: () => ({
            type: 'sine',
            frequency: { setValueAtTime: () => {} },
            connect: () => {},
            start: (t) => scheduled.push(['start', t]),
            stop: (t) => scheduled.push(['stop', t])
        }),
        createGain: () => ({
            gain: { setValueAtTime: () => {}, linearRampToValueAtTime: () => {}, exponentialRampToValueAtTime: () => {} },
            connect: () => {}
        }),
        close: () => { closed += 1; }
    };

    SpeakerTest.RESUME_TIMEOUT_MS = 20;
    const started = await s.playTone(0.45);
    SpeakerTest.RESUME_TIMEOUT_MS = 1000;

    assert.equal(started, true);
    // Five bells, each started and each stopped, and every start on the audio
    // clock the context reports rather than on wall time.
    assert.equal(scheduled.filter(e => e[0] === 'start').length, 5);
    assert.equal(scheduled.filter(e => e[0] === 'stop').length, 5);
    assert.ok(scheduled.every(e => e[1] >= 4.5), 'nothing may be scheduled before the context clock');
    void closed;
});

test('dispose closes the audio context and is safe to call twice', () => {
    // The runner calls dispose on every exit. A context left open keeps the audio
    // hardware awake, and the next step finds the volume slider doing nothing.
    const s = new SpeakerTest();
    let closed = 0;
    s.audioContext = { state: 'running', close: () => { closed += 1; } };

    s.dispose();
    s.dispose();

    assert.equal(closed, 1);
    assert.equal(s.audioContext, null);
});

test('dispose survives an audio context that refuses to close', () => {
    const s = new SpeakerTest();
    s.audioContext = { state: 'closed', close: () => { throw new Error('already closed'); } };

    assert.doesNotThrow(() => s.dispose());
    assert.equal(s.audioContext, null);
});

test('reset puts both verdicts back to unjudged', () => {
    const s = new SpeakerTest();
    s.earpieceWorking = true;
    s.loudspeakerWorking = false;
    s.plays.earpiece = 3;

    // A rerun has to start clean. A verdict carried over from the first attempt
    // would fail the second before anyone listened to anything.
    s.reset();

    assert.equal(s.earpieceWorking, null);
    assert.equal(s.loudspeakerWorking, null);
    assert.equal(s.plays.earpiece, 0);
});

test('the step label follows the two verdicts and the play counts', () => {
    // The end of the chain, checked here so the wiring between the two modules is
    // pinned: a listener failure on the loudspeaker has to name the loudspeaker.
    const s = new SpeakerTest();
    s.earpieceWorking = true;
    s.loudspeakerWorking = false;
    s.plays = { earpiece: 2, loudspeaker: 2 };

    const outcome = describeOutcome(
        { earpiece: s.earpieceWorking, loudspeaker: s.loudspeakerWorking },
        s.plays);

    assert.equal(outcome.passed, false);
    assert.match(outcome.notes, /Hoofdluidspreker geeft geen geluid/);
    assert.match(outcome.notes, /toon 2x en 2x afgespeeld/);
});
