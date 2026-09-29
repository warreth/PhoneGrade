import test from 'node:test';
import assert from 'node:assert/strict';

import {
    INSPECTION_COLORS,
    gridColorFor,
    patchLabel,
    patchRect,
    nextPatch,
    previousPatch,
    createInspection,
    setVerdict,
    getVerdict,
    defectsInColor,
    allDefects,
    inspectedCount,
    describeDefects,
    isDisplayFaulty
} from '../modules/DisplayInspection.js';

test('a fresh inspection has nothing looked at and nothing wrong', () => {
    const insp = createInspection();

    assert.equal(insp.total, 12);
    assert.equal(inspectedCount(insp, 'white'), 0);
    assert.deepEqual(allDefects(insp), []);
    assert.equal(isDisplayFaulty(insp), false);
    assert.match(describeDefects(insp), /Geen dode of vastzittende pixels/);
});

test('the grid covers the whole panel with no sliver left over', () => {
    // Three equal columns of 33% would leave a strip of screen uninspected, and a
    // dead pixel in that strip is exactly the one nobody looks at.
    const { cols, rows, total } = createInspection();

    const widths = new Set();
    const heights = new Set();
    for (let i = 0; i < total; i++) {
        const r = patchRect(i, cols, rows);
        assert.ok(r.x0 >= 0 && r.y0 >= 0);
        assert.ok(r.x1 <= 1.0000001, `patch ${i} runs past the right edge`);
        assert.ok(r.y1 <= 1.0000001, `patch ${i} runs past the bottom edge`);
        widths.add(r.x0.toFixed(6));
        heights.add(r.y0.toFixed(6));
    }

    // The first and last patch have to meet at the edges exactly.
    const first = patchRect(0, cols, rows);
    const last = patchRect(total - 1, cols, rows);
    assert.equal(first.x0, 0);
    assert.equal(first.y0, 0);
    assert.equal(last.x1, 1);
    assert.equal(last.y1, 1);
    assert.equal(widths.size, cols);
    assert.equal(heights.size, rows);
});

test('a patch label says where on the screen it is', () => {
    // "Vak 1" is useless on a report. The operator has to be able to say which
    // corner, and the label has to name the row and the column.
    assert.equal(patchLabel(0, 3, 4), 'Vak 1 (rij 1, kolom 1)');
    assert.equal(patchLabel(4, 3, 4), 'Vak 5 (rij 2, kolom 2)');
    assert.equal(patchLabel(5, 3, 4), 'Vak 6 (rij 2, kolom 3)');
    assert.equal(patchLabel(11, 3, 4), 'Vak 12 (rij 4, kolom 3)');
});

test('paging wraps at both ends', () => {
    assert.equal(nextPatch(0, 12), 1);
    assert.equal(nextPatch(11, 12), 0);
    assert.equal(previousPatch(0, 12), 11);
    assert.equal(previousPatch(1, 12), 0);
});

test('the grid is visible on black', () => {
    // An OLED showing pure black is switched off. A white grid at the usual
    // opacity is invisible there, and the operator would be asked to inspect a
    // region they cannot see at all.
    assert.notEqual(gridColorFor('#000000'), gridColorFor('#ffffff'));
    assert.match(gridColorFor('#000000'), /0\.55/);
});

test('a verdict on one patch is kept apart from the same patch on another colour', () => {
    // A stuck subpixel can show on one colour and not on another. Collapsing them
    // would make a clean panel look faulty and hide a genuinely dead one.
    const insp = createInspection();
    setVerdict(insp, 'white', 5, 'defect');
    setVerdict(insp, 'black', 5, 'ok');

    assert.equal(getVerdict(insp, 'white', 5), 'defect');
    assert.equal(getVerdict(insp, 'black', 5), 'ok');
    assert.deepEqual(defectsInColor(insp, 'white'), [5]);
    assert.deepEqual(defectsInColor(insp, 'black'), []);
});

test('looking at the same patch twice keeps the later verdict', () => {
    const insp = createInspection();
    setVerdict(insp, 'white', 3, 'defect');
    setVerdict(insp, 'white', 3, 'ok');

    // The operator flagged it by accident and corrected themselves. The label
    // should not go on claiming a defect they then ruled out.
    assert.equal(getVerdict(insp, 'white', 3), 'ok');
    assert.equal(inspectedCount(insp, 'white'), 1);
});

test('a defect visible on more than one colour condemns the display', () => {
    const insp = createInspection();
    setVerdict(insp, 'white', 2, 'defect');
    setVerdict(insp, 'red', 2, 'defect');

    // Same patch, two colours: a dead subpixel or a dead column.
    assert.equal(isDisplayFaulty(insp), true);
    assert.deepEqual(allDefects(insp), [{ patch: 2, colors: ['white', 'red'] }]);
});

test('a defect on one colour only is reported but does not fail the display', () => {
    // An RGB stripe panel loses one of three subpixels under pure red. That is how
    // the panel is built, not a fault, and grading every such panel as faulty
    // would make the check worthless.
    const insp = createInspection();
    setVerdict(insp, 'red', 0, 'defect');

    assert.equal(isDisplayFaulty(insp), false);
    assert.match(describeDefects(insp), /1 defect gevonden/);
    assert.match(describeDefects(insp), /vak 1/);
    assert.match(describeDefects(insp), /rood/);
});

test('the note says where the defect is and under which colours', () => {
    // "Display defective" sends the next person to look at the wrong half of the
    // panel. The note has to be enough to find it again.
    const insp = createInspection();
    setVerdict(insp, 'white', 0, 'defect');
    setVerdict(insp, 'white', 7, 'defect');
    setVerdict(insp, 'blue', 0, 'defect');

    const note = describeDefects(insp);

    assert.match(note, /2 defecten/);
    assert.match(note, /vak 1 .* op wit en blauw/);
    assert.match(note, /vak 8 .* op wit/);
});

test('a single defect is written in the singular', () => {
    const insp = createInspection();
    setVerdict(insp, 'white', 1, 'defect');
    setVerdict(insp, 'red', 1, 'defect');

    assert.match(describeDefects(insp), /^1 defect gevonden/);
});

test('a clean panel says so plainly', () => {
    const insp = createInspection();
    for (let i = 0; i < insp.total; i++) {
        for (const c of INSPECTION_COLORS) setVerdict(insp, c.id, i, 'ok');
    }

    assert.equal(isDisplayFaulty(insp), false);
    assert.equal(describeDefects(insp), 'Geen dode of vastzittende pixels gevonden');
});

test('the inspection covers the colours a dead pixel has to be looked for under', () => {
    // White, red, green, blue and black are the five a stuck subpixel shows up
    // on. Grey is left out on purpose: it is a blend of the others and adds time
    // without adding a fault that the others would have missed.
    assert.deepEqual(INSPECTION_COLORS.map(c => c.id), ['white', 'red', 'green', 'blue', 'black']);
});

test('inspections of different sizes stay independent', () => {
    const small = createInspection({ cols: 2, rows: 2 });
    const large = createInspection({ cols: 3, rows: 4 });

    setVerdict(small, 'white', 0, 'defect');

    assert.equal(small.total, 4);
    assert.equal(large.total, 12);
    assert.deepEqual(allDefects(large), []);
});
