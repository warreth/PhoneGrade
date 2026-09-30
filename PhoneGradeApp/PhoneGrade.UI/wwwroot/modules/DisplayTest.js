import { DeviceTest } from './DeviceTest.js';
import {
    INSPECTION_COLORS,
    VERDICT_OK,
    VERDICT_DEFECTIVE,
    createInspection,
    recordVerdict,
    verdictOf,
    defectiveColorNames,
    isDisplayFaulty,
    describeDefects
} from './DisplayInspection.js';

/** What the bar answers when the operator wants the colour before this one. */
const BACK = 'back';

/**
 * DisplayTest: the panel looked at one pure colour at a time.
 *
 * The screen is filled with white, then red, green, blue and black, and the
 * operator says whether that colour is right. "Slecht" records the colour as
 * defective and moves on to the next one: a colour that is wrong is a reason to
 * look at the other four, not a reason to stop, so all five are always checked.
 *
 * An earlier version framed the panel into a grid of patches and asked for a
 * verdict per patch per colour. Sixty answers to a question the operator could
 * not reliably answer is not more detail, it is noise, and the run took an age
 * to get through. One question per colour is as far as a human looking at a
 * screen can honestly go, and it is enough to say which colour the fault is on.
 */
export class DisplayTest extends DeviceTest {
    constructor() {
        super('display', 'Display & Dead Pixels', 'Inspect each patch of the screen for dead and stuck pixels');
        this.colors = INSPECTION_COLORS;
        this.inspection = createInspection();
        this.currentIndex = 0;
        this.overlay = null;
    }

    reset() {
        super.reset();
        this.inspection = createInspection();
        this.currentIndex = 0;
        this.removeOverlay();
    }

    /**
     * Takes the whole overlay off the screen.
     *
     * The overlay is appended to document.body, not to the test container, because
     * a full-screen fill has to be able to sit above everything. That makes it the
     * one thing on the page the runner cannot clear by emptying the container, so
     * a skip or the failsafe in the middle of a colour left a full-screen block of
     * red over the next step, with no way past it but reloading.
     */
    removeOverlay() {
        if (this.overlay && document.body.contains(this.overlay)) {
            document.body.removeChild(this.overlay);
        }
        this.overlay = null;
    }

    dispose() {
        this.removeOverlay();
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting display inspection...');

        // Asked until it is confirmed, never failed on.
        //
        // A phone sitting at a third brightness is not a phone with a broken
        // display, it is a phone whose brightness has not been turned up yet. The
        // old version recorded "scherm niet helder genoeg" as a display failure,
        // so a phone in a dim second-hand shop could be graded faulty for it and
        // the owner would be the one paying. The way out is the hold-to-skip
        // button the runner already shows on every step, and that records the
        // step as skipped, which is what it is.
        //
        // This does not return on skip. The runner settles the step, records it as
        // skipped and moves on, and the promise below is simply left pending. That
        // is the point: a pending promise cannot be mistaken for a verdict.
        await this.showBrightnessCheck(container);
        this.details.brightnessLevel = 'Bevestigd door de technicus';

        let index = 0;
        while (index < this.colors.length) {
            const color = this.colors[index];
            this.currentIndex = index;
            this.reportProgress(wsClient, (index / this.colors.length) * 100, `Kleur: ${color.name}`);

            const answer = await this.showColor(color, index);

            // Back changes no verdict, only where the operator is looking, so the
            // colour they left is still there when they come back to it.
            if (answer === BACK) {
                index = Math.max(0, index - 1);
                continue;
            }

            recordVerdict(this.inspection, color.id, answer);
            this.haptic.tap();

            // Counted after the verdict was stored, so the desktop is told the
            // colour is done rather than one step behind it for the whole run.
            this.reportProgress(
                wsClient,
                ((index + 1) / this.colors.length) * 100,
                `${color.name}: ${answer === VERDICT_DEFECTIVE ? 'afwijking gemeld' : 'goed'}`,
                this.id,
                `${this.name} (${color.name})`);

            index += 1;
        }

        const faulty = isDisplayFaulty(this.inspection);

        this.details.colorsChecked = this.colors.map(c => c.name);
        this.details.defectiveColors = defectiveColorNames(this.inspection);
        this.details.suspectedFaulty = faulty;

        if (faulty) {
            this.fail(describeDefects(this.inspection));
        } else {
            this.pass(describeDefects(this.inspection));
        }
    }

    /**
     * Asks for full brightness and does not let go until it is confirmed.
     *
     * The old card showed a pure white panel and a pure black panel side by side
     * on the screen being judged and asked whether the screen was bright. One
     * screen cannot be at 100% and 0% at once, so the question had no answer, and
     * the only two buttons were "yes, the display works" and "no, fail the
     * display". Asking again, with nothing on the screen to confuse the answer,
     * is the fix.
     */
    showBrightnessCheck(container) {
        return new Promise((resolve) => {
            const render = () => {
                container.innerHTML = `
                    <div class="check-overlay">
                        <div class="check-card">
                            <h3 class="check-title">Eerst de helderheid</h3>
                            <p class="check-text">
                                Zet de schermhelderheid op maximaal. Trek de snelle
                                instellingen omlaag en zet de zonnewijzer op het
                                maximum. Bij een te donker scherm is een
                                vastzittend pixel niet te onderscheiden van een
                                schaduw, en dan valt niets te beoordelen.
                            </p>
                            <p class="check-question">
                                Staat de helderheid op maximaal, en is de
                                scherminhoud niet te donker om de kleuren hieronder te
                                herkennen?
                            </p>
                            <div class="check-actions">
                                <button id="brightness-ok" class="btn btn-success">Ja, klaar</button>
                                <button id="brightness-retry" class="btn btn-secondary">Ik controleer het nogmaals</button>
                            </div>
                        </div>
                    </div>
                `;

                const btnOk = container.querySelector('#brightness-ok');
                const btnRetry = container.querySelector('#brightness-retry');

                btnOk.addEventListener('click', () => resolve(true));

                // Not a failure, and not a no-op either: it puts the card back up
                // and resets the highlight, so the operator can tell that nothing
                // was recorded and try again.
                btnRetry.addEventListener('click', () => {
                    this.haptic.tap();
                    render();
                });
            };

            render();
        });
    }

    /**
     * One colour, full screen, with one question over it.
     *
     * Goed and Slecht both move on. A colour marked Slecht is recorded and the
     * loop carries on to the next one, because the fault might be on every colour
     * or only on this one and stopping would leave the rest unlooked at. Vorige is
     * there for a button pressed by mistake: it changes nothing but where the
     * operator is standing, and the colour before still holds its last verdict.
     *
     * Resolves with the verdict, or with BACK. The bar itself records nothing;
     * run() does, so there is one place where a colour can end up marked.
     *
     * @returns {Promise<string>} VERDICT_OK, VERDICT_DEFECTIVE or BACK
     */
    showColor(color, index) {
        return new Promise((resolve) => {
            const overlay = document.createElement('div');
            overlay.className = 'display-test-fullscreen';
            overlay.style.background = color.hex;
            document.body.appendChild(this.overlay = overlay);

            const verdict = verdictOf(this.inspection, color.id);
            const active = (wanted) => verdict === wanted ? 'is-active' : '';

            overlay.innerHTML = `
                <div class="display-bar">
                    <div class="display-bar-title">
                        <span class="display-color-name">${color.name}</span>
                        <span class="display-progress">Kleur ${index + 1} van ${this.colors.length}</span>
                    </div>
                    <p class="display-hint">Afwijking gezien? Kies Slecht, anders Goed.</p>
                    <div class="display-bar-actions">
                        ${index > 0 ? `<button id="color-prev" class="btn btn-secondary">Vorige</button>` : ''}
                        <button id="color-ok" class="btn btn-success ${active(VERDICT_OK)}">Goed</button>
                        <button id="color-defect" class="btn btn-danger ${active(VERDICT_DEFECTIVE)}">Slecht</button>
                    </div>
                </div>
            `;

            // Off the screen before the promise settles, so the next colour starts
            // on a clean panel rather than underneath this one.
            const finish = (answer) => {
                this.removeOverlay();
                resolve(answer);
            };

            overlay.querySelector('#color-prev')?.addEventListener('click', (e) => {
                e.stopPropagation();
                finish(BACK);
            });

            overlay.querySelector('#color-ok').addEventListener('click', (e) => {
                e.stopPropagation();
                finish(VERDICT_OK);
            });

            overlay.querySelector('#color-defect').addEventListener('click', (e) => {
                e.stopPropagation();
                finish(VERDICT_DEFECTIVE);
            });
        });
    }
}
