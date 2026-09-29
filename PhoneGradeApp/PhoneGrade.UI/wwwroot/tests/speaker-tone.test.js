import test from 'node:test';
import assert from 'node:assert/strict';

import {
    planChime,
    isAudible,
    isSilentAtEnd,
    isWithinRange,
    describeOutcome,
    SECTION_VOLUMES,
    CHIME_NOTES
} from '../modules/SpeakerTone.js';

test('the chime is a rising arpeggio, so it is recognisable as a fault pattern', () => {
    // A flat tone or a noise burst is much harder to tell from a rattle in the
    // housing. A short rising run of notes is a pattern an operator can name.
    const { events } = planChime(0.5);

    assert.equal(events.length, CHIME_NOTES.length);
    for (let i = 1; i < events.length; i++) {
        assert.ok(events[i].frequency > events[i - 1].frequency,
            `note ${i} should be higher than note ${i - 1}`);
    }
});

test('every note lands inside the range a small earpiece can reproduce', () => {
    // A 5 mm mylar driver falls off hard below about 300 Hz and above 6 kHz.
    // Notes outside that are not testing the speaker, they are testing nothing.
    const { events } = planChime(0.5);

    for (const note of events) {
        assert.ok(note.frequency >= 300, `${note.frequency} Hz is below an earpiece`);
        assert.ok(note.frequency <= 6000, `${note.frequency} Hz is above an earpiece`);
    }

    assert.equal(isAudible(planChime(0.5)), true);
});

test('the sequence is silent by the time it reports itself finished', () => {
    // The old schedule stopped each oscillator 50 ms after a ramp that only reached
    // 0.001, so every bell ended on a step. Five of those in a row is the buzz an
    // operator reports as a dead speaker.
    const chime = planChime(0.5);
    const endAt = chime.durationMs / 1000;

    for (const note of chime.events) {
        assert.ok(note.stopAt <= endAt + 1e-9,
            `note at ${note.startAt} is still sounding at ${endAt}`);
        assert.ok(note.releaseAt <= note.stopAt,
            'a note must be faded out before it is stopped, or the stop is a click');
    }

    assert.equal(isSilentAtEnd(chime), true);
});

test('the gain never asks the mix bus for more than it has', () => {
    for (const volume of [0.45, 0.7, 1, 4]) {
        const chime = planChime(volume);

        for (const note of chime.events) {
            assert.ok(note.peak <= 1, `peak ${note.peak} at volume ${volume}`);
        }

        assert.equal(isWithinRange(chime), true);
    }
});

test('a volume that would produce silence is refused rather than played', () => {
    // A chime at zero volume cannot tell a working speaker from a dead one, so
    // planning one is a bug and the step has to notice it. A loud request is
    // clamped instead, because clipping is a matter of degree.
    assert.equal(isWithinRange(planChime(0)), false);
    assert.equal(isWithinRange(planChime(-1)), false);
    assert.equal(isWithinRange(planChime(NaN)), false);
    assert.equal(isWithinRange(planChime(4)), true);
});

test('each note fades in rather than appearing at full level', () => {
    // A rise from silence in one step is itself a click, and it is what makes a
    // sequence sound harsh at high volume.
    const { events } = planChime(0.7);

    for (const note of events) {
        assert.ok(note.attackAt > note.startAt, 'the fade in has to take time');
        assert.ok(note.attackAt < note.releaseAt);
    }
});

test('the two sections are played at different levels', () => {
    // The earpiece is played well above the 0.12 it used to get. At 0.12 a working
    // earpiece in a noisy shop is missed, and the operator answers "nothing", which
    // the old step then recorded as a dead earpiece with no way to check.
    assert.ok(SECTION_VOLUMES.earpiece >= 0.4,
        'the earpiece has to be loud enough to be heard over a shop floor');
    assert.ok(SECTION_VOLUMES.loudspeaker > SECTION_VOLUMES.earpiece,
        'the loudspeaker has more output than the earpiece and needs less drive');
    assert.ok(SECTION_VOLUMES.loudspeaker <= 0.8,
        'driven hard the operator stops listening, which is how a pass gets faked');
});

test('the step runs in about a second and a half, not a minute', () => {
    const chime = planChime(0.5, 100);
    const span = chime.durationMs / 1000;

    // Long enough to follow as music, short enough that a second listen costs
    // nothing. This is the cost of the replay button, so it is worth knowing.
    assert.ok(span > 1.0 && span < 2.0, `sequence was ${span}s`);

    // The reported length is rounded up to a whole millisecond so the last note is
    // never cut short, which means the wait can sit up to 1 ms past the stop.
    const last = chime.events[chime.events.length - 1];
    const overshoot = span + 100 - last.stopAt;
    assert.ok(overshoot >= 0, 'the wait must cover the whole sequence');
    assert.ok(overshoot < 0.0011, `the wait overshoots by ${overshoot}s`);
});

test('both speakers working is a pass that says so', () => {
    const outcome = describeOutcome({ earpiece: true, loudspeaker: true });

    assert.equal(outcome.passed, true);
    assert.equal(outcome.incomplete, false);
    assert.match(outcome.notes, /Oorluidspreker en Hoofdluidspreker/);
    assert.doesNotMatch(outcome.notes, /afgespeeld/, 'no need to mention a single play');
});

test('one dead speaker is named, not averaged away', () => {
    // This is the case the old wording lost. A phone that cannot be heard on
    // speaker but works on the earpiece is a phone someone can still take calls
    // on, and the report has to be able to say which is which.
    const loud = describeOutcome({ earpiece: true, loudspeaker: false });
    assert.equal(loud.passed, false);
    assert.match(loud.notes, /Oorluidspreker werkt goed/);
    assert.match(loud.notes, /Hoofdluidspreker geeft geen geluid/);

    const ear = describeOutcome({ earpiece: false, loudspeaker: true });
    assert.equal(ear.passed, false);
    assert.match(ear.notes, /Hoofdluidspreker werkt goed/);
    assert.match(ear.notes, /Oorluidspreker geeft geen geluid/);
});

test('two dead speakers are not the same as one', () => {
    const both = describeOutcome({ earpiece: false, loudspeaker: false });

    assert.equal(both.passed, false);
    assert.match(both.notes, /Geen geluid uit de oorluidspreker of de hoofdluidspreker/);
});

test('a section nobody judged is not a section that failed', () => {
    // A step abandoned after the earpiece, by a skip or the failsafe, used to leave
    // the loudspeaker at false and put "no audio" on the label for a phone whose
    // only problem was that nobody got to it.
    const onlyEarpiece = describeOutcome({ earpiece: true, loudspeaker: null });
    assert.equal(onlyEarpiece.passed, false);
    assert.equal(onlyEarpiece.incomplete, true);
    assert.match(onlyEarpiece.notes, /Niet beoordeeld: hoofdluidspreker/);

    const nothing = describeOutcome({ earpiece: null, loudspeaker: null });
    assert.equal(nothing.incomplete, true);
    assert.match(nothing.notes, /oorluidspreker en hoofdluidspreker/);
});

test('a verdict reached after a second listen says so', () => {
    // A pass recorded on the first play, right after a warning about the volume
    // that the operator may not have acted on, is a weaker claim than one recorded
    // after listening twice. The label is where that difference has to show up.
    const first = describeOutcome({ earpiece: true, loudspeaker: true }, { earpiece: 1, loudspeaker: 1 });
    assert.doesNotMatch(first.notes, /afgespeeld/);

    const second = describeOutcome({ earpiece: true, loudspeaker: true }, { earpiece: 2, loudspeaker: 1 });
    assert.equal(second.passed, true);
    assert.match(second.notes, /toon 2x en 1x afgespeeld/);
});

test('the retry count survives a failure verdict too', () => {
    // It cuts the other way as well: a speaker that was played three times and
    // still silent is a better-established failure than one that was given one go.
    const outcome = describeOutcome({ earpiece: false, loudspeaker: true }, { earpiece: 3, loudspeaker: 1 });

    assert.equal(outcome.passed, false);
    assert.match(outcome.notes, /toon 3x en 1x afgespeeld/);
});

test('a missing play count is not read as zero plays to complain about', () => {
    // describeOutcome is called with a default, and a caller that forgets the
    // counts should not get a note that says "0x afgespeeld".
    const outcome = describeOutcome({ earpiece: true, loudspeaker: true });

    assert.doesNotMatch(outcome.notes, /0x/);
});
