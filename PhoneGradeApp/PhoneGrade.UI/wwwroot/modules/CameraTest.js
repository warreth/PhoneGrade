import { DeviceTest } from './DeviceTest.js';
import {
    CAPABILITY,
    classifyMediaError,
    explainMediaError,
    hasMediaDevices
} from './MediaCapability.js';

/**
 * The front and rear cameras, checked by taking a photo with each and looking at
 * it.
 *
 * The old step tried to open the rear camera and, if anything at all went wrong,
 * threw the error out to a single catch that failed the whole step with the raw
 * browser message. A refused permission prompt, a camera another app was holding
 * for a second, and a phone with no rear camera all produced the same dead end,
 * and the operator had no way to do anything about any of them. The step also
 * failed both cameras when only the first had been reached.
 *
 * Every camera is now reached and judged on its own. A camera that will not open
 * shows the operator what the browser said in plain Dutch, and offers two things
 * they can actually do: try again, or record the camera as defective. The step
 * never decides either of those for them, and a failure to open the front camera
 * no longer throws away whatever was decided about the rear one.
 *
 * The torch is treated the same way. Plenty of cameras have none, and a camera
 * without a torch is not a camera with a fault, so the missing torch is noted and
 * the photo is judged without it.
 */
export class CameraTest extends DeviceTest {
    constructor() {
        super('camera', 'Camera & flits', 'Test voor- en achtercamera met live beeld en fotobeoordeling');
        this.frontWorking = false;
        this.backWorking = false;
        this.torchActive = false;
        // Held on the instance so dispose() can reach the stream even when the
        // step is abandoned before its own cleanup runs.
        this._stream = null;
    }

    /**
     * Two cameras, each with its own prompt, framing, capture and review.
     *
     * 90 s is not enough for one camera on a real phone, let alone two. A timeout
     * in the middle of the second one failed a camera that had not been looked at
     * yet.
     */
    getFailsafeMs() {
        return 180000;
    }

    reset() {
        super.reset();
        this.frontWorking = false;
        this.backWorking = false;
        this.torchActive = false;
    }

    /** Releases the camera. Also called by the runner when the step is abandoned. */
    dispose() {
        this.stopStream();
    }

    stopStream() {
        if (this._stream) {
            this._stream.getTracks().forEach(t => t.stop());
            this._stream = null;
        }
    }

    markup() {
        return `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title" id="cam-step-title">Camera-inspectie</h3>

                    <div class="step-card">
                        <p class="step-lead" id="cam-instructions">Live camera wordt gestart...</p>

                        <div id="video-container" class="camera-view">
                            <video id="live-video" autoplay playsinline muted class="camera-video"></video>
                            <canvas id="photo-canvas" class="camera-video" hidden></canvas>

                            <div id="torch-overlay" class="camera-overlay" hidden>
                                Deze camera heeft geen flits. Zorg zelf voor voldoende licht
                                voordat je de foto beoordeelt.
                            </div>
                        </div>

                        <div id="camera-error-area" class="step-stack" hidden>
                            <p class="step-note" id="camera-error-msg"></p>
                            <div class="step-actions">
                                <button id="btn-camera-retry" class="btn btn-secondary">Opnieuw proberen</button>
                                <button id="btn-camera-reject" class="btn btn-danger">Camera defect</button>
                            </div>
                        </div>

                        <div id="review-instructions" class="step-hint" hidden>
                            Bekijk de foto op scherpte, stof op de lens en ruis in het beeld.
                        </div>

                        <div id="live-controls" class="step-actions">
                            <button id="btn-capture" class="btn btn-primary">Foto maken</button>
                            <button id="btn-camera-defect" class="btn btn-danger">Camera defect</button>
                        </div>

                        <div id="review-controls" class="step-actions" hidden>
                            <button id="btn-retake" class="btn btn-secondary">Opnieuw maken</button>
                            <button id="btn-use-photo" class="btn btn-success">Foto goedkeuren</button>
                        </div>

                        <p id="cam-reject-reason" class="step-question" hidden>Wat is er mis met deze camera?</p>
                        <div id="reject-controls" class="step-stack" hidden>
                            <div class="step-actions">
                                <button id="btn-reject-blurry" class="btn btn-secondary">Onscherp</button>
                                <button id="btn-reject-dark" class="btn btn-secondary">Te donker</button>
                                <button id="btn-reject-artifacts" class="btn btn-secondary">Ruis of stof</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Camera-inspectie voorbereiden...');

        container.innerHTML = this.markup();

        const refs = {
            stepTitle: container.querySelector('#cam-step-title'),
            instructions: container.querySelector('#cam-instructions'),
            video: container.querySelector('#live-video'),
            canvas: container.querySelector('#photo-canvas'),
            torchOverlay: container.querySelector('#torch-overlay'),
            errorArea: container.querySelector('#camera-error-area'),
            errorMsg: container.querySelector('#camera-error-msg'),
            btnCameraRetry: container.querySelector('#btn-camera-retry'),
            btnCameraReject: container.querySelector('#btn-camera-reject'),
            reviewInstructions: container.querySelector('#review-instructions'),
            liveControls: container.querySelector('#live-controls'),
            reviewControls: container.querySelector('#review-controls'),
            btnCapture: container.querySelector('#btn-capture'),
            btnRetake: container.querySelector('#btn-retake'),
            btnUsePhoto: container.querySelector('#btn-use-photo'),
            btnDefect: container.querySelector('#btn-camera-defect'),
            rejectReason: container.querySelector('#cam-reject-reason'),
            rejectControls: container.querySelector('#reject-controls'),
            btnRejectBlurry: container.querySelector('#btn-reject-blurry'),
            btnRejectDark: container.querySelector('#btn-reject-dark'),
            btnRejectArtifacts: container.querySelector('#btn-reject-artifacts')
        };

        this.details = {};

        try {
            const rear = await this.inspectCamera(wsClient, refs, 'rear');
            this.backWorking = rear.working;
            this.reportProgress(wsClient, 50, rear.working ? 'Achtercamera goedgekeurd' : 'Achtercamera afgekeurd');

            const front = await this.inspectCamera(wsClient, refs, 'front');
            this.frontWorking = front.working;
            this.reportProgress(wsClient, 100, front.working ? 'Voorcamera goedgekeurd' : 'Voorcamera afgekeurd');
        } finally {
            this.stopStream();
        }

        this.details.torchActivated = this.torchActive;

        const rejected = [];
        if (!this.backWorking) rejected.push(['achter', 'rearCamera']);
        if (!this.frontWorking) rejected.push(['voor', 'frontCamera']);

        if (rejected.length === 0) {
            this.pass('Voor- en achtercamera zijn live beoordeeld en goedgekeurd');
        } else {
            const notes = rejected.map(([label, key]) => {
                const note = this.details[key]?.note || 'niet beoordeeld';
                return `${label}: ${note}`;
            }).join('; ');
            this.fail(`Camera afgekeurd - ${notes}`);
        }
    }

    /**
     * Takes one camera through frame, capture, review and verdict.
     *
     * The two ways this can end - a photo accepted, or the camera declared
     * defective - are both the operator's call. A camera that will not open is
     * offered back to them with the browser's reason and a retry, because the
     * reason is very often something they can fix (a refused prompt, an app
     * holding the camera) and it is not the same as a broken camera.
     */
    async inspectCamera(wsClient, refs, side) {
        const isRear = side === 'rear';
        const key = isRear ? 'rearCamera' : 'frontCamera';
        const label = isRear ? 'achtercamera' : 'voorcamera';
        const facingMode = isRear ? 'environment' : 'user';
        const volume = isRear ? 'Stap 1: achtercamera' : 'Stap 2: voorcamera';

        refs.stepTitle.textContent = volume;
        refs.instructions.textContent = isRear
            ? 'Richt de achtercamera op een voorwerp en maak een foto.'
            : 'Richt de voorcamera op je gezicht en maak een foto.';

        // Every camera starts from a clean card, so a defect reported on the rear
        // camera does not leave its buttons sitting under the front one.
        let decision = null;

        while (decision === null) {
            // Also on a retry. The trouble card hides the capture controls, and
            // coming back from a retry into a card that still has them hidden left
            // the operator with a live view and nothing to press.
            doResetCard(refs);
            refs.torchOverlay.hidden = true;

            const started = await this.startCamera(wsClient, refs, facingMode);

            if (!started.ok) {
                this.details[key] = {
                    working: false,
                    note: `${started.result.kind}: ${started.result.message || started.result.name}`
                };

                decision = await this.waitForCameraTrouble(refs, started.result, label);

                if (decision === 'retry') {
                    decision = null;
                    this.details[key] = null;
                    continue;
                }

                return { working: false };
            }

            if (isRear) {
                this.details.torchAvailable = this.torchActive;
            }

            this.reportProgress(wsClient, isRear ? 25 : 75, `${label} live, wacht op een foto`);

            decision = await this.waitForPhotoVerdict(refs, key, label);
        }

        return { working: decision === 'accepted' };
    }

    /**
     * Opens one camera and reports what the browser said if it will not.
     *
     * @returns {Promise<{ok: boolean, result: object}>}
     */
    async startCamera(wsClient, refs, facingMode) {
        this.stopStream();

        if (!hasMediaDevices()) {
            await this.reportCapabilityGap(wsClient, 'navigator.mediaDevices.getUserMedia', 'missing');
            return {
                ok: false,
                result: {
                    kind: CAPABILITY.MISSING,
                    name: 'MediaDevicesUnavailable',
                    message: 'navigator.mediaDevices is niet beschikbaar',
                    fixable: false
                }
            };
        }

        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode }
            });

            this._stream = stream;
            refs.video.srcObject = stream;
            refs.video.hidden = false;
            refs.canvas.hidden = true;

            // The torch is a bonus, not the test. Plenty of cameras have none, and
            // a camera without a torch is not a faulty camera, so the failure is
            // noted and the photo is judged in whatever light there is.
            if (facingMode === 'environment') {
                const track = stream.getVideoTracks()[0];
                try {
                    await track.applyConstraints({ advanced: [{ torch: true }] });
                    this.torchActive = true;
                } catch (e) {
                    this.torchActive = false;
                    refs.torchOverlay.hidden = false;
                }
            }

            return { ok: true, result: null };
        } catch (err) {
            const result = classifyMediaError(err);
            if (result.kind === CAPABILITY.DENIED || result.kind === CAPABILITY.MISSING) {
                await this.reportCapabilityGap(wsClient, 'navigator.mediaDevices.getUserMedia', result.kind);
            }
            return { ok: false, result };
        }
    }

    /**
     * Shows why a camera would not open and waits for the operator to choose.
     *
     * @returns {Promise<'retry'|'defect'>}
     */
    waitForCameraTrouble(refs, result, label) {
        refs.instructions.textContent = `${capitalise(label)} kon niet worden geopend.`;
        refs.errorMsg.textContent = explainMediaError(result, label);

        refs.errorArea.hidden = false;
        refs.liveControls.hidden = true;
        refs.reviewControls.hidden = true;

        return new Promise((resolve) => {
            refs.btnCameraRetry.onclick = () => {
                refs.errorArea.hidden = true;
                resolve('retry');
            };

            refs.btnCameraReject.onclick = () => {
                refs.errorArea.hidden = true;
                resolve('defect');
            };
        });
    }

    /**
     * Runs the capture and review controls until the operator accepts the photo
     * or names a defect.
     *
     * @returns {Promise<'accepted'|'defect'>}
     */
    waitForPhotoVerdict(refs, key, label) {
        return new Promise((resolve) => {
            const decide = (accepted, note) => {
                this.details[key] = { working: accepted, note };
                resolve(accepted ? 'accepted' : 'defect');
            };

            refs.btnCapture.onclick = () => {
                const captured = takeSnapshot(refs);
                if (!captured) {
                    refs.instructions.textContent =
                        'Nog geen beeld. Wacht tot het livebeeld loopt en maak dan de foto.';
                }
            };

            refs.btnRetake.onclick = () => {
                showLive(refs);
            };

            refs.btnUsePhoto.onclick = () => {
                decide(true, 'Foto goedgekeurd door de technicus');
            };

            refs.btnDefect.onclick = () => {
                showLive(refs);
                refs.rejectReason.hidden = false;
                refs.rejectControls.hidden = false;
            };

            const reject = (note) => decide(false, note);
            refs.btnRejectBlurry.onclick = () => reject('Onscherpe foto');
            refs.btnRejectDark.onclick = () => reject('Te donker om te beoordelen');
            refs.btnRejectArtifacts.onclick = () => reject('Ruis of stof op de lens');
        });
    }

    async reportCapabilityGap(wsClient, missingApi, reason) {
        if (!wsClient || !wsClient.sessionId) return;
        try {
            const ua = navigator.userAgent;
            let osVersion = 'Unknown';
            if (/Android/.test(ua)) osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || 'Android Unknown';
            else if (/iPhone|iPad|iPod/.test(ua)) osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || 'iOS Unknown';

            await fetch(`${wsClient.baseUrl}/api/pwa/log-warning`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sessionId: wsClient.sessionId,
                    missingApi,
                    userAgent: ua,
                    osVersion,
                    reason
                })
            });
        } catch (e) {
            console.warn('Could not report the unusable camera API:', e);
        }
    }
}

/** Puts the card back to the state it starts a camera in. */
function doResetCard(refs) {
    showLive(refs);
    refs.errorArea.hidden = true;
    refs.rejectReason.hidden = true;
    refs.rejectControls.hidden = true;
}

function showLive(refs) {
    refs.reviewInstructions.hidden = true;
    refs.liveControls.hidden = false;
    refs.reviewControls.hidden = true;
    refs.rejectReason.hidden = true;
    refs.rejectControls.hidden = true;
    refs.canvas.hidden = true;
    refs.video.hidden = false;
}

/**
 * Copies the current video frame into the canvas for review.
 *
 * Returns false when the video has not produced a frame yet, so a black image
 * cannot be accepted as a good photo.
 */
function takeSnapshot(refs) {
    const { video, canvas } = refs;

    if (!video.videoWidth || !video.videoHeight) return false;

    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

    video.hidden = true;
    canvas.hidden = false;
    refs.liveControls.hidden = true;
    refs.reviewControls.hidden = false;
    refs.reviewInstructions.hidden = false;
    return true;
}

function capitalise(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
}
