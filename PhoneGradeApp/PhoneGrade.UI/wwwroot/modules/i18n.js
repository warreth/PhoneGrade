import nl from '../locales/nl.js';
import en from '../locales/en.js';
import de from '../locales/de.js';
import es from '../locales/es.js';
import fr from '../locales/fr.js';
import pt from '../locales/pt.js';
import zh from '../locales/zh.js';

/**
 * What the phone says, and in which language.
 *
 * The shell was written in English and the steps underneath it in Dutch, so one
 * screen carried both: "Test Suite Complete" over rows reading "Opnieuw" and
 * "Overgeslagen". Everything an operator reads comes out of here instead, and
 * every dictionary holds one language.
 *
 * Which language is not the phone's decision to make. The handset belongs to the
 * customer and can be set to anything at all, while the person reading the
 * screen is the operator at the desk, so the desktop passes its own language on
 * the query string. The browser's language is the second choice, for the case
 * where the page is opened by hand, and Dutch is what is left when neither
 * carries a language this app has.
 *
 * The list below is the same list the desktop keeps in its own language table,
 * and both are checked against each other by a test: a language in the picker
 * with no dictionary here would draw raw keys on the phone, and a dictionary
 * here with no entry in the table could never be reached from the settings.
 */
const DICTIONARIES = { nl, en, de, es, fr, pt, zh };

const DEFAULT_LOCALE = 'nl';

let active = DEFAULT_LOCALE;

/**
 * Reduces a tag such as `nl-BE` or `en-GB` to a language this app carries.
 *
 * Anything else falls back to the default rather than to English, because a
 * German phone opened by a Dutch operator should read Dutch and not the
 * language that happens to be one step away.
 *
 * @param {string} tag
 * @returns {string}
 */
export function resolveLocale(tag) {
    const primary = String(tag || '').toLowerCase().split('-')[0];

    return Object.prototype.hasOwnProperty.call(DICTIONARIES, primary)
        ? primary
        : DEFAULT_LOCALE;
}

/** The language everything is read in right now. */
export function locale() {
    return active;
}

/**
 * Switches the language for everything read from now on.
 * @returns {string} the language actually in use
 */
export function setLocale(tag) {
    active = resolveLocale(tag);
    return active;
}

/**
 * Picks the language once, from the address and then from the browser.
 *
 * The query string wins because the desktop puts its own language there, and
 * the phone's language is only a guess about who is reading.
 *
 * @param {string} search - the query string of the page
 * @param {string} navLanguage - `navigator.language`
 * @returns {string} the language in use
 */
export function initLocale(search = '', navLanguage = '') {
    let fromQuery = '';
    try {
        fromQuery = new URLSearchParams(search || '').get('lang') || '';
    } catch (e) {
        fromQuery = '';
    }

    return setLocale(fromQuery || navLanguage);
}

/**
 * One sentence, in the language in use.
 *
 * A key that no dictionary carries comes back as itself. That is deliberate:
 * showing a raw key is wrong, but it is also the loudest possible sign that a
 * string was added in one place and forgotten in the other, and the test over
 * both dictionaries is what turns that into a failure rather than a sentence an
 * operator cannot read.
 *
 * A sentence carrying a value has the value written into the sentence rather
 * than hung off the end of it, because the two languages do not put it in the
 * same place: "Nauwkeurigheid: 8 m" and "Accuracy: 8 m" agree, but a fragment
 * with a number appended does not let either language say it that way.
 *
 * A placeholder nothing arrived for is left as it is. Blanking it would turn a
 * missing value into a sentence that reads as finished.
 *
 * @param {string} key
 * @param {Object<string, number|string>} [vars] - values for `{name}` placeholders
 * @returns {string}
 */
export function t(key, vars) {
    const dict = DICTIONARIES[active] || DICTIONARIES[DEFAULT_LOCALE];
    const text = dict[key];

    if (text === undefined) return key;
    if (!vars) return text;

    return text.replace(/\{(\w+)\}/g, (whole, name) =>
        Object.prototype.hasOwnProperty.call(vars, name) ? String(vars[name]) : whole);
}

/**
 * Writes the dictionary over the shell.
 *
 * index.html is a static file, so its sentences are in the page before any
 * script runs. Marking them with `data-i18n` and filling them in here is what
 * leaves one language on the screen instead of the English of the markup
 * fighting the Dutch of the steps.
 *
 * The aria labels are written the same way: a screen reader hears the same
 * language the eye does.
 *
 * @param {Document} doc
 * @returns {number} how many pieces of text were written
 */
export function applyStaticText(doc) {
    let written = 0;

    for (const el of doc.querySelectorAll('[data-i18n]')) {
        el.textContent = t(el.dataset.i18n);
        written += 1;
    }

    for (const el of doc.querySelectorAll('[data-i18n-label]')) {
        el.setAttribute('aria-label', t(el.dataset.i18nLabel));
        written += 1;
    }

    if (doc.documentElement) {
        doc.documentElement.lang = active;
    }

    return written;
}
