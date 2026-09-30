import { t } from './i18n.js';
import { DeviceTest } from './DeviceTest.js';
import {
    planChime,
    isAudible,
    isWithinRange,
    describeOutcome,
    SECTION_VOLUMES
} from './SpeakerTone.js';

/**
 * The earpiece and the loudspeaker, judged by ear.
 *
 * Both halves used to be one-shot. The play button disabled itself, vanished when
 * the tone finished, and the verdict buttons appeared once and then hid
 * themselves again the moment they were pressed. That left the operator with a
 * single chance to get it right, and a single chance is not enough for something
 * judged by listening: a missed tone while the volume was still coming up reads
 * exactly like a dead earpiece, and the verdict it produced was final.
 *
 * So the play button stays, the verdict buttons stay, and a verdict can be
 * changed. The tone can be played as many times as the operator wants before
 * answering, and how many times it was played is recorded, because a pass
 * reached on the second listen is not the same claim as one reached on the first.
 *
 * A tone that could not be produced is reported as such. If the audio context
 * will not start, or the destination will not accept a source, the honest note
 * is that no tone was played, and the operator is asked to try again. Blaming the
 * loudspeaker for a failure in the browser would put a fault on a grading label
 * that is not in the phone.
 */
export class SpeakerTest extends DeviceTest {
    /** How long to wait for the audio context to wake before giving up on it. */
    static RESUME_TIMEOUT_MS = 1000;

    constructor() {
        super('speaker', t('speaker.stepName'), t('speaker.stepDescription'));
        this.audioContext = null;

        // Null until judged, not false. A section nobody has answered is not a
        // section that failed, and the difference is what keeps a step that was
        // abandoned halfway from being reported as a dead speaker.
        this.earpieceWorking = null;
        this.loudspeakerWorking = null;

        this.plays = { earpiece: 0, loudspeaker: 0 };
        this.audioProblem = null;
    }

    reset() {
        super.reset();
        this.earpieceWorking = null;
        this.loudspeakerWorking = null;
        this.plays = { earpiece: 0, loudspeaker: 0 };
        this.audioProblem = null;
    }

    /**
     * Closes the audio context.
     *
     * Called by the runner on every exit, including a skip and the failsafe. A
     * context left open keeps the audio hardware awake, and on a phone that shows
     * up as the operator reaching the next step and finding the volume slider not
     * doing anything.
     */
    dispose() {
        if (!this.audioContext) return;

        try { this.audioContext.close(); } catch (e) { /* already closed */ }
        this.audioContext = null;
    }

    /**
     * The two speaker cards.
     *
     * The layout is inert on arrival and gets wired up section by section, so
     * building it up front is what lets the sections exist without being live.
     * Both cards are rendered at once but the second starts locked: a phone has to
     * be held against the ear for one test and flat on a table for the other, and
     * asking for both in the same position tests one of them in a place it cannot
     * work.
     */
    markup() {
        return `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('speaker.audioHeading')}</h3>

                    <div class="step-warning">
                        ${t('speaker.volumeWarning')}
                    </div>

                    <div id="earpiece-section" class="audio-section">
                        <p class="audio-section-title">${t('speaker.earpieceCardTitle')}</p>
                        <p class="audio-section-hint">${t('speaker.earpieceCardHint')}</p>
                        <button id="play-earpiece-btn" class="btn btn-primary">${t('speaker.playToneButton')}</button>
                        <div id="earpiece-feedback" class="audio-verdicts" hidden>
                            <button id="earpiece-yes" class="btn btn-success">${t('speaker.clearlyHeardButton')}</button>
                            <button id="earpiece-no" class="btn btn-danger">${t('speaker.nothingOrDistortedButton')}</button>
                        </div>
                        <p id="earpiece-status" class="audio-status">${t('speaker.notPlayedYetStatus')}</p>
                    </div>

                    <div id="loudspeaker-section" class="audio-section is-locked">
                        <p class="audio-section-title">${t('speaker.loudspeakerCardTitle')}</p>
                        <p class="audio-section-hint">${t('speaker.loudspeakerCardHint')}</p>
                        <button id="play-loud-btn" class="btn btn-primary">${t('speaker.playToneButton')}</button>
                        <div id="loud-feedback" class="audio-verdicts" hidden>
                            <button id="loud-yes" class="btn btn-success">${t('speaker.clearlyHeardButton')}</button>
                            <button id="loud-no" class="btn btn-danger">${t('speaker.nothingOrDistortedButton')}</button>
                        </div>
                        <p id="loud-status" class="audio-status">${t('speaker.notPlayedYetStatus')}</p>
                    </div>
                </div>
            </div>
        `;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('speaker.preparingProgress'));

        container.innerHTML = this.markup();

        await this.walk(wsClient, container);

        this.reportProgress(wsClient, 100, t('speaker.finishedProgress'));

        const outcome = describeOutcome(
            { earpiece: this.earpieceWorking, loudspeaker: this.loudspeakerWorking },
            this.plays);

        this.details.earpiece = this.earpieceWorking;
        this.details.loudspeaker = this.loudspeakerWorking;
        this.details.earpiecePlays = this.plays.earpiece;
        this.details.loudspeakerPlays = this.plays.loudspeaker;
        this.details.audioProblem = this.audioProblem;

        if (outcome.passed) {
            this.pass(outcome.notes);
        } else {
            this.fail(outcome.notes);
        }
    }

    /**
     * Earpiece first, then loudspeaker.
     *
     * The second half stays locked until the first is answered, because the phone
     * has to be held against the ear for one and flat on a table for the other.
     * Doing both at once means one of the two is always judged in the wrong
     * position.
     */
    async walk(wsClient, container) {
        await this.askAbout(container, 'earpiece');
        await this.askAbout(container, 'loudspeaker');
    }

    /**
     * Wires up one speaker card and resolves once it has been judged.
     *
     * The play button is deliberately never disabled and never hidden. It used to
     * disable itself and then set display none when the tone finished, and the
     * verdict buttons appeared once and hid themselves again the moment they were
     * pressed. That left one chance to get it right on something judged by
     * listening, and a tone missed while the volume was still coming up reads
     * exactly like a dead speaker.
     *
     * So the button stays and the verdicts stay. The operator can play the tone as
     * often as they like before answering, and can change their mind afterwards.
     */
    askAbout(container, section) {
        return new Promise((resolve) => {
            const isEarpiece = section === 'earpiece';
            const prefix = isEarpiece ? 'earpiece' : 'loud';
            const volume = isEarpiece ? SECTION_VOLUMES.earpiece : SECTION_VOLUMES.loudspeaker;

            const playBtn = container.querySelector(`#play-${isEarpiece ? 'earpiece' : 'loud'}-btn`);
            const feedback = container.querySelector(`#${prefix}-feedback`);
            const yesBtn = container.querySelector(`#${prefix}-yes`);
            const noBtn = container.querySelector(`#${prefix}-no`);
            const status = container.querySelector(`#${prefix}-status`);

            if (!playBtn || !feedback || !yesBtn || !noBtn || !status) {
                // Wiring up half a card would leave buttons that do nothing, which
                // is worse than no step at all: the operator would sit there
                // pressing a dead button with no indication why.
                console.warn(`SpeakerTest: markup for section '${section}' is incomplete.`);
                resolve();
                return;
            }

            const renderVerdict = () => {
                const value = isEarpiece ? this.earpieceWorking : this.loudspeakerWorking;
                yesBtn.classList.toggle('is-active', value === true);
                noBtn.classList.toggle('is-active', value === false);
            };

            const setStatus = (text, kind) => {
                status.textContent = text;
                status.className = 'audio-status' + (kind ? ` is-${kind}` : '');
            };

            const play = async () => {
                const started = await this.playTone(volume);

                if (!started) {
                    // Not a verdict on the speaker. The browser would not give us
                    // a tone, so nothing has been tested yet, and the label has to
                    // say that rather than blame the hardware.
                    this.audioProblem = t('speaker.noToneFromBrowser');
                    setStatus(t('speaker.noToneStatus'), 'error');
                    return;
                }

                this.plays[section] += 1;
                this.audioProblem = null;

                // The verdict buttons appear on the first successful play and stay
                // there, so the operator can listen again before committing.
                feedback.hidden = false;

                const plays = this.plays[section];
                setStatus(plays === 1
                    ? t('speaker.tonePlayedOnce')
                    : t('speaker.tonePlayedTimes', { plays }));
            };

            playBtn.addEventListener('click', play);

            // Tracked separately, because they are two different things. Unlocking
            // happens on the first answer of any kind: a "nothing heard" verdict on
            // the earpiece is a real result, and the operator still has to be able
            // to carry on and test the loudspeaker. Settling happens once, on the
            // first answer, because the runner is waiting on it and cannot be
            // un-waited. A later change to the verdict is still recorded.
            let unlocked = false;
            let settled = false;

            const answer = (value) => {
                if (isEarpiece) this.earpieceWorking = value;
                else this.loudspeakerWorking = value;

                renderVerdict();
                setStatus(value ? t('speaker.verdictHeardWell') : t('speaker.verdictNoSound'), value ? 'success' : 'error');

                this.haptic.tap();

                if (isEarpiece && !unlocked) {
                    const next = container.querySelector('#loudspeaker-section');
                    if (next) next.classList.remove('is-locked');
                    unlocked = true;
                }

                if (settled) return;
                settled = true;
                resolve();
            };

            yesBtn.addEventListener('click', () => answer(true));
            noBtn.addEventListener('click', () => answer(false));
        });
    }

    /**
     * Creates or wakes the audio context, then plays one pass of the chime.
     *
     * The context is built inside the click handler because a browser will not
     * let audio start without a gesture, and it is awaited before the notes are
     * scheduled: a context that is still suspended when the notes are booked
     * starts them late, or not at all, and the operator hears silence through no
     * fault of the speaker.
     *
     * @returns {Promise<boolean>} whether a tone was actually produced
     */
    async playTone(volume) {
        try {
            if (!this.audioContext) {
                const Ctor = window.AudioContext || window.webkitAudioContext;
                if (!Ctor) return false;
                this.audioContext = new Ctor();
            }

            if (this.audioContext.state === 'suspended') {
                // Bounded, and the state is checked afterwards rather than trusted
                // from the promise.
                //
                // Measured on a real Pixel over the secure origin: resume() does
                // not always settle. When the click does not carry a user
                // activation, the promise simply never resolves, and an unbounded
                // await here means playTone never returns, the status line never
                // changes, no verdict buttons appear, and the operator is left
                // pressing a button that does nothing until the 90 s failsafe ends
                // the step. A refusal has to be able to come back as "no tone", not
                // as nothing at all.
                await Promise.race([
                    this.audioContext.resume().catch(() => { /* refused, caught by the state check below */ }),
                    new Promise(resolve => setTimeout(resolve, SpeakerTest.RESUME_TIMEOUT_MS))
                ]);
            }

            if (this.audioContext.state !== 'running') {
                // Suspended, closed, or interrupted. Either way nothing is coming
                // out, and the operator has to be told so before they are asked
                // whether they heard anything.
                return false;
            }

            const chime = planChime(volume, this.audioContext.currentTime);

            // Both checks are about whether anything came out, not about how it
            // sounded. A sequence that planned itself silent would otherwise be
            // played and reported to the operator as a tone to judge, and the
            // verdict they give on silence is the one that puts a fault on the
            // label.
            if (!isAudible(chime) || !isWithinRange(chime)) return false;

            for (const note of chime.events) {
                this.scheduleNote(note);
            }

            // Waits for the whole sequence rather than a guess at its length, so
            // the verdict buttons appear when the last bell has died and not while
            // it is still ringing.
            await new Promise(r => setTimeout(r, chime.durationMs));
            return true;
        } catch (e) {
            console.warn('Speaker tone failed:', e);
            return false;
        }
    }

    scheduleNote(note) {
        const ctx = this.audioContext;

        const osc = ctx.createOscillator();
        const gain = ctx.createGain();

        osc.type = 'sine';
        osc.frequency.setValueAtTime(note.frequency, note.startAt);

        gain.gain.setValueAtTime(0, note.startAt);
        gain.gain.linearRampToValueAtTime(note.peak, note.attackAt);
        gain.gain.exponentialRampToValueAtTime(0.0001, note.releaseAt);

        osc.connect(gain);
        gain.connect(ctx.destination);

        osc.start(note.startAt);
        osc.stop(note.stopAt);
    }
}
