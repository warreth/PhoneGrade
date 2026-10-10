import test from 'node:test';
import assert from 'node:assert/strict';

import {
    INSPECTION_COLORS,
    VERDICT_OK,
    VERDICT_DEFECTIVE,
    createInspection,
    recordVerdict,
    verdictOf,
    colorName,
    defectiveColorIds,
    defectiveColorNames,
    isDisplayFaulty,
    describeDefects
} from '../modules/DisplayInspection.js';
import { setLocale } from '../modules/i18n.js';

test('a fresh inspection has judged nothing and found nothing', () => {
    const insp = createInspection();

    assert.equal(insp.verdicts.size, 0);
    assert.deepEqual(defectiveColorIds(insp), []);
    assert.equal(isDisplayFaulty(insp), false);
    assert.equal(describeDefects(insp), 'Geen kleurafwijkingen gevonden');
});

test('a colour judged twice keeps the later verdict', () => {
    // The operator pressed the wrong button and corrected themselves. A report
    // that kept the first answer would go on claiming a defect they ruled out a
    // second later.
    const insp = createInspection();
    recordVerdict(insp, 'white', VERDICT_DEFECTIVE);
    recordVerdict(insp, 'white', VERDICT_OK);

    assert.equal(verdictOf(insp, 'white'), VERDICT_OK);
    assert.equal(insp.verdicts.size, 1);
    assert.equal(isDisplayFaulty(insp), false);
});

test('one colour marked Slecht condemns the display', () => {
    // The verdict is the operator's, not a heuristic about subpixel layout. They
    // looked at a full screen of red and said something was wrong with it.
    const insp = createInspection();
    recordVerdict(insp, 'red', VERDICT_DEFECTIVE);

    assert.equal(isDisplayFaulty(insp), true);
    assert.deepEqual(defectiveColorIds(insp), ['red']);
    assert.deepEqual(defectiveColorNames(insp), ['Rood']);
});

test('the affected colours come back in the order the panel shows them', () => {
    // Pressed black, then red, then blue. The report has to read the way the
    // panel does, or the next person hunts the wrong corner first.
    const insp = createInspection();
    recordVerdict(insp, 'black', VERDICT_DEFECTIVE);
    recordVerdict(insp, 'red', VERDICT_DEFECTIVE);
    recordVerdict(insp, 'blue', VERDICT_DEFECTIVE);

    assert.deepEqual(defectiveColorIds(insp), ['red', 'blue', 'black']);
    assert.deepEqual(defectiveColorNames(insp), ['Rood', 'Blauw', 'Zwart']);
});

test('every colour judged Goed leaves the panel standing', () => {
    const insp = createInspection();
    for (const color of INSPECTION_COLORS) {
        recordVerdict(insp, color.id, VERDICT_OK);
    }

    assert.equal(isDisplayFaulty(insp), false);
    assert.equal(describeDefects(insp), 'Geen kleurafwijkingen gevonden');
});

test('the note names the affected colours, singular and plural', () => {
    // "Display defective" sends the next person to look at the whole panel.
    // The colours are as specific as the question that was asked.
    const one = createInspection();
    recordVerdict(one, 'white', VERDICT_DEFECTIVE);
    assert.equal(describeDefects(one), 'Afwijking gemeld bij: Wit');

    const two = createInspection();
    recordVerdict(two, 'white', VERDICT_DEFECTIVE);
    recordVerdict(two, 'black', VERDICT_DEFECTIVE);
    assert.equal(describeDefects(two), 'Afwijkingen gemeld bij: Wit, Zwart');
});

test('the colours are the five a panel fault shows up under', () => {
    // White, red, green, blue and black. Grey is a blend of the others and adds
    // time without adding a fault the five would have missed.
    assert.deepEqual(INSPECTION_COLORS.map(c => c.id), ['white', 'red', 'green', 'blue', 'black']);
    assert.ok(INSPECTION_COLORS.every(c => colorName(c) && c.hex),
        'each colour needs a name for the label and a fill for the screen');
});

test('the colour names follow the language the page is read in', () => {
    // The names used to be literals in the module, so an English operator read
    // "Wit, Rood" on the label. They come out of the dictionary now.
    assert.equal(colorName(INSPECTION_COLORS[1]), 'Rood');

    setLocale('en');
    assert.equal(colorName(INSPECTION_COLORS[1]), 'Red');

    const insp = createInspection();
    recordVerdict(insp, 'white', VERDICT_DEFECTIVE);
    assert.deepEqual(defectiveColorNames(insp), ['White']);

    setLocale('nl');
});

test('two inspections stay independent', () => {
    // The inspection is state on the test object. If it were shared, a second
    // device in the same session would inherit the first one's fault.
    const a = createInspection();
    const b = createInspection();

    recordVerdict(a, 'white', VERDICT_DEFECTIVE);

    assert.equal(isDisplayFaulty(a), true);
    assert.equal(isDisplayFaulty(b), false);
});

test('a judgement about a colour the panel does not show is not reported', () => {
    const insp = createInspection();
    recordVerdict(insp, 'magenta', VERDICT_DEFECTIVE);

    // It stays in the map, but the label only ever names the five colours on
    // screen, and a lookup that misses must not throw on the way there.
    assert.equal(verdictOf(insp, 'magenta'), VERDICT_DEFECTIVE);
    assert.deepEqual(defectiveColorIds(insp), []);
    assert.equal(describeDefects(insp), 'Geen kleurafwijkingen gevonden');
});

test('a colour with no verdict yet is neither Goed nor Slecht', () => {
    const insp = createInspection();
    recordVerdict(insp, 'red', VERDICT_OK);

    assert.equal(verdictOf(insp, 'white'), null);
    assert.equal(verdictOf(insp, 'blue'), null);
    assert.deepEqual(defectiveColorIds(insp), []);
});
