import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

import nl from '../locales/nl.js';
import en from '../locales/en.js';
import de from '../locales/de.js';
import es from '../locales/es.js';
import fr from '../locales/fr.js';
import pt from '../locales/pt.js';
import zh from '../locales/zh.js';
import { t, setLocale, locale, resolveLocale, initLocale, applyStaticText } from '../modules/i18n.js';

/**
 * Every shipped dictionary, in the order the desktop lists them. The list is
 * spelled out here rather than read off the folder so a dictionary file that
 * nobody wired into i18n.js is a test failure instead of a language that exists
 * on disk and can never be chosen.
 */
const DICTIONARIES = { nl, en, de, es, fr, pt, zh };

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

test('every dictionary carries the same keys, and none of them is blank', () => {
    const reference = Object.keys(nl).sort();

    for (const [name, dict] of Object.entries(DICTIONARIES)) {
        assert.deepEqual(Object.keys(dict).sort(), reference,
            `${name}.js carries a different set of keys than nl.js, and a key in one language and not the other is a sentence that would show up as its own key`);

        for (const [key, value] of Object.entries(dict)) {
            assert.equal(typeof value, 'string', `${name}.js: ${key} is not a sentence`);
            assert.notEqual(value.trim(), '', `${name}.js: ${key} is empty`);
        }
    }

    assert.ok(reference.length >= 30,
        `only ${reference.length} keys found, the shell on its own is thirty odd sentences`);
});

test('every dictionary holds the same number of placeholders as the Dutch one', () => {
    // A translator who drops a {0} leaves the value out of the sentence entirely,
    // and nothing else in the suite notices: the sentence renders, it just no
    // longer says what it was about.
    const placeholders = (text) => (String(text).match(/\{\w+\}/g) || []).sort();

    for (const [name, dict] of Object.entries(DICTIONARIES)) {
        for (const key of Object.keys(nl)) {
            assert.deepEqual(placeholders(dict[key]), placeholders(nl[key]),
                `${name}.js: ${key} does not carry the same placeholders as nl.js`);
        }
    }
});

test('no dictionary is a copy of the Dutch one', () => {
    // Same keys would still pass the checks above if a language were a copy of
    // nl.js, and every operator in that language would be reading Dutch.
    for (const [name, dict] of Object.entries(DICTIONARIES)) {
        if (name === 'nl') continue;

        const identical = Object.keys(nl).filter(key => nl[key] === dict[key]);

        assert.ok(identical.length < Object.keys(nl).length / 2,
            `${name}.js has ${identical.length} of ${Object.keys(nl).length} sentences the same as Dutch`);
    }

    assert.equal(nl['results.title'], 'Testsuite voltooid');
    assert.equal(en['results.title'], 'Test Suite Complete');
});

test('every sentence the source asks for exists in every language', () => {
    const asked = keysAskedFor();

    assert.ok(asked.size >= 20, `the scan found only ${asked.size} keys, it is not reading the page`);

    for (const key of asked) {
        for (const [name, dict] of Object.entries(DICTIONARIES)) {
            assert.ok(Object.prototype.hasOwnProperty.call(dict, key), `${name}.js does not carry ${key}`);
        }
    }
});

test('every language on disk is wired in, and every wired language is on disk', () => {
    // The dictionary a language needs and the one i18n.js loads have to be the
    // same set. A file nobody imported is a language that exists and can never be
    // reached; an import with no file behind it is a crash on startup.
    const onDisk = readdirSync(path.join(wwwroot, 'locales'))
        .filter(name => name.endsWith('.js'))
        .map(name => name.replace(/\.js$/, ''))
        .sort();

    assert.deepEqual(onDisk, Object.keys(DICTIONARIES).sort(),
        'the dictionaries in wwwroot/locales and the ones i18n.js loads are not the same set');
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
    // A Norwegian phone read by a Dutch operator should read Dutch, not English,
    // which is only one step away from the tag and nothing else.
    assert.equal(resolveLocale('nb-NO'), 'nl');
    assert.equal(resolveLocale('sv-SE'), 'nl');
    assert.equal(resolveLocale(''), 'nl');
    assert.equal(resolveLocale(undefined), 'nl');
});

test('a regional tag is reduced to the language behind it', () => {
    assert.equal(resolveLocale('nl-BE'), 'nl');
    assert.equal(resolveLocale('NL-be'), 'nl');
    assert.equal(resolveLocale('en-GB'), 'en');
    assert.equal(resolveLocale('nl-NL-x-private'), 'nl');
    assert.equal(resolveLocale('de-AT'), 'de');
    assert.equal(resolveLocale('fr-CA'), 'fr');
    assert.equal(resolveLocale('pt-BR'), 'pt');
    assert.equal(resolveLocale('zh-Hans-CN'), 'zh');
    assert.equal(resolveLocale('zh-TW'), 'zh'); // simplified is what the app carries
});

test('every language the app carries can be asked for', () => {
    for (const name of Object.keys(DICTIONARIES)) {
        assert.equal(resolveLocale(name), name, `${name} is in the dictionary list but cannot be selected`);
        setLocale(name);
        assert.equal(locale(), name);
        assert.notEqual(t('shell.welcome'), name, `${name}.js does not carry the welcome sentence`);
    }

    setLocale('nl');
});

test('the language on the URL beats the language of the phone', () => {
    // The desktop passes its own language, because the person reading the
    // screen is the operator at the desk and the handset is the customer's.
    assert.equal(initLocale('?sessionId=x&lang=en', 'nl-BE'), 'en');
    assert.equal(initLocale('?lang=nl', 'en-US'), 'nl');
    assert.equal(initLocale('', 'en-US'), 'en');
    assert.equal(initLocale('', 'fr-FR'), 'fr');
    assert.equal(initLocale('?lang=de', 'nl-BE'), 'de');
    assert.equal(initLocale('?lang=zh', ''), 'zh');
    assert.equal(initLocale('', 'sv-SE'), 'nl'); // not a language this build carries

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

test('a sentence with a number in it keeps the number where it was', () => {
    // The steps that used to be built by joining a string to a value now have
    // the whole sentence in one place, so both languages can be read as one
    // sentence instead of as a fragment and a number.
    setLocale('nl');
    assert.equal(
        t('touch.touchCount', { touched: 5, cells: 112 }),
        'Aangeraakt: 5/112');
    assert.equal(
        t('location.accuracy', { metres: 8 }),
        'Nauwkeurigheid: 8 m');

    setLocale('en');
    assert.equal(
        t('touch.touchCount', { touched: 5, cells: 112 }),
        'Touched: 5/112');
});

test('a placeholder with nothing behind it stays visible rather than emptied', () => {
    // A value that never arrived is a bug worth seeing on screen, the same way
    // a key no dictionary carries shows itself. Blanking it would turn the
    // mistake into a sentence that reads as finished.
    setLocale('nl');
    assert.equal(t('touch.touchCount', { cells: 112 }), 'Aangeraakt: {touched}/112');
    assert.equal(t('nowhere.at.all', { x: 1 }), 'nowhere.at.all');
});

test('a value of any kind goes in as written', () => {
    setLocale('nl');
    assert.equal(t('location.accuracy', { metres: 0 }), 'Nauwkeurigheid: 0 m');
    assert.equal(t('location.accuracy', { metres: 12.5 }), 'Nauwkeurigheid: 12.5 m');
});

test('a key with nothing written for it shows the key rather than a blank', () => {
    setLocale('nl');

    const orphan = fakeElement({ i18n: 'not.a.key.yet' }, 'keep me');
    const doc = fakeDocument([orphan]);

    applyStaticText(doc);

    assert.equal(orphan.textContent, 'not.a.key.yet',
        'the key is wrong on screen but it is also the signal that an entry is missing');
});
