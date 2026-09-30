import test from 'node:test';
import assert from 'node:assert/strict';

/**
 * A document with no layout at all, only the three things the row builder uses:
 * elements exist, they take a className and a text, and they hold children.
 *
 * Nothing here parses markup, so an assertion that a string came out as text
 * means the row never handed it to a parser either.
 */
function fakeDocument() {
    const created = [];
    return {
        created,
        createElement(tag) {
            const el = {
                tag,
                className: '',
                textContent: '',
                dataset: {},
                children: [],
                appendChild(child) { el.children.push(child); return child; }
            };
            created.push(el);
            return el;
        }
    };
}

/** The first element the builder made with this class, or null. */
function byClass(doc, className) {
    return doc.created.find(el => el.className === className) || null;
}

global.document = fakeDocument();

import { buildResultRow } from '../modules/ResultRows.js';

test('a row says what ran and how it came out', () => {
    global.document = fakeDocument();

    const row = buildResultRow({ id: 'touch', name: 'Touchscreen', status: 'passed' });

    const name = byClass(global.document, 'result-item-name');
    const badge = byClass(global.document, 'result-badge passed');

    assert.equal(row.className, 'result-item');
    assert.equal(name.textContent, 'Touchscreen');
    assert.equal(badge.textContent, 'PASSED');
});

test('a skipped row carries the reason it was skipped', () => {
    // This is the whole point of the row. GPS skips three different ways, and a
    // bare SKIPPED leaves the operator to guess which: the browser refused, the
    // phone never sent a fix, or the browser has no location API at all.
    global.document = fakeDocument();

    const row = buildResultRow({
        id: 'location',
        name: 'GPS / Locatie',
        status: 'skipped',
        notes: 'Geolocation API ontbreekt in deze browser'
    });

    const note = byClass(global.document, 'result-item-note');
    assert.ok(note, 'the reason has to be on the screen');
    assert.equal(note.textContent, 'Geolocation API ontbreekt in deze browser');
    assert.equal(row.children[row.children.length - 1], note,
        'the note goes last so it lands on its own line under the row');
});

test('a note is set as text and never handed to a parser', () => {
    // The note comes off the phone. If it is written as markup, anything that
    // reads as a tag in it stops being a sentence and starts being DOM.
    global.document = fakeDocument();

    const row = buildResultRow({
        id: 'location',
        name: 'GPS / Locatie',
        status: 'failed',
        notes: 'Geen GPS-fix: <img src=x onerror="alert(1)"> vanwege de dekking'
    });

    const note = byClass(global.document, 'result-item-note');
    assert.equal(note.innerHTML, undefined, 'the note must be set as text, not markup');
    assert.equal(note.textContent, 'Geen GPS-fix: <img src=x onerror="alert(1)"> vanwege de dekking');
    assert.equal(row.children.filter(c => c.tag === 'img').length, 0);
});

test('a row without anything to say has no note element', () => {
    global.document = fakeDocument();

    buildResultRow({ id: 'touch', name: 'Touchscreen', status: 'passed' });

    assert.equal(byClass(global.document, 'result-item-note'), null);
    assert.equal(global.document.created.length, 3, 'name, badge and nothing else');
});

test('a passed row offers no retry', () => {
    global.document = fakeDocument();

    buildResultRow({ id: 'touch', name: 'Touchscreen', status: 'passed', notes: 'Alles werkt' });

    const retry = byClass(global.document, 'btn btn-secondary retry-test-btn');
    assert.equal(retry, null, 'there is nothing to repeat on a step that ran');
});

test('a skipped row can be run again, and knows which step it belongs to', () => {
    global.document = fakeDocument();

    buildResultRow({ id: 'location', name: 'GPS / Locatie', status: 'skipped', notes: 'Refused' });

    const retry = byClass(global.document, 'btn btn-secondary retry-test-btn');
    assert.ok(retry, 'a skip is worth another attempt');
    // The row's own id, not its position: a step can produce more than one row,
    // so the row at index 2 is not step 2.
    assert.equal(retry.dataset.testId, 'location');
    assert.equal(retry.textContent, 'Opnieuw');
});

test('a failed row can be run again', () => {
    global.document = fakeDocument();

    buildResultRow({ id: 'vibration', name: 'Trillen / Motor', status: 'failed' });

    const retry = byClass(global.document, 'btn btn-secondary retry-test-btn');
    assert.ok(retry);
    assert.equal(retry.dataset.testId, 'vibration');
});

test('every element is built, not string assembled', () => {
    global.document = fakeDocument();

    buildResultRow({
        id: 'location',
        name: 'GPS / Locatie',
        status: 'skipped',
        notes: 'Locatietoegang niet verleend binnen de ingestelde tijd'
    });

    assert.ok(global.document.created.every(el => typeof el.className === 'string'));
    assert.ok(global.document.created.some(el => el.textContent === 'Opnieuw'));
});
