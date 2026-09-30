import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

import nl from '../locales/nl.js';
import en from '../locales/en.js';
import { t, setLocale, locale, resolveLocale, initLocale, applyStaticText } from '../modules/i18n.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const wwwroot = path.resolve(here, '..');

/**
 * Every shipped script and page, which is where the text an operator reads
 * lives. The tests and the dictionaries are left out: the tests only quote
 * sentences, and the dictionaries are what everything is checked against.
 */
function sourceFiles(dir = wwwroot, out = []) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            if (entry.name === 'tests' || entry.name === 'locales') continue;
            sourceFiles(full, out);
        } else if (entry.name.endsWith('.js') || entry.name.endsWith('.html')) {
            out.push(full);
        }
    }
    return out;
}

/** Every sentence the source asks for, by key. */
function keysAskedFor() {
    const asked = new Set();
    const call = /\bt\(\s*(['"`])([^'"`]+)\1\s*\)/g;
    const marked = /data-i18n(?:-label)?="([^"]+)"/g;

    for (const file of sourceFiles()) {
        const text = readFileSync(file, 'utf8');
        for (const match of text.matchAll(call)) asked.add(match[2]);
        for (const match of text.matchAll(marked)) asked.add(match[1]);
    }

    return asked;
}

/** A document holding only what writing the shell touches. */
function fakeDocument(elements) {
    return {
        documentElement: { lang: '' },
        querySelectorAll(selector) {
            const key = selector
                .replace(/^\[|\]$/g, '')
                .replace(/^data-/, '')
                .replace(/-([a-z])/g, (whole, letter) => letter.toUpperCase());
            return elements.filter(el => key in el.dataset);
        }
    };
}

function fakeElement(dataset, textContent = '') {
    const attrs = {};
    return {
        dataset,
        textContent,
        setAttribute(name, value) { attrs[name] = value; },
        getAttribute(name) { return name in attrs ? attrs[name] : null; }
    };
}

test('the two dictionaries carry the same keys, and none of them is blank', () => {
    const nlKeys = Object.keys(nl).sort();
    const enKeys = Object.keys(en).sort();

    assert.deepEqual(nlKeys, enKeys,
        'a key in one language and not the other is a sentence that would show up as its own key');

    for (const [name, dict] of [['nl', nl], ['en', en]]) {
        for (const [key, value] of Object.entries(dict)) {
            assert.equal(typeof value, 'string', `${name}.js: ${key} is not a sentence`);
            assert.notEqual(value.trim(), '', `${name}.js: ${key} is empty`);
        }
    }

    assert.ok(nlKeys.length >= 30,
        `only ${nlKeys.length} keys found, the shell on its own is thirty odd sentences`);
});

test('the two dictionaries are not one dictionary written out twice', () => {
    // Same keys would still pass if en.js were a copy of nl.js, and every
    // English operator would be reading Dutch.
    const identical = Object.keys(nl).filter(key => nl[key] === en[key]);

    assert.ok(identical.length < Object.keys(nl).length / 2,
        `${identical.length} of ${Object.keys(nl).length} sentences are the same in both`);
    assert.equal(nl['results.title'], 'Testsuite voltooid');
    assert.equal(en['results.title'], 'Test Suite Complete');
});

test('every sentence the source asks for exists in both languages', () => {
    const asked = keysAskedFor();

    assert.ok(asked.size >= 20, `the scan found only ${asked.size} keys, it is not reading the page`);

    for (const key of asked) {
        assert.ok(Object.prototype.hasOwnProperty.call(nl, key), `nl.js does not carry ${key}`);
        assert.ok(Object.prototype.hasOwnProperty.call(en, key), `en.js does not carry ${key}`);
    }
});

test('an operator who asked for nothing gets Dutch', () => {
    // Dutch is the product's own language, so it is what is left when the
    // address and the browser both say nothing useful.
    setLocale('');

    assert.equal(locale(), 'nl');
    assert.equal(t('shell.welcome'), 'Welkom bij PhoneGrade');
    assert.equal(t('results.retry'), 'Opnieuw');
    assert.equal(t('results.skipped'), 'Overgeslagen');
});

test('asking for English gives the English wording', () => {
    setLocale('en');

    assert.equal(t('shell.welcome'), 'Welcome to PhoneGrade');
    assert.equal(t('results.retry'), 'Retry');
    assert.equal(t('results.skipped'), 'Skipped');

    setLocale('nl');
});

test('a language this app does not carry falls back to Dutch', () => {
    // A German phone read by a Dutch operator should read Dutch, not English,
    // which is only one step away from the tag and nothing else.
    assert.equal(resolveLocale('de-DE'), 'nl');
    assert.equal(resolveLocale('fr'), 'nl');
    assert.equal(resolveLocale(''), 'nl');
    assert.equal(resolveLocale(undefined), 'nl');
});

test('a regional tag is reduced to the language behind it', () => {
    assert.equal(resolveLocale('nl-BE'), 'nl');
    assert.equal(resolveLocale('NL-be'), 'nl');
    assert.equal(resolveLocale('en-GB'), 'en');
    assert.equal(resolveLocale('nl-NL-x-private'), 'nl');
});

test('the language on the URL beats the language of the phone', () => {
    // The desktop passes its own language, because the person reading the
    // screen is the operator at the desk and the handset is the customer's.
    assert.equal(initLocale('?sessionId=x&lang=en', 'nl-BE'), 'en');
    assert.equal(initLocale('?lang=nl', 'en-US'), 'nl');
    assert.equal(initLocale('', 'en-US'), 'en');
    assert.equal(initLocale('', 'fr-FR'), 'nl');

    setLocale('nl');
});

test('a sentence no dictionary carries comes back as its own key', () => {
    // Wrong on the screen, and loudly so, rather than blank or half a
    // sentence. The scan over every source file is what keeps it rare.
    assert.equal(t('nowhere.to.be.found'), 'nowhere.to.be.found');
});

test('the shell is written in the language in use', () => {
    setLocale('nl');

    const title = fakeElement({ i18n: 'shell.title' });
    const welcome = fakeElement({ i18n: 'shell.welcome' });
    const restart = fakeElement({ i18nLabel: 'aria.restart' }, 'Run Again');
    const doc = fakeDocument([title, welcome, restart]);

    const written = applyStaticText(doc);

    assert.equal(title.textContent, 'PhoneGrade Testsuite');
    assert.equal(welcome.textContent, 'Welkom bij PhoneGrade');
    assert.equal(restart.textContent, 'Run Again',
        'an aria label is not text on the page, it must not be overwritten');
    assert.equal(restart.getAttribute('aria-label'), 'Testsuite opnieuw draaien');
    assert.equal(doc.documentElement.lang, 'nl', 'the page has to say which language it is in');
    assert.equal(written, 3);
});

test('switching to English rewrites the shell', () => {
    setLocale('nl');
    const welcome = fakeElement({ i18n: 'shell.welcome' });
    const skip = fakeElement({ i18n: 'shell.skipTest' }, 'Skip Test');
    const doc = fakeDocument([welcome, skip]);

    applyStaticText(doc);
    assert.equal(welcome.textContent, 'Welkom bij PhoneGrade');

    setLocale('en');
    const changed = applyStaticText(doc);

    assert.equal(welcome.textContent, 'Welcome to PhoneGrade');
    assert.equal(skip.textContent, 'Skip Test');
    assert.equal(skip.getAttribute('aria-label'), null);
    assert.equal(doc.documentElement.lang, 'en');
    assert.equal(changed, 2);

    setLocale('nl');
});

test('a key with nothing written for it shows the key rather than a blank', () => {
    setLocale('nl');

    const orphan = fakeElement({ i18n: 'not.a.key.yet' }, 'keep me');
    const doc = fakeDocument([orphan]);

    applyStaticText(doc);

    assert.equal(orphan.textContent, 'not.a.key.yet',
        'the key is wrong on screen but it is also the signal that an entry is missing');
});
