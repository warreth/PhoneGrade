import test from 'node:test';
import assert from 'node:assert/strict';

Object.defineProperty(global, 'navigator', {
    value: { userAgent: 'Mozilla/5.0 (Linux; Android 17) Chrome/140' },
    writable: true,
    configurable: true
});

global.window = { isSecureContext: true };
global.location = { protocol: 'https:' };

import {
    CAPABILITY,
    classifyMediaError,
    explainMediaError,
    hasMediaDevices,
    isFixable,
    isSecureOrigin
} from '../modules/MediaCapability.js';

test('a refused prompt is a refusal and not a missing camera', () => {
    // The distinction the whole module exists for. Both used to produce the same
    // dead end, and only one of them is something the operator can do about.
    const refused = classifyMediaError({ name: 'NotAllowedError', message: 'Permission denied' });
    const absent = classifyMediaError({ name: 'NotFoundError', message: 'Requested device not found' });

    assert.equal(refused.kind, CAPABILITY.DENIED);
    assert.equal(absent.kind, CAPABILITY.MISSING);
    assert.equal(isFixable(refused.kind), true);
    assert.equal(isFixable(absent.kind), false);
});

test('the error names the browsers actually use are all sorted', () => {
    // Measured across the browsers, because each of these is a different name for
    // the same two outcomes and matching on the message instead of the name gave
    // a different answer in every language.
    assert.equal(classifyMediaError({ name: 'PermissionDeniedError' }).kind, CAPABILITY.DENIED);
    assert.equal(classifyMediaError({ name: 'SecurityError' }).kind, CAPABILITY.DENIED);
    assert.equal(classifyMediaError({ name: 'DevicesNotFoundError' }).kind, CAPABILITY.MISSING);
    assert.equal(classifyMediaError({ name: 'NotReadableError' }).kind, CAPABILITY.BUSY);
    assert.equal(classifyMediaError({ name: 'TrackStartError' }).kind, CAPABILITY.BUSY);
    assert.equal(classifyMediaError({ name: 'AbortError' }).kind, CAPABILITY.BUSY);
    assert.equal(classifyMediaError({ name: 'OverconstrainedError' }).kind, CAPABILITY.UNSUPPORTED);
    assert.equal(classifyMediaError({ name: 'ConstraintNotSatisfiedError' }).kind, CAPABILITY.UNSUPPORTED);
});

test('a camera another app was holding is worth another try', () => {
    // This is the case that produced a dead camera fault on a phone whose camera
    // was fine, because a messaging app had it open for one second.
    const busy = classifyMediaError({ name: 'NotReadableError', message: 'Could not start video source' });

    assert.equal(busy.kind, CAPABILITY.BUSY);
    assert.equal(busy.fixable, true);
    assert.match(explainMediaError(busy, 'achtercamera'), /andere app/);
});

test('an unrecognised error is reported as itself rather than forced into a kind', () => {
    // Guessing would put a reason on the report that nobody checked. A name the
    // module does not know is kept, and the message is passed on.
    const odd = classifyMediaError({ name: 'SomeNewError', message: 'the codec said no' });

    assert.equal(odd.kind, CAPABILITY.ERROR);
    assert.equal(odd.name, 'SomeNewError');
    assert.match(explainMediaError(odd, 'camera'), /the codec said no/);
});

test('something that is not an error at all does not throw', () => {
    // getUserMedia can reject with a string on some older builds.
    const nothing = classifyMediaError(undefined);

    assert.equal(nothing.kind, CAPABILITY.ERROR);
    assert.equal(nothing.message, '');
    assert.match(explainMediaError(nothing, 'microfoon'), /Onbekende fout/);
});

test('every kind has something to say to the operator', () => {
    // A card with no text on it is a card an operator skips, which is the problem
    // this module was written for.
    for (const kind of Object.values(CAPABILITY)) {
        const line = explainMediaError({ kind, name: 'X', message: 'm' }, 'microfoon');
        assert.ok(line.length > 0, `no text for ${kind}`);
        assert.match(line, /microfoon/, `no subject in the text for ${kind}`);
    }
});

test('the article belongs to the sentence, not to the subject', () => {
    // The caller passes a bare noun. Passing "de achtercamera" in produced
    // "Dit toestel of deze browser heeft geen de achtercamera", which reads like a
    // machine wrote it, and this text goes on a graded report.
    const missing = { kind: CAPABILITY.MISSING, name: 'NotFoundError', message: '' };

    assert.equal(
        explainMediaError(missing, 'achtercamera'),
        'Dit toestel of deze browser heeft geen achtercamera.'
    );
    assert.match(explainMediaError({ kind: CAPABILITY.DENIED }, 'microfoon'), /^Toegang tot de microfoon is geweigerd\./);
    assert.match(explainMediaError({ kind: CAPABILITY.BUSY }, 'camera'), /^De camera is in gebruik/);
    assert.match(explainMediaError({ kind: CAPABILITY.UNSUPPORTED }, 'camera'), /^De camera ondersteunt/);
});

test('a page that is not secure is named as the reason, not the hardware', () => {
    // The insecure-origin case is worth its own kind: nothing about the camera is
    // wrong, the page was served in a way the browser blocks.
    const line = explainMediaError({ kind: CAPABILITY.INSECURE }, 'camera');
    assert.match(line, /niet beveilig/);
    assert.equal(isFixable(CAPABILITY.INSECURE), false);
});

test('the real answer is the secure-context flag, and the protocol is only a fallback', () => {
    // isSecureContext is the browser telling us, rather than us guessing from the
    // address bar. Verified on the Pixel: the same page is false on the LAN
    // address and true over adb reverse.
    global.window.isSecureContext = false;
    assert.equal(isSecureOrigin(), false);

    global.window.isSecureContext = true;
    assert.equal(isSecureOrigin(), true);

    delete global.window.isSecureContext;
    global.location.protocol = 'http:';
    assert.equal(isSecureOrigin(), false);
    global.location.protocol = 'https:';
    assert.equal(isSecureOrigin(), true);
});

test('a mediaDevices object that is there but useless counts as absent', () => {
    // Some browsers expose navigator.mediaDevices on an insecure page but leave
    // getUserMedia out, so checking the object alone was not enough.
    global.navigator.mediaDevices = {};
    assert.equal(hasMediaDevices(), false);

    global.navigator.mediaDevices = { getUserMedia: () => {} };
    assert.equal(hasMediaDevices(), true);

    delete global.navigator.mediaDevices;
    assert.equal(hasMediaDevices(), false);
});
