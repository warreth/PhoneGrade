/**
 * A browser, just real enough to drive a hardware step with.
 *
 * The camera, motion, microphone and location steps all write a card as markup
 * and then look the controls up by id. Testing them therefore needs two halves
 * of a page: the html the step produced, and the elements it goes on to press.
 * That pair lives here, once, so every one of those test files is about the step
 * rather than about rebuilding the same fake.
 *
 * What it deliberately does not have: a layout engine, a media stack, or a
 * device. Anything the step would have to invent, a test should hand in
 * explicitly, because a value the test made up is not evidence about the phone.
 */

/** The window listeners a step put on, so a test can deliver a real event. */
const windowListeners = new Map();

/** How many listeners have been taken off. A step that leaks is visible here. */
let windowRemovals = 0;

global.window = {
    devicePixelRatio: 3,
    isSecureContext: true,
    location: { search: '?sessionId=TEST_SUITE_123', protocol: 'http:', host: 'localhost:5055' },
    addEventListener(type, handler) {
        if (!windowListeners.has(type)) windowListeners.set(type, []);
        windowListeners.get(type).push(handler);
    },
    removeEventListener(type, handler) {
        const list = windowListeners.get(type) || [];
        const i = list.indexOf(handler);
        if (i >= 0) { list.splice(i, 1); windowRemovals += 1; }
    }
};

global.location = { protocol: 'https:' };

function makeBody() {
    const body = { style: {}, children: [] };
    body.appendChild = (el) => { body.children.push(el); return el; };
    body.removeChild = (el) => {
        const i = body.children.indexOf(el);
        if (i >= 0) body.children.splice(i, 1);
        return el;
    };
    body.contains = (el) => body.children.includes(el);
    return body;
}

global.document = {
    body: makeBody(),
    head: { appendChild: () => {} },
    addEventListener: () => {},
    removeEventListener: () => {},
    getElementById: () => null,
    createElement: (tag) => ({
        tag, className: '', style: {}, children: [],
        set innerHTML(v) { this._html = v; },
        get innerHTML() { return this._html || ''; },
        appendChild(child) { this.children.push(child); return child; },
        removeChild(child) {
            const i = this.children.indexOf(child);
            if (i >= 0) this.children.splice(i, 1);
            return child;
        },
        querySelector: () => null
    })
};

Object.defineProperty(global, 'navigator', {
    value: { vibrate: () => true, userAgent: 'Mozilla/5.0 (Linux; Android 17) Chrome/140' },
    writable: true,
    configurable: true
});

/**
 * The capability reports the steps send to the desktop.
 *
 * Left real, they turn every test into a connection attempt to a port with
 * nothing behind it, which is slow and noisy. Recording them is better than
 * stubbing them out: a refusal reaching the desktop as a missing camera is one
 * of the things worth catching, so the reports are part of what is under test.
 */
const reports = [];

global.fetch = async (url, options) => {
    if (typeof url === 'string' && url.includes('/api/pwa/log-warning')) {
        reports.push(JSON.parse(options.body));
    }
    return { ok: true, status: 200 };
};

/** Delivers a window event to whoever is listening, the way the browser would. */
export function fireWindow(type, event) {
    for (const handler of [...(windowListeners.get(type) || [])]) handler(event);
}

/** How many listeners of a kind are still attached. */
export function windowListenerCount(type) {
    return (windowListeners.get(type) || []).length;
}

/** How many listeners have been taken off since the last reset. */
export function windowRemovalCount() {
    return windowRemovals;
}

/** The capability gaps reported to the desktop, oldest first. */
export function reportedGaps() {
    return reports.map(r => r.reason);
}

/**
 * Puts the shared state back to how a fresh page would look.
 *
 * A step left open on purpose, as several of these are, keeps its listeners and
 * its timers. Carrying those into the next test would make it pass or fail on the
 * previous test's leftovers.
 */
export function resetPage() {
    windowListeners.clear();
    windowRemovals = 0;
    reports.length = 0;
}

/** A client that swallows everything, so a test never waits on the desktop. */
export function fakeClient() {
    return {
        sessionId: 'TEST_SUITE_123',
        baseUrl: 'http://localhost:5155',
        isConnected: () => false,
        send: () => {}
    };
}

/**
 * A container just real enough to press the buttons on.
 *
 * Ids that are not in the markup come back null, which is the point: a button
 * that has been renamed shows up as a failing test rather than as a test that
 * quietly stopped testing anything.
 */
export function fakeContainer() {
    const container = {
        html: '',
        nodes: new Map(),

        set innerHTML(v) {
            this.html = v || '';
            // A new render means a new card, so the previous card's handlers have
            // to go with it. A stale one that survived would answer a press meant
            // for a button that is no longer on screen.
            this.nodes.clear();

            for (const match of this.html.matchAll(/<(\w+)([^>]*?)\bid="([^"]+)"([^>]*)>/g)) {
                const [, tag, before, id, after] = match;
                const attrs = `${before} ${after}`;
                const node = makeNode(tag, id);
                const from = match.index + match[0].length;

                const classMatch = attrs.match(/class="([^"]*)"/);
                if (classMatch) {
                    for (const c of classMatch[1].split(/\s+/).filter(Boolean)) node.classes.add(c);
                    node.className = [...node.classes].join(' ');
                }

                // The file an image points at. A step that photographs several
                // lenses puts a different photo in each row, and a test that
                // cannot read which photo is where cannot tell the second lens
                // from the first, or a retake from the shot it replaced.
                const srcMatch = attrs.match(/\bsrc="([^"]*)"/);
                if (srcMatch) node.src = srcMatch[1];

                // The attributes the element was actually written with, not
                // sensible defaults. Which parts of a card start hidden is part of
                // what the operator sees before touching anything, and on the real
                // page the hidden attribute has to beat a class that sets a
                // display, which is a rule the stylesheet has to carry.
                node.hidden = /(^|\s)hidden(\s|=|$)/.test(attrs);
                node.disabled = /(^|\s)disabled(\s|=|$)/.test(attrs);

                // The words inside the element. Most of what these steps say to the
                // operator is text content, and a node with an empty textContent
                // would let a step delete its own instructions unnoticed.
                node.innerHTML = this.html.slice(from, closingTagAt(this.html, from, tag));
                node.textContent = innerTextOf(this.html, from, tag);

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

const VOID_ELEMENTS = new Set(['input', 'img', 'br', 'hr', 'meta', 'link', 'source', 'video', 'audio', 'canvas']);

/**
 * Where an element's closing tag sits, so the text inside it can be read back.
 *
 * Only the immediate close is looked for, and a void element never has one. The
 * markup in these steps does not nest same-named elements, so counting is not
 * needed and getting it subtly wrong would be worse than not counting.
 */
function closingTagAt(html, from, tag) {
    if (VOID_ELEMENTS.has(tag)) return from;
    const at = html.indexOf('</' + tag + '>', from);
    return at === -1 ? from : at;
}

/** The visible words between two offsets, with the tags taken out. */
function innerTextOf(html, from, tag) {
    const to = closingTagAt(html, from, tag);
    if (to <= from) return '';
    return html.slice(from, to)
        .replace(/<[^>]*>/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
}

/**
 * Every snapshot the fake canvas hands back, counted so that two lenses do not
 * come back as the same photo.
 */
let snapshotCount = 0;

export function makeNode(tag, id) {
    const node = {
        tag,
        id,
        textContent: '',
        className: '',
        innerHTML: '',
        style: {},
        hidden: false,
        disabled: false,
        classes: new Set(),
        listeners: {},
        onclick: null,
        videoWidth: 640,
        videoHeight: 480,

        getContext: () => ({ drawImage: () => {} }),

        /**
         * The photo a snapshot becomes.
         *
         * The frame itself is blank in here, but each capture has to come back as
         * its own image: a step that photographs several lenses has to be able to
         * tell the second photo from the first, and a test that cannot tell them
         * apart cannot tell whether the second lens was photographed at all.
         */
        toDataURL: () => `data:image/jpeg;base64,shot-${++snapshotCount}`,

        addEventListener(type, handler) {
            if (!this.listeners[type]) this.listeners[type] = [];
            this.listeners[type].push(handler);
        },

        /** Presses the button the way an operator would. */
        press() {
            if (typeof this.onclick === 'function') this.onclick();
            for (const handler of this.listeners.click || []) handler({ target: this });
        },

        fire(type) {
            for (const handler of this.listeners[type] || []) handler({ target: this });
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

/**
 * Leaves a step the way the runner's failsafe leaves it, without waiting for it.
 *
 * In production the runner always releases the promise, so a step still waiting
 * on a person is ended by the failsafe or the clock. The tests that use this are
 * about what was on the screen while the step was open, and waiting out a real
 * failsafe would make the suite take minutes.
 */
export function abandon(step) {
    step.dispose();
}

/** Lets pending microtasks and timers drain. */
export function tick(ms = 0) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

/** Sets the motion APIs on or off, the way a browser with or without them looks. */
export function setMotionApis(present) {
    if (present) {
        global.DeviceMotionEvent = function DeviceMotionEvent() {};
        global.DeviceOrientationEvent = function DeviceOrientationEvent() {};
    } else {
        delete global.DeviceMotionEvent;
        delete global.DeviceOrientationEvent;
    }
}

/** Points getUserMedia at a queue of answers, one per call. */
export function useGetUserMedia(answers) {
    const calls = [];
    global.navigator.mediaDevices = {
        getUserMedia: async (constraints) => {
            calls.push(constraints);
            const next = answers.shift();
            if (!next) throw { name: 'NotFoundError', message: 'the queue is empty' };
            // An answer with a name is a refusal and is thrown, the way the
            // browser throws one. Anything else is a stream that comes back.
            if (next.name) throw next;
            return next;
        }
    };
    return calls;
}

/** A refused prompt, the way getUserMedia reports one. */
export const DENIED = { name: 'NotAllowedError', message: 'Permission denied' };

/** A camera another app is holding open. */
export const BUSY = { name: 'NotReadableError', message: 'Could not start video source' };
