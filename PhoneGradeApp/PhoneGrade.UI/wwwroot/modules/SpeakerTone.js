/**
 * The tone the speaker step plays, worked out before it is played, and what the
 * two verdicts together come to.
 *
 * Both are here rather than inside SpeakerTest because both are the part of the
 * step that can be checked without a phone and a pair of ears.
 *
 * The tone is a plan and not a pile of calls into the audio context for two
 * reasons.
 *
 * The first is that the sequence has to be reproducible to be checked. Frequencies
 * and timings are the whole content of a speaker test, and "it makes a noise" is
 * not a thing anyone can assert. Spelled out here, a test can prove the highest
 * note is within hearing range, that nothing is scheduled after the step reports
 * it is finished, and that the gain never asks for more than 1.0.
 *
 * The second is that the original sequence had a click in it. Every note ramped
 * down to 0.001 and the oscillator was stopped 50 ms later, so each bell ended on
 * a small step rather than on silence, and five of those in a row is the kind of
 * buzz an operator reports as a defective speaker. It is planned here, ramped all
 * the way down and stopped after the ramp, so the sequence ends on silence.
 */
import { t } from './i18n.js';

/** C5, E5, G5, B5, C6. A major arpeggio, which is easy to recognise as music. */
export const CHIME_NOTES = [523.25, 659.25, 783.99, 987.77, 1046.50];

/** Gap between one bell dying and the next striking. */
export const NOTE_GAP_S = 0.22;

/** How long one bell takes to fade. */
export const NOTE_DECAY_S = 0.42;

/** The quietest that counts as faded. Not zero: a ramp to zero is illegal. */
const SILENCE = 0.0001;

/**
 * The schedule for one pass of the chime.
 *
 * @param {number} volume 0 to 1, the peak of the loudest note
 * @param {number} startTime the audio clock value to start at
 * @returns {{events: Array, durationMs: number}}
 */
export function planChime(volume, startTime = 0) {
    const peak = clamp(volume, 0, 1) * 0.7;
    const events = [];

    CHIME_NOTES.forEach((frequency, index) => {
        const at = startTime + (index * NOTE_GAP_S);
        events.push({
            frequency,
            startAt: at,
            // Short fade in. An instant rise from silence is itself a click.
            attackAt: at + 0.02,
            peak,
            // Faded out before the oscillator is stopped, so the stop lands on
            // silence rather than on a step up from 0.001 to 0.
            releaseAt: at + NOTE_DECAY_S,
            stopAt: at + NOTE_DECAY_S + 0.01
        });
    });

    const last = events[events.length - 1];
    return {
        events,
        // The last bell is the end of the sequence, not the sum of the gaps, so
        // the step does not report itself finished while a bell is still ringing.
        durationMs: Math.ceil((last.stopAt - startTime) * 1000)
    };
}

/**
 * How loud a section is played.
 *
 * The earpiece is a few millimetres across behind a mesh, and the original 0.12
 * was quiet enough to be missed in a noisy shop, which then got recorded as a
 * dead earpiece. The loudspeaker is a different order of magnitude of output, so
 * it is played lower than it could be: at full scale the last note is loud enough
 * that the operator stops listening properly, which is the other way to get a
 * false pass.
 */
export const SECTION_VOLUMES = {
    earpiece: 0.45,
    loudspeaker: 0.7
};

/** True when the frequencies a phone can actually reproduce are in the sequence. */
export function isAudible(chime) {
    return chime.events.some(e => e.frequency >= 200 && e.frequency <= 10000);
}

/** True when nothing is still sounding after the sequence is reported as done. */
export function isSilentAtEnd(chime) {
    const end = chime.durationMs / 1000;
    return chime.events.every(e => e.stopAt <= end);
}

/**
 * True when the sequence asks for something the mix bus can give.
 *
 * A peak of zero counts as out of range on purpose. A chime at zero volume is not
 * a quiet chime, it is a test that cannot tell a working speaker from a dead one,
 * and the step has to treat it as a tone it failed to produce rather than as a
 * pass. Out of range in both directions: too loud clips, too quiet tests nothing.
 */
export function isWithinRange(chime) {
    return chime.events.every(e => e.peak > 0 && e.peak <= 1);
}

function clamp(value, low, high) {
    if (!Number.isFinite(value)) return low;
    return Math.min(high, Math.max(low, value));
}

/**
 * What the two verdicts together mean.
 *
 * Split out because the wording is the part of this step that ends up on a
 * grading label, and the three cases have to stay apart. "The earpiece works and
 * the loudspeaker does not" is a phone someone can still take calls on. "Neither
 * works" is a phone nobody can. Collapsing them to a single "audio defective"
 * throws away the only thing the report can tell the next person.
 *
 * The plays count is in here too, because it is the difference between a speaker
 * that was tested once while the volume was still low and one that was tested
 * properly. A pass recorded on the first attempt, right after a warning about
 * volume that the operator may not have acted on, is a different claim from one
 * recorded after listening twice, and the label should say which it was.
 *
 * @param {{earpiece: boolean|null, loudspeaker: boolean|null}} verdicts
 * @param {{earpiece: number, loudspeaker: number}} plays
 */
export function describeOutcome(verdicts, plays = {}) {
    const { earpiece, loudspeaker } = verdicts;

    const earpiecePlays = plays.earpiece || 0;
    const loudspeakerPlays = plays.loudspeaker || 0;

    // A section that was never judged is not a working one. Recording silence as
    // a pass is the worst of the three mistakes available here, and it is what a
    // step that was skipped halfway through would otherwise do.
    if (earpiece === null || loudspeaker === null) {
        const missing = [
            earpiece === null ? t('speaker.earpieceLabelLower') : null,
            loudspeaker === null ? t('speaker.loudspeakerLabelLower') : null
        ].filter(Boolean);

        return {
            passed: false,
            notes: t('speaker.notJudged', { missing: missing.join(t('speaker.joinAnd')) }),
            incomplete: true
        };
    }

    const heard = [];
    const dead = [];
    if (earpiece) heard.push(t('speaker.earpieceLabel')); else dead.push(t('speaker.earpieceLabel'));
    if (loudspeaker) heard.push(t('speaker.loudspeakerLabel')); else dead.push(t('speaker.loudspeakerLabel'));

    let notes;
    if (dead.length === 0) {
        notes = t('speaker.allWorking', {
            list: heard.join(t('speaker.joinAnd')),
            verb: heard.length === 1 ? t('speaker.verbWorks') : t('speaker.verbWork')
        });
    } else if (heard.length === 0) {
        notes = t('speaker.noneWork');
    } else {
        notes = t('speaker.someWorkSomeSilent', {
            working: heard.join(t('speaker.joinAnd')),
            dead: dead.join(t('speaker.joinAnd'))
        });
    }

    // Named only when the operator needed more than one go at it. A pass recorded
    // on the second listen is not the same claim as one recorded on the first,
    // and it is the first that gets argued about later.
    const retried = (earpiecePlays > 1 || loudspeakerPlays > 1)
        ? t('speaker.replays', { earpiece: earpiecePlays, loudspeaker: loudspeakerPlays })
        : '';

    return {
        passed: dead.length === 0,
        notes: notes + retried,
        incomplete: false
    };
}
