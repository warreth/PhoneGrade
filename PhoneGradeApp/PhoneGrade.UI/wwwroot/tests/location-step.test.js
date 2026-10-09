import test, { beforeEach } from 'node:test';
import assert from 'node:assert/strict';

import {
    abandon,
    fakeClient,
    fakeContainer,
    reportedGaps,
    tick
} from './helpers/fake-dom.js';

/**
 * The location step, and the retry that used to look broken.
 *
 * Two separate faults, in one step. The first was that a refusal ended the step
 * on its own, the same way the camera and microphone steps did. The second was
 * worse in practice: the step re-armed its 45 s window on every refusal, so an
 * operator on a phone that kept saying no could press retry for as long as they
 * liked and the suite would never move on, and one that did move on did so with
 * the retry button still lit.
 *
 * The window is now opened once and only counted down from there, and the reason
 * the retry kept failing is written on the card, because on Android it is not
 * the thing most people think it is. Turning location on in the quick settings
 * is not enough: the browser keeps its own answer, and only its own site
 * permission changes it. That is what made the button look dead.
 */

beforeEach(() => {
    // Nothing global to reset for this step, but the shared page has to look
    // freshly loaded so a stray geolocation from another test cannot answer here.
    // The secure-context flag is what decides between the two automatic skips,
    // so it is put back the same way.
    global.navigator.geolocation = undefined;
    global.window.isSecureContext = true;
});

const { LocationTest, RetryDeadline } = await import('../modules/LocationTest.js');

/** A geolocation error as the browser builds it. */
function geoError(code, message = 'x') {
    return { code, message, PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 };
}

/**
 * Waits for the step to leave the running state.
 *
 * The three attempts are chained through timers, and how many event loop turns
 * they get before an assertion is not something a test should assume.
 */
async function settle(loc, tries = 60) {
    for (let i = 0; i < tries && loc.status === 'running'; i++) await tick(10);
    return loc.status;
}

/** A fix of the kind a phone returns when it can see the sky. */
const A_FIX = { coords: { latitude: 52.37021, longitude: 4.89517, accuracy: 8.4 } };

/** Points geolocation at a queue of answers, one per call, and records what was asked. */
function useGeolocation(answers) {
    const calls = [];
    global.navigator.geolocation = {
        getCurrentPosition: (ok, bad, options) => {
            calls.push({ options });
            const next = answers.shift();
            if (!next) throw new Error('the geolocation queue is empty');
            setTimeout(() => { (next.ok ? ok : bad)(next.value); }, 0);
        }
    };
    return calls;
}

test('location: a refusal asks the operator and keeps the step open', async () => {
    useGeolocation([{ value: geoError(1, 'User denied Geolocation') }]);
    const loc = new LocationTest();
    const container = fakeContainer();

    loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await tick();

    // The old step called skip() the instant the browser said no, before the
    // operator had had any chance to look at the phone.
    assert.equal(loc.status, 'running');
    assert.equal(container.nodes.get('location-error-area').hidden, false);
    assert.ok(container.nodes.get('btn-retry-location'));
    assert.ok(container.nodes.get('btn-loc-give-up'));

    // The instructions are the ones that actually change the answer. On Android
    // the browser keeps its own refusal, so only the site permission does it: the
    // quick-settings toggle alone leaves the retry returning the same refusal
    // without ever asking again, which is what made retry look broken.
    const steps = container.nodes.get('loc-fix-steps').innerHTML;
    assert.match(steps, /browserinstellingen/);
    assert.match(steps, /Locatie/);
    assert.match(steps, /Opnieuw proberen/);

    abandon(loc);
});

test('location: the grace window is opened once and is not restarted by a retry', () => {
    const deadline = new RetryDeadline(90000);
    const t0 = 1_000_000;

    assert.equal(deadline.arm(t0), true);
    assert.equal(deadline.arm(t0 + 5000), false, 'a second arming is refused');
    assert.equal(deadline.remainingMs(t0 + 5000), 85000, 'the window did not slide');
    assert.equal(deadline.remainingSeconds(t0 + 5000), 85);

    // And once the window has passed, arming cannot bring it back. That is the
    // whole point: the old code started a fresh 45 s on every refusal, so a phone
    // that kept saying no could hold the suite on this step for ever.
    assert.equal(deadline.remainingMs(t0 + 200000), 0);
    assert.equal(deadline.arm(t0 + 200000), false);

    deadline.clear();
    assert.equal(deadline.isArmed, false);
    assert.equal(deadline.remainingMs(t0), 90000, 'a cleared window is a full window again');
});

test('location: a retry inside the grace does not move the deadline, and a late fix still lands', async () => {
    const calls = useGeolocation([
        { value: geoError(1, 'denied') },
        { value: geoError(1, 'denied') },
        { ok: true, value: A_FIX }
    ]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await tick();

    // The operator can see how long the retry is still worth making, so waiting is
    // a decision rather than a guess.
    assert.match(container.nodes.get('loc-grace-note').textContent, /Over \d+ seconden/);

    // Two more refusals in a row, then a fix. The window keeps counting from the
    // first one and the fix is not thrown away for arriving late.
    container.nodes.get('btn-retry-location').press();
    await tick();
    container.nodes.get('btn-retry-location').press();
    await tick(30);

    assert.equal(calls.length, 3);
    assert.equal(loc.status, 'passed');
    assert.equal(loc.details.accuracyGrade, 'high');
    assert.equal(loc.details.latitude, 52.37021);
    assert.match(loc.notes, /Nauwkeurige GPS-fix op 8.4 m/);

    await run;
});

test('location: a retry puts the spinner back up and clears the error', async () => {
    useGeolocation([{ value: geoError(1, 'denied') }, { ok: true, value: A_FIX }]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await tick();
    assert.equal(container.nodes.get('location-error-area').hidden, false);

    container.nodes.get('btn-retry-location').press();

    // The old step left the error card up while it waited, so the phone looked
    // stuck even when the new request was already running.
    assert.equal(container.nodes.get('location-error-area').hidden, true);
    assert.equal(container.nodes.get('loc-spinner').hidden, false);
    assert.match(container.nodes.get('loc-status').textContent, /Wachten op coördinaten/);

    await tick(30);
    await run;
    assert.equal(loc.status, 'passed');
});

test('location: a coarse fix is a pass that says how coarse it was', async () => {
    useGeolocation([{ ok: true, value: { coords: { latitude: 52.37, longitude: 4.89, accuracy: 240 } } }]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await tick(30);

    assert.equal(loc.status, 'passed');
    assert.equal(loc.details.accuracyGrade, 'low');
    assert.match(loc.notes, /grover dan 100 m/);
    await run;
});

test('location: the accurate attempt is followed by a coarse one, and the options say so', async () => {
    const calls = useGeolocation([
        { value: geoError(3, 'Timeout expired') },
        { ok: true, value: { coords: { latitude: 52.37, longitude: 4.89, accuracy: 240 } } }
    ]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await tick(30);

    // The first attempt is the accurate one the step always asked for. When it
    // runs out the step asks again for a position the network can answer: that
    // is the route an indoor phone has, and it is what turns a timeout into a
    // fix. The options have to change with it, or the second call is the same
    // request that just failed.
    assert.equal(calls.length, 2);
    assert.equal(calls[0].options.enableHighAccuracy, true);
    assert.equal(calls[0].options.timeout, 30000);
    assert.equal(calls[0].options.maximumAge, 0);
    assert.equal(calls[1].options.enableHighAccuracy, false);
    assert.equal(calls[1].options.maximumAge, 60000);

    // The fallback fix is a pass, and the report says how coarse it was rather
    // than pretending it was a satellite fix.
    assert.equal(loc.status, 'passed');
    assert.equal(loc.details.accuracyGrade, 'low');
    assert.match(loc.notes, /grover dan 100 m/);
    await run;
});

test('location: a timeout on the hardware, not a question', async () => {
    const calls = useGeolocation([
        { value: geoError(3, 'Timeout expired') },
        { value: geoError(3, 'Timeout expired') },
        { value: geoError(3, 'Timeout expired') }
    ]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();

    // Three attempts are chained through timers; give them the turns they need
    // instead of assuming one tick is enough.
    await settle(loc);

    assert.equal(calls.length, 3);
    assert.equal(loc.status, 'failed');
    assert.match(loc.notes, /timeout/);
    assert.equal(loc.details.errorCode, 3);

    // And the way to improve it is still on the card, because indoors it does.
    assert.equal(container.nodes.get('location-error-area').hidden, false);
    assert.match(container.nodes.get('loc-fix-steps').innerHTML, /raam/);

    await run;
});

test('location: a last known position passes and says it came from the cache', async () => {
    const calls = useGeolocation([
        { value: geoError(3, 'Timeout expired') },
        { value: geoError(3, 'Timeout expired') },
        { ok: true, value: { coords: { latitude: 52.37, longitude: 4.89, accuracy: 100 } } }
    ]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await settle(loc);

    // A phone on a desk with no satellite in sight and no data can still say
    // where it last was. The fix is a pass because the pipeline answered, and
    // the note says the measurement is not fresh so nobody reads it as one.
    assert.equal(calls.length, 3);
    assert.equal(calls[2].options.maximumAge, Infinity);
    assert.equal(loc.status, 'passed');
    assert.equal(loc.details.fixSource, 'cache');
    assert.match(loc.notes, /laatst bekende locatie/);
    assert.match(loc.notes, /geen verse meting/);
    await run;
});

test('location: an unavailable position says the same and does not blame the browser', async () => {
    useGeolocation([
        { value: geoError(2, 'Position unavailable') },
        { value: geoError(2, 'Position unavailable') },
        { value: geoError(2, 'Position unavailable') }
    ]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await settle(loc);

    assert.equal(loc.status, 'failed');
    assert.match(loc.notes, /position unavailable/);
    assert.match(container.nodes.get('loc-error-msg').textContent, /geen positie door/);
    await run;
});

test('location: giving up is the operator\'s own decision', async () => {
    useGeolocation([{ value: geoError(1, 'denied') }]);
    const loc = new LocationTest();
    const container = fakeContainer();

    const run = loc.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-location').press();
    await tick();
    container.nodes.get('btn-loc-give-up').press();
    await run;

    // A skip, but on a record that says a person pressed a button, which is a very
    // different thing from a step that gave up on its own.
    assert.equal(loc.status, 'skipped');
    assert.match(loc.notes, /niet verleend/);
    assert.match(loc.notes, /niet gecontroleerd/);
});

test('location: a browser with no geolocation at all is the one automatic gap', async () => {
    delete global.navigator.geolocation;
    const loc = new LocationTest();
    const container = fakeContainer();

    await loc.run(fakeClient(), container);

    // Nothing can be done about this one and no operator can change it, so it is
    // recorded as a gap rather than left sitting on a step nobody can finish.
    assert.equal(loc.status, 'skipped');
    assert.equal(loc.details.capabilityGap, 'missing');
    assert.match(loc.notes, /ontbreekt/);
    assert.ok(container.innerHTML.length > 0, 'the operator is still told what happened');
});

test('location: a geolocation object with no method counts as absent', async () => {
    // navigator.geolocation exists on an insecure page with the method taken off
    // it, which is the shape that used to throw instead of reporting a gap.
    global.navigator.geolocation = {};
    const loc = new LocationTest();

    await loc.run(fakeClient(), fakeContainer());

    assert.equal(loc.status, 'skipped');
    assert.match(loc.notes, /ontbreekt/);
});

test('location: an insecure address names the setting to change, not a missing browser', async () => {
    // The case an operator runs into with a phone on the plain network address.
    // The browser withholds the whole API there, so allowing location on the
    // phone changes nothing and the step skipped forever. The sentence has to
    // point at the pc, because that is where the fix is.
    global.window.isSecureContext = false;
    delete global.navigator.geolocation;

    const loc = new LocationTest();
    const container = fakeContainer();

    await loc.run(fakeClient(), container);

    assert.equal(loc.status, 'skipped');
    assert.equal(loc.details.insecureOrigin, true);
    assert.equal(loc.details.secureContext, false);
    assert.match(loc.notes, /beveiligde herkomst/);
    assert.doesNotMatch(loc.notes, /ontbreekt/,
        'a browser that was never asked is not a browser without the API');

    // Both routes out, by the name each one has in the settings.
    assert.match(container.innerHTML, /Webtest via USB \(localhost\)/);
    assert.match(container.innerHTML, /Veilige verbinding via internet/);
    assert.ok(reportedGaps().includes('insecure-origin'),
        'the desktop is told which of the two gaps it is');
});

test('location: a secure address with no API is still the browser having none', async () => {
    // The other automatic skip, kept apart from the one above. Both are skips,
    // and only one of them is answered by changing something on the pc.
    global.window.isSecureContext = true;
    delete global.navigator.geolocation;

    const loc = new LocationTest();
    const container = fakeContainer();

    await loc.run(fakeClient(), container);

    assert.equal(loc.status, 'skipped');
    assert.equal(loc.details.insecureOrigin, undefined);
    assert.match(loc.notes, /ontbreekt/);
    assert.doesNotMatch(container.innerHTML, /Webtest via USB/);
});

test('location: the grace plus a second request is more than the single-measurement failsafe', () => {
    // The runner ended the step after 90 s, which is inside the window the operator
    // was given to go and change a setting.
    assert.ok(new LocationTest().getFailsafeMs() > 90000);
});
