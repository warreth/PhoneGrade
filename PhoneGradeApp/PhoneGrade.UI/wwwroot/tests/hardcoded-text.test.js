import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const wwwroot = path.resolve(here, '..');

/**
 * Files whose text nobody reads as an operator.
 *
 * The debug overlay is a developer console that runs off a plain script in the
 * head, before any module has loaded, so it cannot reach the dictionary at all.
 * The remote logger ships lines to the desktop's Logs tab, which is log output
 * and stays as it is. The dictionaries and the tests are skipped because they
 * are the thing being checked rather than something that renders.
 */
const NOT_READ_BY_THE_OPERATOR = new Set([
    'debug-console.js',
    'RemoteConsoleLogger.js'
]);

const SKIPPED_DIRECTORIES = new Set(['tests', 'locales']);

function shippedFiles(dir = wwwroot, out = []) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            if (SKIPPED_DIRECTORIES.has(entry.name)) continue;
            shippedFiles(full, out);
        } else if (entry.name.endsWith('.js') && !NOT_READ_BY_THE_OPERATOR.has(entry.name)) {
            out.push(full);
        }
    }
    return out;
}

/**
 * Splits a file into comments, quoted text, and the code in between, keeping
 * every piece at the offset it came from so a hit can be reported by line.
 *
 * The comments matter: they are prose, full of apostrophes, and a reader that
 * took those for string delimiters would invent sentences that are not there.
 * Regex literals get the same treatment for the same reason.
 */
function lex(source) {
    const kept = [];       // comments blanked, quoted text kept
    const structural = []; // comments and quoted text both blanked
    const literals = [];
    const n = source.length;
    let i = 0;

    const pad = (text, blank) => {
        for (const ch of text) {
            kept.push(blank ? (ch === '\n' ? '\n' : ' ') : ch);
            structural.push(ch === '\n' ? '\n' : ' ');
        }
    };

    while (i < n) {
        const c = source[i];

        if (c === '/' && source[i + 1] === '/') {
            let j = i;
            while (j < n && source[j] !== '\n') j++;
            pad(source.slice(i, j), true);
            i = j;
            continue;
        }

        if (c === '/' && source[i + 1] === '*') {
            let j = i + 2;
            while (j < n && !(source[j] === '*' && source[j + 1] === '/')) j++;
            j = Math.min(n, j + 2);
            pad(source.slice(i, j), true);
            i = j;
            continue;
        }

        if (c === '\'' || c === '"') {
            const start = i;
            let j = i + 1;
            while (j < n && source[j] !== c) {
                if (source[j] === '\\') j++;
                else if (source[j] === '\n') break;
                j++;
            }
            const end = Math.min(n, j + 1);
            literals.push({ content: source.slice(start + 1, Math.max(start + 1, end - 1)), start, quote: c });
            pad(source.slice(start, end), false);
            i = end;
            continue;
        }

        if (c === '`') {
            const start = i;
            let j = i + 1;
            let content = '';
            while (j < n && source[j] !== '`') {
                if (source[j] === '\\') { content += source[j + 1] || ''; j += 2; continue; }
                if (source[j] === '$' && source[j + 1] === '{') {
                    // The expression itself is code; only what it is standing
                    // in for could be words on the screen, and that is written
                    // as t('key') elsewhere rather than here.
                    let depth = 1;
                    j += 2;
                    while (j < n && depth > 0) {
                        if (source[j] === '{') depth++;
                        else if (source[j] === '}') depth--;
                        j++;
                    }
                    continue;
                }
                content += source[j];
                j++;
            }
            const end = Math.min(n, j + 1);
            literals.push({ content, start, quote: '`' });
            pad(source.slice(start, end), false);
            i = end;
            continue;
        }

        if (c === '/') {
            const last = structural[structural.length - 1];
            const afterValue = last !== undefined && /[A-Za-z0-9_$)\]]/.test(last);
            if (!afterValue) {
                let j = i + 1;
                let inClass = false;
                while (j < n && source[j] !== '\n') {
                    if (source[j] === '\\') { j += 2; continue; }
                    if (source[j] === '[') inClass = true;
                    else if (source[j] === ']') inClass = false;
                    else if (source[j] === '/' && !inClass) break;
                    j++;
                }
                const end = Math.min(n, source[j] === '/' ? j + 1 : j);
                pad(source.slice(i, end), false);
                i = end;
                continue;
            }
        }

        kept.push(c);
        structural.push(c);
        i++;
    }

    return { kept: kept.join(''), structural: structural.join(''), literals };
}

/** The words a person would read, out of a piece of markup. */
function visibleWords(markup) {
    return markup
        .replace(/\$\{[^}]*\}/g, '')
        .replace(/<[^>]*>/g, ' ')
        .split(/\s+/)
        .filter(word => /[A-Za-zÀ-ɏ]/.test(word));
}

function hasLetters(text) {
    return /[A-Za-zÀ-ɏ]/.test(text);
}

/** Where the closing parenthesis of the one at `open` sits. */
function callEnd(structural, open) {
    let depth = 0;
    for (let i = open; i < structural.length; i++) {
        if (structural[i] === '(') depth++;
        else if (structural[i] === ')') { depth--; if (depth === 0) return i + 1; }
    }
    return structural.length;
}

/** The start offsets of the top level commas inside the call at `open`. */
function argumentStarts(structural, open) {
    const ends = callEnd(structural, open);
    const starts = [open + 1];
    let depth = 0; // already inside this call's parentheses
    for (let i = open + 1; i < ends; i++) {
        const c = structural[i];
        if (c === '(' || c === '[' || c === '{') depth++;
        else if (c === ')' || c === ']' || c === '}') depth--;
        else if (c === ',' && depth === 0) starts.push(i + 1);
    }
    starts.push(ends);
    return starts;
}

/**
 * Where the statement starting at `from` ends.
 *
 * A verdict can be spread over a line or two of ternary, so the end is the next
 * semicolon rather than the end of the line, with a bound so a missing one
 * cannot drag the rest of the file into the same hit.
 */
function statementEnd(structural, from) {
    let depth = 0;
    const limit = Math.min(structural.length, from + 600);
    for (let i = from; i < limit; i++) {
        const c = structural[i];
        if (c === '(' || c === '[' || c === '{') depth++;
        else if (c === ')' || c === ']' || c === '}') { depth--; if (depth < 0) return i; }
        else if (c === ';' && depth === 0) return i;
    }
    return limit;
}

function lineOf(kept, offset) {
    let line = 1;
    for (let i = 0; i < offset && i < kept.length; i++) {
        if (kept[i] === '\n') line++;
    }
    return line;
}

function literalsBetween(literals, from, to) {
    return literals.filter(lit => lit.start >= from && lit.start < to);
}

/**
 * Every rule name the scan below is allowed to raise.
 *
 * `note` refuses anything else, so a rule wired up in the scanner but left out
 * of here fails loudly, and the test over the groups fails until it is covered.
 */
const SCAN_RULES = [
    'markup',
    'verdict',
    'progress',
    'connection status',
    'step name',
    'pushed word',
    'join separator',
    'note',
    'rendered text',
    'aria label',
    'written value',
    'returned sentence'
];

/**
 * Everything an operator reads, looked for where it used to be written by hand.
 *
 * The rules are about sinks rather than about the shape of a sentence: a
 * string handed to a verdict, a note, an innerHTML, a textContent, an aria
 * label or a step's own name is read out loud on the phone, whatever it looks
 * like, while a class name, a colour or a selector next to it is not. Checking
 * sentences for Dutch-looking words instead would let an English literal
 * through, which is half of what was wrong.
 */
function violationsIn(file, source) {
    const { kept, structural, literals } = lex(source);
    const hits = [];
    const note = (rule, lit) => {
        if (!SCAN_RULES.includes(rule)) {
            throw new Error(`the scan raised "${rule}", which is not in SCAN_RULES and so has no test`);
        }

        // The dictionary call is the thing being checked against, so the key it
        // is handed is not a sentence written into the source.
        const askedForTheDictionary = /\bt\(\s*$/.test(structural.slice(Math.max(0, lit.start - 8), lit.start));
        if (askedForTheDictionary) return;

        // A literal the code compares against is a value it recognises, not a
        // sentence it writes. The resume banner reads a stored status this way,
        // and the status words around it are not what the operator sees.
        const comparedNotWritten = /(!==|!==|===|==|!=)\s*$/.test(structural.slice(Math.max(0, lit.start - 16), lit.start));
        if (comparedNotWritten) return;

        // What a person reads out of the piece, not the markup around it: a
        // bare <div> is letters without words, and would otherwise make every
        // card look translated when only its tags are.
        const readable = /<\s*\/?[A-Za-z!]/.test(lit.content)
            ? visibleWords(lit.content).join(' ')
            : lit.content;
        if (!hasLetters(readable)) return;

        // Braces mean it is a stylesheet a card injects rather than something
        // read out. Nothing an operator reads has them.
        if (/[{}]/.test(readable)) return;
        hits.push(`${file}:${lineOf(kept, lit.start)} [${rule}] ${readable.replace(/\s+/g, ' ').trim().slice(0, 70)}`);
    };

    // Markup of any kind: the text nodes are what gets read.
    for (const lit of literals) {
        if (/<\s*\/?[A-Za-z!]/.test(lit.content) && visibleWords(lit.content).length > 0) {
            note('markup', lit);
        }
    }

    const callSinks = [
        { name: 'verdict', pattern: /\.\s*(pass|fail|skip)\s*\(/g, args: () => null },
        { name: 'progress', pattern: /\breportProgress\s*\(/g, args: () => [2] },
        { name: 'connection status', pattern: /\bupdateConnectionStatus\s*\(/g, args: () => [1, 2] },
        { name: 'step name', pattern: /\bsuper\s*\(/g, args: () => [1, 2] },
        // Words gathered into a list are read back out of it later, so the
        // list itself is where the sentence is written.
        { name: 'pushed word', pattern: /\.\s*push\s*\(/g, args: () => null },
        { name: 'join separator', pattern: /\.\s*join\s*\(/g, args: () => [0] }
    ];

    for (const sink of callSinks) {
        for (const match of structural.matchAll(sink.pattern)) {
            const open = match.index + match[0].length - 1;
            const starts = argumentStarts(structural, open);
            const wanted = sink.args();
            for (let arg = 0; arg + 1 < starts.length; arg++) {
                if (wanted && !wanted.includes(arg)) continue;
                for (const lit of literalsBetween(literals, starts[arg], starts[arg + 1])) {
                    note(sink.name, lit);
                }
            }
        }
    }

    const assignmentSinks = [
        { name: 'note', pattern: /\bnotes\s*=(?!=)/g },
        { name: 'note', pattern: /\bnotes\s*:/g },
        { name: 'rendered text', pattern: /\.\s*(?:textContent|innerText|innerHTML)\s*=/g }
    ];

    for (const sink of assignmentSinks) {
        for (const match of structural.matchAll(sink.pattern)) {
            // The gap after the equals sign is walked in the text with the
            // strings still in it: the value being assigned is a string, and
            // skipping spaces in the blanked copy would run straight over it.
            let from = match.index + match[0].length;
            while (from < kept.length && /\s/.test(kept[from])) from++;
            const to = statementEnd(structural, from);
            for (const lit of literalsBetween(literals, from, to)) {
                note(sink.name, lit);
            }
        }
    }

    // A phrase written into anything. The property name cannot be read out of
    // a source scan, so the shape of the value decides instead: a sentence has
    // spaces in it, while a status, an id and a class name are one token or a
    // run of them. Only the value itself counts, so a call that happens to
    // take a sentence as an argument is left to the rule for that call, and
    // anything reached through `.style.` is a stylesheet rather than words.
    const NEVER_A_SENTENCE = new Set([
        'className', 'cssText', 'style', 'notes', 'textContent', 'innerText', 'innerHTML',
        // Colours and the version strings that travel to the desktop as
        // telemetry: both are full of spaces and neither is read out loud.
        'color', 'fillStyle', 'strokeStyle', 'osVersion', 'browserVersion'
    ]);
    const VALUE_POSITION = '=+?:,|&';
    const assignment = /([A-Za-z_$][\w$]*(?:\s*\.\s*[A-Za-z_$][\w$]*)*)\s*=(?!=)(?!>)/g;
    for (const match of structural.matchAll(assignment)) {
        const path = match[1];
        const last = path.split('.').pop().trim();
        if (NEVER_A_SENTENCE.has(last)) continue;
        if (path.includes('.style.')) continue;

        let from = match.index + match[0].length;
        while (from < kept.length && /\s/.test(kept[from])) from++;
        const to = statementEnd(structural, from);
        for (const lit of literalsBetween(literals, from, to)) {
            if (!/\s/.test(lit.content)) continue;
            const before = kept.slice(from, lit.start).replace(/\s+/g, '');
            const lastChar = before[before.length - 1];
            if (lastChar !== undefined && !VALUE_POSITION.includes(lastChar)) continue;
            // Inside an arrow the body is a run of statements of its own, and
            // a literal beyond a second assignment belongs to that assignment
            // and will be judged when its own turn comes.
            const arrow = before.lastIndexOf('=>');
            const tail = arrow < 0 ? before : before.slice(arrow + 2);
            if (tail.includes('=')) continue;
            note('written value', lit);
        }
    }

    // aria-label is the one sink named by a string rather than by code, so it
    // is found by reading the label the call was given.
    for (const match of structural.matchAll(/\bsetAttribute\s*\(/g)) {
        const open = match.index + match[0].length - 1;
        const starts = argumentStarts(structural, open);
        const label = literalsBetween(literals, starts[0], starts[1])[0];
        if (!label || label.content !== 'aria-label') continue;
        for (const lit of literalsBetween(literals, starts[1], starts[2] || starts[1])) {
            note('aria label', lit);
        }
    }

    // A sentence handed back to whoever calls this. One word is a token here:
    // the platform names and the statuses that travel as data look exactly
    // like a very short sentence, and only a phrase is worth chasing.
    for (const match of structural.matchAll(/\breturn\b/g)) {
        let from = match.index + match[0].length;
        while (from < kept.length && /\s/.test(kept[from])) from++;
        const lit = literals.find(l => l.start === from);
        if (!lit) continue;
        if (!/\s/.test(lit.content)) continue;
        note('returned sentence', lit);
    }

    return hits;
}

function allViolations() {
    const hits = [];
    for (const file of shippedFiles()) {
        const rel = path.relative(wwwroot, file).replace(/\\/g, '/');
        hits.push(...violationsIn(rel, readFileSync(file, 'utf8')));
    }
    return hits;
}

test('the scanner is looking at the shipped scripts', () => {
    const files = shippedFiles();
    assert.ok(files.length >= 20, `only ${files.length} files found, the walk is not reading wwwroot`);
    assert.ok(files.some(f => f.endsWith('TouchTest.js')), 'the steps themselves have to be in the list');
    assert.ok(!files.some(f => f.endsWith('locales')), 'the dictionaries are what is checked against');
});

/**
 * Every rule the scan can raise, and the test that fails on it.
 *
 * Held as data so a rule added without a test behind it is caught by the test
 * over this list rather than by someone remembering to write one.
 */
const RULE_GROUPS = [
    {
        test: 'no step writes an operator sentence into its markup',
        because: 'these text nodes are read out loud',
        rules: ['markup']
    },
    {
        test: 'no verdict, note or progress line is written by hand',
        because: 'these become the row under the step',
        rules: ['verdict', 'note', 'progress']
    },
    {
        test: 'no card writes its own labels, names or aria text',
        because: 'these are on the screen',
        rules: ['rendered text', 'step name', 'aria label', 'connection status', 'written value']
    },
    {
        test: 'no module hands a sentence back as a literal',
        because: 'these reach the caller and end up on a card',
        rules: ['returned sentence', 'pushed word', 'join separator']
    }
];

const KNOWN_RULES = RULE_GROUPS.flatMap(group => group.rules);

for (const group of RULE_GROUPS) {
    test(group.test, () => {
        const hits = allViolations().filter(h => group.rules.some(rule => h.includes(`[${rule}]`)));
        assert.deepEqual(hits, [], `${group.because}:\n${hits.join('\n')}`);
    });
}

test('every rule the scan can raise has a test behind it', () => {
    const named = new Set(KNOWN_RULES);
    assert.equal(named.size, KNOWN_RULES.length, 'the same rule is listed twice');
    assert.ok(SCAN_RULES.every(rule => named.has(rule)),
        'a rule with no test is a kind of hardcoded text nobody is checking: ' +
        SCAN_RULES.filter(rule => !named.has(rule)).join(', '));
});
