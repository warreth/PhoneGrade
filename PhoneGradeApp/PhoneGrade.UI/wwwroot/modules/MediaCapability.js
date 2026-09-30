/**
 * Why a media or sensor call did not work, and what to tell the operator.
 *
 * The steps used to answer this question themselves, and they answered it the
 * same way every time: something went wrong, so skip. That produced a skip for a
 * prompt the operator had not answered yet, a skip for a camera that was busy
 * for a second, and a skip for a browser that needed a setting changed. None of
 * those are the same as "this phone has no camera", and only the last one is
 * worth a gap on the grading label.
 *
 * So the guesses are collected here, in one place, as data. The difference that
 * matters is between a capability that is genuinely absent (nothing the operator
 * can do, and the grade should know about it) and everything else (a prompt, a
 * setting, another app holding the camera), which is a thing to tell the
 * operator and retry, not a verdict.
 */

/** The kinds of "not working" a media call can come back with. */
export const CAPABILITY = {
    /** The browser has no such API at all. A real gap in the device or browser. */
    MISSING: 'missing',
    /** The API exists, but the page is not a secure context, so it is blocked. */
    INSECURE: 'insecure',
    /** The operator was asked and refused, or the policy blocks it. Fixable. */
    DENIED: 'denied',
    /** The hardware is there but busy, or the OS would not hand it over yet. */
    BUSY: 'busy',
    /** The requested device or constraint does not exist. */
    UNSUPPORTED: 'unsupported',
    /** Something else. Reported as-is rather than guessed at. */
    ERROR: 'error'
};

/** The error names that mean each kind, across the browsers. */
const ERROR_NAMES = {
    denied: ['NotAllowedError', 'PermissionDeniedError', 'SecurityError'],
    missing: ['NotFoundError', 'DevicesNotFoundError'],
    busy: ['NotReadableError', 'TrackStartError', 'AbortError'],
    unsupported: ['OverconstrainedError', 'ConstraintNotSatisfiedError', 'NotSupportedError', 'TypeError']
};

/**
 * Sorts a thrown media error into one of the kinds above.
 *
 * The name is used rather than the message: the messages are localised and
 * reworded between browsers, and matching on them produced a different answer in
 * every language. An unrecognised error keeps its own name and message instead of
 * being forced into a category it may not belong to.
 *
 * @param {unknown} error
 * @returns {{kind: string, name: string, message: string, fixable: boolean}}
 */
export function classifyMediaError(error) {
    const name = (error && error.name) || '';
    const message = (error && error.message) || '';

    if (!name && !message) {
        return { kind: CAPABILITY.ERROR, name: 'UnknownError', message: '', fixable: false };
    }

    for (const [kind, names] of Object.entries(ERROR_NAMES)) {
        if (names.includes(name)) {
            return { kind, name, message, fixable: isFixable(kind) };
        }
    }

    return { kind: CAPABILITY.ERROR, name: name || 'Error', message, fixable: false };
}

/**
 * Whether the operator can do something about it.
 *
 * A refused prompt and a busy camera are both worth another attempt: the
 * operator may have since granted access, or closed the app that held the
 * camera. A missing API is not, and a page that is not a secure context is only
 * fixable by changing how the page is served, which is the desktop's job, not
 * the operator's.
 */
export function isFixable(kind) {
    return kind === CAPABILITY.DENIED || kind === CAPABILITY.BUSY;
}

/** True when the browser exposes the call at all. */
export function hasMediaDevices() {
    return !!(navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function');
}

/** True when the page is served over HTTPS or localhost. */
export function isSecureOrigin() {
    // window.isSecureContext is the real answer where it exists. The protocol
    // check is the fallback for the older browsers that do not have it.
    if (typeof window !== 'undefined' && typeof window.isSecureContext === 'boolean') {
        return window.isSecureContext;
    }
    return typeof location !== 'undefined' && location.protocol === 'https:';
}

/**
 * A short Dutch line to show the operator for a classified error.
 *
 * Written as an instruction where there is one to give. "NotAllowedError" tells
 * the operator nothing, and a step that says nothing is a step that gets
 * skipped.
 *
 * The subject is a bare noun and the article is part of each sentence, so
 * "achtercamera" reads correctly whether it opens a clause or sits after a
 * preposition. Passing the article in produced "heeft geen de achtercamera".
 */
export function explainMediaError(result, subject = 'camera') {
    switch (result.kind) {
        case CAPABILITY.DENIED:
            return `Toegang tot de ${subject} is geweigerd. Sta het toe in de browser en probeer opnieuw.`;
        case CAPABILITY.MISSING:
            return `Dit toestel of deze browser heeft geen ${subject}.`;
        case CAPABILITY.INSECURE:
            return `De pagina is niet beveiligd, daarom blokkeert de browser de ${subject}.`;
        case CAPABILITY.BUSY:
            return `De ${subject} is in gebruik door een andere app. Sluit die app en probeer opnieuw.`;
        case CAPABILITY.UNSUPPORTED:
            return `De ${subject} ondersteunt deze instelling niet.`;
        default:
            return result.message
                ? `Onbekende fout bij de ${subject}: ${result.message}`
                : `Onbekende fout bij de ${subject}.`;
    }
}
