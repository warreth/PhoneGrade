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

/** The index of the first test the desktop has no verdict for. */
export function firstPendingIndex(tests, progress) {
    const stored = indexSteps(progress);
    const index = tests.findIndex(t => !stored.has(String(t.id).toLowerCase()));
    return index < 0 ? tests.length : index;
}

/**
 * Copies stored verdicts onto the matching tests.
 *
 * The statuses are copied rather than recomputed. The phone already ran these,
 * and re-running them would mean putting the operator through the same steps a
 * second time, which is the whole thing this is meant to avoid.
 */
export function applyStoredResults(tests, progress) {
    const stored = indexSteps(progress);
    const applied = [];

    tests.forEach(test => {
        const step = stored.get(String(test.id).toLowerCase());
        if (!step) return;

        test.status = step.status || 'passed';
        test.notes = step.status === 'skipped' || step.status === 'failed'
            ? `Onthouden van de vorige run: ${step.status}`
            : 'Onthouden van de vorige run';
        test.details = test.details || {};
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
