/**
 * The reasoning behind a dead-pixel inspection, kept apart from the pixels.
 *
 * Looking for a dead pixel on a 6.7 inch 1440p panel does not work by showing a
 * flat colour and asking the operator whether they can see one. A single stuck
 * subpixel is a fraction of a millimetre, and at arm's length on a full-screen
 * fill it is invisible. What works is looking at a small patch at a time,
 * magnified, and saying where you are looking.
 *
 * So the inspection is a grid of patches, each inspected on its own at a
 * magnification, with a verdict recorded per patch rather than one verdict for
 * the whole screen. A panel with one dead pixel in the corner of an otherwise
 * perfect screen is a panel with one dead pixel, and a single pass/fail for the
 * entire display would either hide it among the good ones or condemn the whole
 * screen for it.
 *
 * The colour cycling is still there, because an OLED that shows a stuck subpixel
 * under white and not under red is a different fault from one that shows it
 * under both. The colours are the last line of the inspection, not the whole of
 * it.
 */

/** The colours the panel is inspected under, in the order they are shown. */
export const INSPECTION_COLORS = [
    { id: 'white', name: 'Wit', hex: '#ffffff' },
    { id: 'red', name: 'Rood', hex: '#ff0000' },
    { id: 'green', name: 'Groen', hex: '#00ff00' },
    { id: 'blue', name: 'Blauw', hex: '#0000ff' },
    { id: 'black', name: 'Zwart', hex: '#000000' }
];

/** Pure black on an OLED shows the panel as switched off, so the grid has to be brighter there. */
export function gridColorFor(hex) {
    return hex === '#000000' ? 'rgba(255,255,255,0.55)' : 'rgba(255,255,255,0.35)';
}

/** The patch label, so the operator can say where a defect is. */
export function patchLabel(index, cols) {
    return `Vak ${index + 1} (rij ${Math.floor(index / cols) + 1}, kolom ${(index % cols) + 1})`;
}

/**
 * The rectangle a patch covers, in fractions of the panel.
 *
 * Kept as fractions rather than pixels so the same grid fits any screen and the
 * loupe does not need to know the panel size. The last row and column take the
 * remainder rather than a share, so three columns always cover the full width
 * instead of leaving a sliver unpainted.
 */
export function patchRect(index, cols, rows) {
    const col = index % cols;
    const row = Math.floor(index / cols);

    const x0 = col / cols;
    const y0 = row / rows;
    const x1 = col + 1 === cols ? 1 : (col + 1) / cols;
    const y1 = row + 1 === rows ? 1 : (row + 1) / rows;

    return { x0, y0, x1, y1, width: x1 - x0, height: y1 - y0 };
}

/** The next patch after the one given, wrapping at the end. */
export function nextPatch(index, total) {
    return (index + 1) % total;
}

export function previousPatch(index, total) {
    return (index - 1 + total) % total;
}

/**
 * One inspection's worth of results.
 *
 * Kept as a plain object with a small set of operations rather than as state on
 * the test, so the counting can be checked without a screen, a canvas or a phone.
 */
export function createInspection({ cols = 3, rows = 4 } = {}) {
    return {
        cols,
        rows,
        total: cols * rows,
        // Per patch, per colour. Keyed as `${colorId}:${patchIndex}` so a defect
        // found on white and the same patch on black stay apart, which is the
        // difference between a stuck subpixel and a dead one.
        verdicts: new Map(),
        current: 0,
        colorsDone: new Set()
    };
}

function key(colorId, patch) {
    return `${colorId}:${patch}`;
}

/** Records a verdict for one patch under one colour. */
export function setVerdict(inspection, colorId, patch, verdict) {
    inspection.verdicts.set(key(colorId, patch), verdict);
    return inspection;
}

/** The verdict for one patch under one colour, or null when not looked at. */
export function getVerdict(inspection, colorId, patch) {
    return inspection.verdicts.get(key(colorId, patch)) || null;
}

/** Patches flagged as faulty under one colour, in grid order. */
export function defectsInColor(inspection, colorId) {
    const out = [];
    for (let i = 0; i < inspection.total; i++) {
        if (getVerdict(inspection, colorId, i) === 'defect') out.push(i);
    }
    return out;
}

/** Every flagged patch, across all colours, with the colours it was seen under. */
export function allDefects(inspection) {
    const byPatch = new Map();

    for (const [composite, verdict] of inspection.verdicts) {
        if (verdict !== 'defect') continue;
        const [colorId, patchText] = composite.split(':');
        const patch = Number(patchText);

        if (!byPatch.has(patch)) byPatch.set(patch, new Set());
        byPatch.get(patch).add(colorId);
    }

    return [...byPatch.entries()]
        .sort((a, b) => a[0] - b[0])
        .map(([patch, colors]) => ({ patch, colors: [...colors] }));
}

/** How many patches have been looked at, for the progress readout. */
export function inspectedCount(inspection, colorId) {
    let count = 0;
    for (let i = 0; i < inspection.total; i++) {
        if (getVerdict(inspection, colorId, i)) count++;
    }
    return count;
}

/**
 * The sentence put on the grading label.
 *
 * Says where the defects are rather than only that there are some, because "display
 * defective" sends the next person looking at the wrong half of the panel. A
 * single defect on one colour and a defect on every colour are different faults
 * and the note has to keep them apart.
 */
export function describeDefects(inspection) {
    const defects = allDefects(inspection);
    if (defects.length === 0) return 'Geen dode of vastzittende pixels gevonden';

    const parts = defects.map(({ patch, colors }) => {
        const where = patchLabel(patch, inspection.cols).toLowerCase();
        const colorNames = colors
            .map(id => (INSPECTION_COLORS.find(c => c.id === id) || { name: id }).name.toLowerCase());
        return `${where} op ${colorNames.join(' en ')}`;
    });

    return `${defects.length} defect${defects.length === 1 ? '' : 'en'} gevonden: ${parts.join('; ')}`;
}

/**
 * Whether the panel should be graded down.
 *
 * A defect that shows on every colour is a dead subpixel or a dead column. One
 * that shows on a single colour is often an LCD subpixel arrangement rather than
 * a fault: an RGB stripe panel loses one of three subpixels under pure red, and
 * that is not a defect. Treating the two the same would fail a large share of
 * perfectly good panels, so a single-colour sighting is reported but does not by
 * itself condemn the display.
 */
export function isDisplayFaulty(inspection) {
    const defects = allDefects(inspection);
    if (defects.length === 0) return false;

    return defects.some(d => d.colors.length > 1);
}
