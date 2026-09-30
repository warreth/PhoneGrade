/**
 * One row on the results screen: what ran, how it came out, and why.
 *
 * The name and the badge were all that was ever shown, so a step that could not
 * run said nothing about it. The GPS step is where it shows up: it skips when
 * the browser refuses, when the phone never sends a fix, and when the browser
 * has no location API at all, and all three read as a bare SKIPPED with the
 * note under them left off the screen. The note is already on the row. It just
 * never reached the operator.
 *
 * Built as elements rather than as markup. A name and a note are strings that
 * come off the phone, and an innerHTML holding one puts a device-controlled
 * string into the parser.
 */
export function buildResultRow(test) {
    const item = document.createElement('div');
    item.className = 'result-item';

    const name = document.createElement('span');
    name.className = 'result-item-name';
    name.textContent = test.name;

    const badge = document.createElement('span');
    badge.className = 'result-badge ' + test.status;
    badge.textContent = String(test.status).toUpperCase();

    item.appendChild(name);
    item.appendChild(badge);

    // A failed or skipped row can be run again; one that ran and passed has
    // nothing to repeat.
    if (test.status === 'failed' || test.status === 'skipped') {
        const retry = document.createElement('button');
        retry.className = 'btn btn-secondary retry-test-btn';
        // The row's own id, not its position: a step can produce more than one
        // row, so the row at index 2 is not step 2.
        retry.dataset.testId = test.id;
        retry.textContent = 'Opnieuw';
        item.appendChild(retry);
    }

    if (test.notes) {
        const note = document.createElement('span');
        note.className = 'result-item-note';
        note.textContent = test.notes;
        item.appendChild(note);
    }

    return item;
}
