/**
 * What the operator is judging when the screen is one flat colour, kept apart
 * from the screen itself.
 *
 * A full-screen fill of a pure colour is the whole instrument. What is looked
 * for is that colour: a cast that should not be there, burn-in, backlight bleed
 * down one edge, a column that stays dark. Those are judgements about a colour,
 * and one colour at a time is the smallest question that still has an answer.
 *
 * The previous version framed the panel into a grid of patches and asked for a
 * verdict per patch per colour. Sixty answers to a question the operator could
 * not reliably answer is not more detail, it is noise, and it took a run an age
 * to get through.
 *
 * The five colours stay apart, because a panel with a green cast under white and
 * a clean red is a different fault from one that is wrong under all five, and
 * the label has to be able to say which colours were affected.
 *
 * The verdicts are a map keyed by colour id: judging a colour twice keeps the
 * later verdict, and the colours a run marked come back in the order the panel
 * shows them rather than in the order they were pressed.
 */

export const VERDICT_OK = 'ok';
export const VERDICT_DEFECTIVE = 'defective';

export const INSPECTION_COLORS = [
    { id: 'white', name: 'Wit', hex: '#ffffff' },
    { id: 'red', name: 'Rood', hex: '#ff0000' },
    { id: 'green', name: 'Groen', hex: '#00ff00' },
    { id: 'blue', name: 'Blauw', hex: '#0000ff' },
    { id: 'black', name: 'Zwart', hex: '#000000' }
];

export function createInspection() {
    return { verdicts: new Map() };
}

/**
 * Records what the operator said about one colour.
 *
 * Later wins. They pressed the wrong button, or looked again and changed their
 * mind, and a report that keeps the first answer would go on claiming a defect
 * they ruled out a second later.
 */
export function recordVerdict(inspection, colorId, verdict) {
    inspection.verdicts.set(colorId, verdict);
}

export function verdictOf(inspection, colorId) {
    return inspection.verdicts.get(colorId) || null;
}

/** The colours marked Slecht, in the order the panel shows them. */
export function defectiveColorIds(inspection) {
    return INSPECTION_COLORS
        .filter(color => inspection.verdicts.get(color.id) === VERDICT_DEFECTIVE)
        .map(color => color.id);
}

export function defectiveColorNames(inspection) {
    return defectiveColorIds(inspection)
        .map(id => INSPECTION_COLORS.find(color => color.id === id).name);
}

export function isDisplayFaulty(inspection) {
    return defectiveColorIds(inspection).length > 0;
}

/**
 * The note that goes on the row and from there onto the grading label.
 *
 * "Display defective" sends the next person to look at the whole panel. The
 * colours are enough to find the fault again, and there is no further to go than
 * that: a colour is as specific as the question asked.
 */
export function describeDefects(inspection) {
    const names = defectiveColorNames(inspection);

    if (names.length === 0) {
        return 'Geen kleurafwijkingen gevonden';
    }

    return names.length === 1
        ? `Afwijking gemeld bij: ${names[0]}`
        : `Afwijkingen gemeld bij: ${names.join(', ')}`;
}
