import { DeviceTest } from './DeviceTest.js';
import {
    INSPECTION_COLORS,
    gridColorFor,
    patchLabel,
    patchRect,
    nextPatch,
    previousPatch,
    createInspection,
    setVerdict,
    getVerdict,
    inspectedCount,
    describeDefects,
    isDisplayFaulty
} from './DisplayInspection.js';

/**
 * DisplayTest: the panel inspected as a grid of magnified patches, under each
 * pure colour, with a verdict recorded per patch.
 *
 * The previous version showed one flat colour full screen and asked the operator
 * to tap through five of them and flag anything wrong. That is not a workable way
 * to find a dead pixel: a stuck subpixel is smaller than the finger resolution of
 * the eye at arm's length, so "does this screen look right" is a question with no
 * answer. It also recorded a single verdict for the whole display, so one stuck
 * pixel in a corner and a dead column across the middle produced the same note.
 *
 * The inspection is now per patch, so the operator looks at a small region at a
 * time and says what they see, and the label ends up saying where the defects are
 * rather than only that the display is suspect.
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
     * a magnified patch has to be able to sit above everything. That makes it the
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

        for (let i = 0; i < this.colors.length; i++) {
            this.currentIndex = i;
            const color = this.colors[i];
            this.reportProgress(wsClient, (i / this.colors.length) * 100, `Kleur: ${color.name}`);

            await this.inspectColor(wsClient, color);
        }

        const faulty = isDisplayFaulty(this.inspection);

        this.details.colorsChecked = this.colors.map(c => c.name);
        this.details.patchesInspected = inspectedCount(this.inspection, this.colors[0].id);
        this.details.defectNote = describeDefects(this.inspection);
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
     * Walks the grid under one colour, one patch at a time.
     *
     * The screen is one flat fill with a frame on the patch under attention, and
     * a fine reference matrix inside that frame. The matrix is not a
     * magnification: the page has no way to read the panel's own pixels, so it
     * cannot enlarge what the screen is emitting. What it does is show the
     * operator the size of the thing being looked for, which is exactly what a
     * flat rectangle of colour with no frame fails to convey.
     */
    async inspectColor(wsClient, color) {
        const { cols, rows, total } = this.inspection;
        let patch = 0;

        return new Promise((resolve) => {
            const overlay = document.createElement('div');
            overlay.className = 'display-test-fullscreen';
            overlay.style.background = color.hex;
            document.body.appendChild(this.overlay = overlay);

            const render = () => {
                const verdict = getVerdict(this.inspection, color.id, patch);
                const done = inspectedCount(this.inspection, color.id);
                const gridColor = gridColorFor(color.hex);
                const rect = patchRect(patch, cols, rows);

                const cells = new Array(64).fill('<span></span>');

                overlay.innerHTML = `
                    <div class="display-patch"
                         style="left:${rect.x0 * 100}%;
                                top:${rect.y0 * 100}%;
                                width:${rect.width * 100}%;
                                height:${rect.height * 100}%;
                                border-color:${gridColor};">
                        <div class="display-loupe" style="color:${gridColor};">${cells.join('')}</div>
                    </div>
                    <div class="display-bar">
                        <div class="display-bar-title">
                            <span class="display-color-name">${color.name}</span>
                            <span class="display-progress">${done} van ${total} vakken</span>
                        </div>
                        <div class="display-patch-label">${patchLabel(patch, cols)}</div>
                        <div class="display-bar-actions">
                            <button id="patch-defect" class="btn btn-danger ${verdict === 'defect' ? 'is-active' : ''}">Vastzittend pixel</button>
                            <button id="patch-ok" class="btn btn-success ${verdict === 'ok' ? 'is-active' : ''}">Geen afwijking</button>
                        </div>
                        <div class="display-bar-nav">
                            <button id="patch-prev" class="btn btn-secondary">Vorige</button>
                            <button id="patch-next" class="btn btn-secondary">Volgende vak</button>
                            <button id="color-done" class="btn btn-secondary">Kleur klaar</button>
                        </div>
                    </div>
                `;

                const defectBtn = overlay.querySelector('#patch-defect');
                const okBtn = overlay.querySelector('#patch-ok');
                const prevBtn = overlay.querySelector('#patch-prev');
                const nextBtn = overlay.querySelector('#patch-next');
                const doneBtn = overlay.querySelector('#color-done');

                const mark = (verdict) => {
                    setVerdict(this.inspection, color.id, patch, verdict);
                    this.haptic.tap();

                    // Counted after the verdict was stored, not before. The
                    // `done` in this render's closure is one behind, and reporting
                    // it would leave the desktop a patch behind for the whole
                    // colour, so the operator watching the PC would see it stall at
                    // 11 of 12 and never reach 12.
                    const nowDone = inspectedCount(this.inspection, color.id);
                    this.reportProgress(
                        wsClient,
                        (nowDone / total) * 100,
                        `${color.name}: ${patchLabel(patch, cols)} ${verdict === 'defect' ? 'afwijkend' : 'goed'}`,
                        this.id, `${this.name} (${color.name})`);
                };

                defectBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    mark('defect');
                    // Advancing on a defect is the point: the operator flagged it
                    // and wants to keep going, and a rerender is the cheapest way
                    // to confirm the flag took.
                    patch = nextPatch(patch, total);
                    render();
                });

                okBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    mark('ok');
                    patch = nextPatch(patch, total);
                    render();
                });

                prevBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    patch = previousPatch(patch, total);
                    render();
                });

                nextBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    patch = nextPatch(patch, total);
                    render();
                });

                doneBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this.removeOverlay();
                    resolve();
                });
            };

            render();
        });
    }
}
