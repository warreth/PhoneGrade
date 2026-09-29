/**
 * Picking a phone run back up where it stopped.
 *
 * The phone is the only side that knows how far it got, and it can reload at any
 * moment: a locked screen, a browser that dropped the tab, a launch from the
 * home screen. The desktop keeps a copy of the finished steps for exactly that
 * reason, and this turns that copy back into a starting point.
 *
 * The matching is on test id, never on name or position. Ids are the contract
 * between the two sides, and matching on a name or an index would put a verdict
 * on the wrong step the first time a step were renamed or the order changed.
 */

/** Steps keyed by lower-cased id, keeping only the most recent one for each. */
export function indexSteps(progress) {
    const map = new Map();
    for (const step of (progress && progress.steps) || []) {
        if (!step || !step.testId) continue;

        const key = String(step.testId).toLowerCase();
        const seen = map.get(key);
        if (!seen || new Date(step.reportedAt) >= new Date(seen.reportedAt)) {
            map.set(key, step);
        }
    }
    return map;
}

/**
 * The rows a step reports under.
 *
 * Read from the step rather than assumed to be one, because a step may produce
 * more than one row: the touchscreen step measures the grid and the outer edges
 * and reports each separately. A step that has not been asked yet falls back to
 * its own id, so an older module still resumes.
 */
export function resultIdsOf(test) {
    const ids = typeof test.resultIds === 'function' ? test.resultIds() : null;
    if (!ids || ids.length === 0) return [test.id];
    return ids;
}

/** True when every row this step reports has a stored verdict. */
export function isStepSettled(test, stored) {
    return resultIdsOf(test).every(id => stored.has(String(id).toLowerCase()));
}

/**
 * The index of the first test the desktop has no verdict for.
 *
 * A step counts as done only when all of its rows are stored. Half a step is not
 * a step, and treating the grid as finished while the edges are missing would
 * skip the edges without ever having run them.
 */
export function firstPendingIndex(tests, progress) {
    const stored = indexSteps(progress);
    const index = tests.findIndex(t => !isStepSettled(t, stored));
    return index < 0 ? tests.length : index;
}

/**
 * Copies stored verdicts onto the matching steps.
 *
 * The statuses are copied rather than recomputed. The phone already ran these,
 * and re-running them would mean putting the operator through the same steps a
 * second time, which is the whole thing this is meant to avoid.
 *
 * Where a step reports more than one row, the step's own status is taken from its
 * first row and the other rows are left on the step for toResults() to read back.
 * Overwriting the step with whichever row happened to be stored last would let a
 * passing grid hide a failing edge.
 */
export function applyStoredResults(tests, progress) {
    const stored = indexSteps(progress);
    const applied = [];

    tests.forEach(test => {
        const rows = resultIdsOf(test);
        const found = rows.map(id => stored.get(String(id).toLowerCase())).filter(Boolean);
        if (found.length === 0) return;

        const own = found[0];
        test.status = own.status || 'passed';
        test.notes = own.status === 'skipped' || own.status === 'failed'
            ? `Onthouden van de vorige run: ${own.status}`
            : 'Onthouden van de vorige run';
        test.details = test.details || {};
        test.storedRows = found;

        applied.push(test.id);
    });

    return applied;
}

/** True when there is a run worth offering to continue. */
export function canResume(progress) {
    if (!progress || !progress.started) return false;
    if (progress.finished) return false;
    return ((progress.steps || []).length) > 0;
}

/** The sentence shown above the start button. */
export function describeResume(progress) {
    if (!canResume(progress)) return '';

    const done = (progress.steps || []).length;
    const total = progress.totalTests || done;
    const where = progress.currentTestName ? ` Bezig met ${progress.currentTestName}.` : '';
    return `Er staan al ${done} van de ${total} tests klaar.${where}`;
}
