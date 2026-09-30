/**
 * How the run came out, one number per verdict.
 *
 * Skipped sits beside passed and failed rather than inside either: a step that
 * did not run is neither, and counting it as passed is how a run ends up
 * looking complete when part of it never happened.
 *
 * Anything else a step can still be (pending, running) only shows up in the
 * total, which is the honest place for it: the difference between the total and
 * the three counts is exactly how many rows never settled.
 */
const COUNTED = ['passed', 'failed', 'skipped'];

export function countResults(tests) {
    const list = Array.isArray(tests) ? tests : [];
    const counts = { total: list.length, passed: 0, failed: 0, skipped: 0 };

    for (const test of list) {
        const status = test && test.status;
        if (COUNTED.includes(status)) {
            counts[status] += 1;
        }
    }

    return counts;
}
