import { DeviceTest } from './DeviceTest.js';
import { t } from './i18n.js';
import {
    CAPABILITY,
    classifyMediaError,
    explainMediaError,
    hasMediaDevices
} from './MediaCapability.js';

/**
 * Every lens the phone has, photographed by the step itself and judged at the
 * end from the photos side by side.
 *
 * The old step opened the rear camera, waited for the operator to press a
 * button, asked them about that one photo, and only then opened the front one.
 * Two cameras were named before the phone was ever asked what it had, so a
 * third lens - an ultrawide, a macro, whatever the maker fitted - was never
 * opened and never reached the report. Judging them one at a time also meant
 * every later lens was held against what the first one looked like a minute
 * earlier, from memory rather than from the screen.
 *
 * The step now finds every camera the browser will name, photographs each in
 * turn on a round timer, and puts all the photos out together at the end. The
 * operator's hand is on the verdict and nowhere else. A lens that will not open,
 * or that hands back no image, sits on the list with the browser's reason beside
 * it, and whether it is taken again or called defective is their choice.
 *
 * The torch stays what it was, a bonus rather than the test. Plenty of cameras
 * have none, so the missing torch is noted and the photo is judged in whatever
 * light there is.
 *
 * The stream and the countdown are released in dispose(), which the runner calls
 * on every exit. A step left mid-countdown then neither keeps the camera open
 * nor goes on to open the next lens on a phone it is no longer testing.
 */
export class CameraTest extends DeviceTest {
    /** How long the round timer runs before the photo is taken. */
    static PHOTO_DELAY_MS = 3000;

    /** How long a photo is held up before the next lens starts. */
    static SHOT_HOLD_MS = 700;

    /**
     * How long a lens gets to deliver its first frame. Handing over a stream is
     * not the same as having a picture, and a photo taken before the first frame
     * arrives is a black rectangle whatever the sensor sees.
     */
    static FRAME_WAIT_MS = 6000;

    /**
     * How long the previous lens is given to be released before the next one is
     * opened. Some cameras hand back a black stream when the next open races the
     * last close.
     */
    static LENS_SETTLE_MS = 500;

    constructor() {
        super('camera', t('camera.stepName'), t('camera.stepDescription'));
        this.frontWorking = false;
        this.backWorking = false;
        this.torchActive = false;
        // Held on the instance so dispose() can reach them even when the step is
        // abandoned before its own cleanup runs.
        this._stream = null;
        this._countDown = null;
        this._abandoned = false;
        this._torchTried = false;
        this._frontNamed = false;
        // Both overridable so a test can walk the whole sequence without sitting
        // out three seconds per lens. The production values are pinned by a test
        // of their own.
        this.photoDelayMs = CameraTest.PHOTO_DELAY_MS;
        this.shotHoldMs = CameraTest.SHOT_HOLD_MS;
        this.frameWaitMs = CameraTest.FRAME_WAIT_MS;
        this.lensSettleMs = CameraTest.LENS_SETTLE_MS;
    }

    /**
     * N lenses at three seconds each, and then the operator's own time to look
     * at every one of them. 90 s was not enough for two cameras on a real phone.
     */
    getFailsafeMs() {
        return 180000;
    }

    reset() {
        super.reset();
        this.frontWorking = false;
        this.backWorking = false;
        this.torchActive = false;
        this._abandoned = false;
        this._torchTried = false;
        this._frontNamed = false;
    }

    /** Releases the camera. Also called by the runner when the step is abandoned. */
    dispose() {
        this._abandoned = true;
        this.stopStream();
    }

    stopStream() {
        if (this._countDown) {
            // The countdown is not a wait for the operator, so nothing is waiting
            // on it: dropping it leaves the step where it is instead of letting
            // it photograph a phone nobody is testing any more.
            clearInterval(this._countDown.pulse);
            clearTimeout(this._countDown.shot);
            this._countDown = null;
        }

        if (this._stream) {
            this._stream.getTracks().forEach(t => t.stop());
            this._stream = null;
        }
    }

    /**
     * One card for both halves of the step: the live view for photographing and
     * the list for judging, with only one of them up at a time.
     *
     * They are built together because a photo taken again needs the camera back
     * under the same card rather than a second screen appearing somewhere else,
     * and because the list is rendered by putting this markup up a second time.
     */
    markup({ live = true, shots = [] } = {}) {
        return `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title" id="cam-step-title">${t('camera.stepTitle')}</h3>

                    <div class="step-card">
                        <p class="step-lead" id="cam-instructions">${t('camera.instructionsStart')}</p>

                        <div id="video-container" class="camera-view"${live ? '' : ' hidden'}>
                            <video id="live-video" autoplay playsinline muted class="camera-video"></video>
                            <canvas id="photo-canvas" class="camera-video" hidden></canvas>

                            <div id="torch-overlay" class="camera-overlay" hidden>
                                ${t('camera.noTorch')}
                            </div>

                            <div id="cam-timer" class="cam-timer" hidden>
                                <svg class="cam-timer-dial" viewBox="0 0 36 36" aria-hidden="true">
                                    <circle class="cam-timer-track" cx="18" cy="18" r="15" />
                                    <circle id="cam-timer-circle" class="cam-timer-ring" cx="18" cy="18" r="15" pathLength="100" />
                                </svg>
                                <span id="cam-timer-num" class="cam-timer-num">3</span>
                            </div>
                        </div>

                        <div id="camera-error-area" class="step-stack" hidden>
                            <p class="step-note" id="camera-error-msg"></p>
                            <div class="step-actions">
                                <button id="btn-camera-retry" class="btn btn-secondary">${t('camera.retryButton')}</button>
                                <button id="btn-camera-reject" class="btn btn-danger">${t('camera.defectButton')}</button>
                            </div>
                        </div>

                        <div id="photo-review" class="photo-review"${live ? ' hidden' : ''}>
                            ${shots.map((shot, index) => reviewRow(shot, index)).join('')}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /** Find the lenses, photograph them, then judge them. */
    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('camera.progressPreparing'));

        this.details = {};
        container.innerHTML = this.markup({ live: true });

        let shots = [];

        try {
            const cameras = await this.discoverCameras(wsClient, container);
            shots = await this.captureAll(wsClient, container, cameras);
            await this.reviewShots(wsClient, container, shots);
            this.reportProgress(wsClient, 100, t('camera.progressFinished'));
        } finally {
            this.stopStream();
        }

        this.summarise(shots);
    }

    /**
     * Finds every camera on the phone, which needs being allowed to use one first.
     *
     * A browser hands out no device ids and no labels until the camera has been
     * granted once, so the step opens the rear camera the way it always has, by
     * facing, and reads the list that grant unlocked. The camera just opened is
     * marked out of that list by its own device id: facing is the one thing a
     * device entry never carries, and it is the one thing that makes that camera
     * the rear one.
     *
     * Where the browser will not describe itself - no enumerateDevices, a stream
     * that will not name itself, a list that does not hold the camera in use -
     * the step keeps the two cameras it can name without a list, which is what it
     * has always done. Not being told the lenses is not the same as the phone
     * not having them, and the front and rear are what the report has always
     * named.
     */
    async discoverCameras(wsClient, container) {
        for (;;) {
            const refs = this.readRefs(container);
            doResetCard(refs);

            const started = await this.startCamera(wsClient, refs, { facingMode: 'environment' }, false);

            if (started.ok) {
                const found = await this.listCameras();
                this.stopStream();
                return found || this.namedCameras();
            }

            const choice = await this.waitForCameraTrouble(refs, started.result, t('camera.rearSubject'));

            if (choice === 'retry') continue;

            // The operator has called it broken rather than trying again. The list
            // cannot be read without a grant, so the step falls back to the two
            // cameras it can name without one, with the reason already on the rear.
            // A rear that is broken does not make the front one broken, and the
            // front camera is still worth opening.
            return [
                {
                    key: 'rearCamera',
                    name: t('camera.rearSubject'),
                    noteLabel: t('camera.rearLabel'),
                    preFailed: `${started.result.kind}: ${started.result.message || started.result.name}`,
                    // Still worth opening again from the list: an operator who
                    // called it defective may only have been holding off an app
                    // that had the camera, and saying so is not a measurement.
                    constraints: { facingMode: 'environment' }
                },
                {
                    key: 'frontCamera',
                    name: t('camera.frontSubject'),
                    noteLabel: t('camera.frontLabel'),
                    constraints: { facingMode: 'user' }
                }
            ];
        }
    }

    /** The two cameras that never needed a list to be found. */
    namedCameras() {
        return [
            {
                key: 'rearCamera',
                name: t('camera.rearSubject'),
                noteLabel: t('camera.rearLabel'),
                constraints: { facingMode: 'environment' }
            },
            {
                key: 'frontCamera',
                name: t('camera.frontSubject'),
                noteLabel: t('camera.frontLabel'),
                constraints: { facingMode: 'user' }
            }
        ];
    }

    /**
     * Reads the browser's camera list, which is only complete after a grant.
     *
     * Returns null whenever the browser will not describe itself honestly, so
     * the caller can fall back to the two cameras that never needed a list.
     */
    async listCameras() {
        const media = navigator.mediaDevices;
        const track = this._stream && this._stream.getVideoTracks ? this._stream.getVideoTracks()[0] : null;
        const settings = track && track.getSettings ? (track.getSettings() || {}) : null;
        const own = settings ? settings.deviceId : null;

        if (!own || !media || typeof media.enumerateDevices !== 'function') return null;

        let devices;
        try {
            devices = await media.enumerateDevices();
        } catch (e) {
            return null;
        }

        const lenses = (devices || []).filter(d => d.kind === 'videoinput' && d.deviceId);

        // The list and the open stream have to agree about which camera is in
        // use. Where they do not, the list is describing something else, and the
        // two cameras the step can name without one are safer to trust than a
        // list that would photograph the rear camera twice.
        if (!lenses.some(d => d.deviceId === own)) return null;

        return [
            {
                key: 'rearCamera',
                name: t('camera.rearSubject'),
                noteLabel: t('camera.rearLabel'),
                constraints: { deviceId: { exact: own } }
            },
            ...lenses.filter(d => d.deviceId !== own).map((d, i) => {
                const fallback = t('camera.lensLabel', { n: i + 2 });
                return {
                    key: null,
                    name: d.label || fallback,
                    noteLabel: d.label || fallback,
                    constraints: { deviceId: { exact: d.deviceId } }
                };
            })
        ];
    }

    /**
     * Photographs every lens in turn, with nobody's hand on the button.
     *
     * The round timer runs down over the live view, takes the photo itself,
     * holds it up for a moment so the operator can see it was taken, and the
     * next lens starts. Every lens is photographed before any of them is judged,
     * so the verdicts come out side by side rather than one at a time with the
     * next camera already running.
     *
     * A lens that will not open, or that never produces a frame, becomes a photo
     * missing from the list with the reason beside it. It does not stop the
     * lenses behind it from being photographed, and it is not the operator's to
     * call at this point: they see it with everything else and can take it again
     * from there.
     */
    async captureAll(wsClient, container, cameras) {
        const shots = [];

        for (let i = 0; i < cameras.length; i++) {
            // The step can be left while a countdown is running or between two
            // lenses, and a step that has been left does not get to open the
            // next camera on a phone it is no longer testing.
            if (this._abandoned || this.status !== 'running') break;

            shots.push(await this.captureOne(wsClient, container, cameras[i], i + 1, cameras.length));

            // The stream is let go here rather than when the next lens opens, so
            // the camera has a moment to be released before its neighbour is
            // asked for. Opening the next one into the previous one's shutdown is
            // what hands back a black first frame on some phones.
            if (i < cameras.length - 1 && !this._abandoned && this.status === 'running') {
                this.stopStream();
                await this.hold(this.lensSettleMs);
            }
        }

        this.stopStream();
        return shots;
    }

    /** One lens: open it, count down, take the photo, let the camera go. */
    async captureOne(wsClient, container, camera, position, total) {
        const refs = this.readRefs(container);
        doResetCard(refs);

        if (camera.preFailed) {
            return { ...camera, photo: null, note: camera.preFailed, judged: null };
        }

        refs.instructions.textContent = t('camera.instructionsStart');

        const started = await this.startCamera(wsClient, refs, camera.constraints);

        if (this._abandoned) {
            this.stopStream();
            return { ...camera, photo: null, note: t('camera.noPhotoTaken'), judged: null };
        }

        if (!started.ok) {
            return {
                ...camera,
                photo: null,
                note: `${started.result.kind}: ${started.result.message || started.result.name}`,
                judged: null
            };
        }

        const found = this.resolveCamera(camera, position);

        if (found.key === 'rearCamera') {
            // The flash belongs to whichever lens the phone calls the rear one,
            // which is the only one that can have it.
            this.details.torchAvailable = this.torchActive;
        }

        refs.instructions.textContent = t('camera.captureLead', {
            camera: found.name,
            n: position,
            total
        });
        this.reportProgress(
            wsClient,
            Math.round(((position - 1) / total) * 100),
            t('camera.progressLive', { camera: found.name })
        );

        const photo = await this.countDown(refs, this.photoDelayMs);

        if (!photo) {
            // The camera opened and never put a frame on the video. That is a
            // lens with no image, not a photo that happens to be black.
            refs.instructions.textContent = t('camera.noPhotoTaken');
        }

        await this.hold(this.shotHoldMs);

        return { ...found, photo, note: photo ? null : t('camera.noPhotoTaken'), judged: null };
    }

    /**
     * Names a camera the way the report will name it.
     *
     * The list gives a lens the manufacturer's label, which is marketing rather
     * than a verdict, and gives the rear camera nothing at all on some phones.
     * Facing is the one thing that splits a phone's cameras into the two the
     * report has always named, so it is read off the track that is actually
     * open: the first front becomes the front camera, and every other lens keeps
     * the name it arrived with, because an ultrawide is not the rear camera.
     */
    resolveCamera(camera, position) {
        if (camera.key) return camera;

        const track = this._stream && this._stream.getVideoTracks ? this._stream.getVideoTracks()[0] : null;
        const settings = track && track.getSettings ? (track.getSettings() || {}) : {};

        if (settings.facingMode === 'user' && !this._frontNamed) {
            this._frontNamed = true;
            return {
                ...camera,
                key: 'frontCamera',
                name: t('camera.frontSubject'),
                noteLabel: t('camera.frontLabel')
            };
        }

        const fallback = t('camera.lensLabel', { n: position });
        return {
            ...camera,
            key: `camera${position}`,
            name: camera.name || fallback,
            noteLabel: camera.noteLabel || fallback
        };
    }

    /**
     * Runs the round timer and takes the photo when it runs out.
     *
     * The ring and the number are driven by the clock that also takes the photo,
     * so a frame the animation never painted cannot cost a lens its picture: the
     * photograph happens because the time has passed, not because a countdown
     * finished drawing itself.
     *
     * Returns the photo as data, or null when the camera never produced a frame.
     * That has to stay a failure rather than become a black rectangle carrying a
     * technician's approval.
     */
    countDown(refs, ms) {
        const startedAt = Date.now();

        refs.timer.hidden = false;
        refs.timerCircle.style.strokeDashoffset = '0';
        refs.timerNum.textContent = String(secondsLeft(ms));

        return new Promise((resolve) => {
            const finish = async () => {
                if (this._countDown) {
                    clearInterval(this._countDown.pulse);
                    this._countDown = null;
                }
                refs.timer.hidden = true;

                let canvas = takeSnapshot(refs);

                // A first frame can still arrive black while the sensor is
                // waking up, and a background tab can hand back black frames
                // for a moment after it comes back. A few redraws over a couple
                // of seconds turn that into the picture the lens is actually
                // showing. A genuinely dark room stays dark, and the verdict on
                // that is the operator's.
                for (let attempt = 0; attempt < 4 && canvas && frameLooksBlack(canvas); attempt++) {
                    await new Promise(r => setTimeout(r, 500));
                    const redrawn = takeSnapshot(refs);
                    if (redrawn) canvas = redrawn;
                }

                let photo = null;
                if (canvas) {
                    try {
                        photo = canvas.toDataURL('image/jpeg', 0.75);
                    } catch (e) {
                        photo = null;
                    }
                }

                resolve(photo);
            };

            const pulse = setInterval(() => {
                const elapsed = Date.now() - startedAt;
                refs.timerCircle.style.strokeDashoffset = String(Math.min(100, (elapsed / ms) * 100));
                refs.timerNum.textContent = String(secondsLeft(ms - elapsed));
            }, 100);

            this._countDown = { pulse, shot: setTimeout(finish, ms) };
        });
    }

    /** A moment with the photo on screen, so taking one reads as an event. */
    hold(ms) {
        if (ms <= 0) return Promise.resolve();
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    /**
     * Puts every photo out together and waits for a verdict on each one.
     *
     * This is where the buttons are, and not beside the live view: the operator
     * has all of them in front of them in the order they were taken, so the
     * ultrawide is judged against the wide it sits beside instead of against
     * what the screen showed four lenses ago.
     *
     * A lens with no photo has nothing to judge. It is already on the record
     * with why, and carries only the chance to take it again, which is the whole
     * choice there is for a camera that would not open.
     */
    async reviewShots(wsClient, container, shots) {
        for (;;) {
            container.innerHTML = this.markup({ live: false, shots });

            const shown = this.readRefs(container);
            shown.stepTitle.textContent = t('camera.reviewTitle');
            shown.instructions.textContent = t('camera.reviewLead');

            const outcome = await this.waitForVerdicts(container, shots);

            if (outcome === 'judged') return;

            // Sent back for another go: the camera comes up under the same card,
            // is photographed the same way it was the first time, and the list is
            // rebuilt from what came out of it.
            container.innerHTML = this.markup({ live: true });
            const again = { ...shots[outcome], preFailed: null };
            shots[outcome] = await this.captureOne(wsClient, container, again, outcome + 1, shots.length);
        }
    }

    /**
     * Waits until every photo has an answer, or until one is sent back to be
     * taken again.
     *
     * @returns {Promise<'judged'|number>} 'judged', or the index of a lens to retake
     */
    waitForVerdicts(container, shots) {
        const pending = () => shots.filter(s => s.photo && !s.judged).length;

        return new Promise((resolve) => {
            // A list with nothing left to answer is done, whatever is on it: a
            // lens that never produced a photo is already on the record with the
            // reason, and there is nothing else the step would be waiting for.
            const closeIfDone = () => {
                if (pending() === 0) resolve('judged');
            };

            const lock = (index, verdict) => {
                ['btn-use-photo', 'btn-camera-defect', 'btn-retake'].forEach((base) => {
                    const button = container.querySelector(`#${base}-${index}`);
                    if (button) button.disabled = true;
                });

                const row = container.querySelector(`#photo-row-${index}`);
                if (row) {
                    row.classList.add('photo-row-judged');
                    row.classList.add(verdict.working ? 'photo-row-approved' : 'photo-row-rejected');
                }

                const reason = container.querySelector(`#cam-reject-reason-${index}`);
                const controls = container.querySelector(`#reject-controls-${index}`);
                if (reason) reason.hidden = true;
                if (controls) controls.hidden = true;
            };

            const settle = (shot, index, verdict) => {
                if (shot.judged) return;
                shot.judged = verdict;
                lock(index, verdict);

                const verdictLine = container.querySelector(`#photo-verdict-${index}`);
                if (verdictLine) {
                    verdictLine.hidden = false;
                    verdictLine.textContent = verdict.note;
                }

                closeIfDone();
            };

            shots.forEach((shot, index) => {
                const usePhoto = container.querySelector(`#btn-use-photo-${index}`);
                const defect = container.querySelector(`#btn-camera-defect-${index}`);
                const retake = container.querySelector(`#btn-retake-${index}`);
                const reason = container.querySelector(`#cam-reject-reason-${index}`);
                const controls = container.querySelector(`#reject-controls-${index}`);

                if (retake) {
                    retake.onclick = () => {
                        // Locked rows have had their answer, and a lens that never
                        // produced a photo can always be given another go: this
                        // list is only up while the step is still open.
                        if (shot.judged) return;
                        resolve(index);
                    };
                }

                // Nothing to judge: the lens produced no photo, and the list has
                // the reason for that already.
                if (!usePhoto) return;

                usePhoto.onclick = () => settle(shot, index, { working: true, note: t('camera.noteApproved') });

                if (defect) {
                    defect.onclick = () => {
                        if (reason) reason.hidden = false;
                        if (controls) controls.hidden = false;
                    };
                }

                const reject = (note) => settle(shot, index, { working: false, note });
                const blurry = container.querySelector(`#btn-reject-blurry-${index}`);
                const dark = container.querySelector(`#btn-reject-dark-${index}`);
                const dust = container.querySelector(`#btn-reject-artifacts-${index}`);

                if (blurry) blurry.onclick = () => reject(t('camera.noteBlurry'));
                if (dark) dark.onclick = () => reject(t('camera.noteTooDark'));
                if (dust) dust.onclick = () => reject(t('camera.noteDust'));
            });

            closeIfDone();
        });
    }

    /**
     * Turns the verdicts into what the report carries.
     *
     * Each lens is named the way the operator saw it and judged on its own, so a
     * phone with a working wide and a dead ultrawide reads as exactly that
     * rather than as a phone with a camera problem.
     *
     * Nothing is written when the step has already been settled from outside.
     * The runner releases its own promise when a step is skipped or runs out of
     * time, and a verdict written here would replace the one it gave.
     */
    summarise(shots) {
        if (this._abandoned || this.status !== 'running') return;

        if (shots.length === 0) {
            this.fail(t('camera.notAssessed'));
            return;
        }

        const rejected = [];

        shots.forEach((shot, index) => {
            const key = shot.key || `camera${index + 1}`;
            const verdict = shot.judged
                || (shot.note ? { working: false, note: shot.note } : { working: false, note: t('camera.notAssessed') });

            this.details[key] = { working: verdict.working, note: verdict.note };

            if (!verdict.working) {
                rejected.push(`${shot.noteLabel || shot.name}: ${verdict.note}`);
            }
        });

        this.backWorking = !!this.details.rearCamera && this.details.rearCamera.working;
        this.frontWorking = !!this.details.frontCamera && this.details.frontCamera.working;

        if (rejected.length === 0) {
            this.pass(t('camera.passBoth'));
        } else {
            this.fail(t('camera.failWithNotes', { notes: rejected.join(t('camera.notesSeparator')) }));
        }
    }

    /** The parts of the card the current markup has, by id. */
    readRefs(container) {
        return {
            stepTitle: container.querySelector('#cam-step-title'),
            instructions: container.querySelector('#cam-instructions'),
            video: container.querySelector('#live-video'),
            canvas: container.querySelector('#photo-canvas'),
            torchOverlay: container.querySelector('#torch-overlay'),
            errorArea: container.querySelector('#camera-error-area'),
            errorMsg: container.querySelector('#camera-error-msg'),
            btnCameraRetry: container.querySelector('#btn-camera-retry'),
            btnCameraReject: container.querySelector('#btn-camera-reject'),
            timer: container.querySelector('#cam-timer'),
            timerCircle: container.querySelector('#cam-timer-circle'),
            timerNum: container.querySelector('#cam-timer-num')
        };
    }

    /**
     * Opens one camera and reports what the browser said if it will not.
     *
     * The constraints are either a facing, for the two cameras that can be asked
     * for by name, or a device id, for every lens the list found. Both take the
     * torch with them on the way past: the flash is on the rear lens, and a
     * camera that cannot switch it on is photographed in whatever light there is
     * rather than failed for it. The one call that does not is the call made just
     * to read the lens list, because nothing is being photographed yet and the
     * overlay would be up over a camera the step is about to close again.
     *
     * @returns {Promise<{ok: boolean, result: object}>}
     */
    async startCamera(wsClient, refs, constraints, withTorch = true) {
        this.stopStream();

        if (!hasMediaDevices()) {
            await this.reportCapabilityGap(wsClient, 'navigator.mediaDevices.getUserMedia', 'missing');
            return {
                ok: false,
                result: {
                    kind: CAPABILITY.MISSING,
                    name: 'MediaDevicesUnavailable',
                    message: t('camera.mediaDevicesUnavailable'),
                    fixable: false
                }
            };
        }

        try {
            const stream = await navigator.mediaDevices.getUserMedia({ video: constraints });

            this._stream = stream;
            refs.video.srcObject = stream;
            refs.video.hidden = false;
            refs.canvas.hidden = true;

            if (refs.instructions) refs.instructions.textContent = t('camera.waitingForImage');

            // A stream is not a picture. The browser hands the stream over the
            // moment the camera opens, and several phones need a moment more
            // before the first frame arrives; the countdown must not start on a
            // black rectangle that only the driver can see. A lens that never
            // produces one still ends up with no photo, because the snapshot
            // itself refuses to draw a frame that is not there.
            await waitForFirstFrame(refs.video, this.frameWaitMs);

            if (refs.instructions) refs.instructions.textContent = t('camera.instructionsStart');

            const track = stream.getVideoTracks()[0];
            const settings = track && track.getSettings ? (track.getSettings() || {}) : {};

            // The torch is a bonus, not the test. Plenty of cameras have none,
            // and a camera without a torch is not a faulty camera, so the failure
            // is noted and the photo is judged in whatever light there is.
            const isRear = settings.facingMode === 'environment' || constraints.facingMode === 'environment';
            if (withTorch && isRear && !this._torchTried) {
                this._torchTried = true;
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
     * Shows why the camera would not open and waits for the operator to choose.
     *
     * This is asked once, about the camera the step needs before it can find the
     * others, because a refused prompt or an app holding the camera is very often
     * something the operator can fix. It is not the same as a broken camera, and
     * the step does not decide that for them.
     *
     * @returns {Promise<'retry'|'defect'>}
     */
    waitForCameraTrouble(refs, result, label) {
        refs.instructions.textContent = t('camera.cannotOpen', { camera: capitalise(label) });
        refs.errorMsg.textContent = explainMediaError(result, label);

        refs.errorArea.hidden = false;
        refs.timer.hidden = true;

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

/**
 * How much of the countdown is left, in whole seconds.
 *
 * Rounded up, because a timer that has gone to zero while there is still a
 * moment of standing still left to do is a timer nobody holds still for. Both
 * readings of it, the one at the start and the one that follows the ring, go
 * through here so the number and the ring cannot drift apart.
 *
 * @param {number} msLeft - Milliseconds until the photo is taken.
 * @returns {number} Seconds to show, never below one.
 */
function secondsLeft(msLeft) {
    return Math.max(1, Math.ceil(msLeft / 1000));
}

/** Puts the card back to the state a camera is opened in. */
function doResetCard(refs) {
    refs.video.hidden = false;
    refs.canvas.hidden = true;
    refs.errorArea.hidden = true;
    refs.timer.hidden = true;
    refs.torchOverlay.hidden = true;
}

/**
 * Copies the current video frame into the canvas and puts it up in place of the
 * live view, so what was photographed is what was just on screen.
 *
 * Returns the canvas, or null when the video has not produced a frame yet, so a
 * black rectangle can never travel as a photo a technician approved.
 */
function takeSnapshot(refs) {
    const { video, canvas } = refs;

    if (!video.videoWidth || !video.videoHeight) return null;

    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

    video.hidden = true;
    canvas.hidden = false;

    return canvas;
}

/**
 * Waits until the video has a frame to draw.
 *
 * Resolves true as soon as one is there, false when the wait runs out. An
 * environment that does not model a video element is answered from the
 * element's size instead, because a unit test's fake video has no readyState
 * and waiting for one would stall the step.
 *
 * @param {HTMLVideoElement} video
 * @param {number} timeoutMs
 * @returns {Promise<boolean>}
 */
async function waitForFirstFrame(video, timeoutMs) {
    const hasSize = () => (video.videoWidth || 0) > 0 && (video.videoHeight || 0) > 0;
    const modeled = typeof video.readyState !== 'undefined'
        || typeof video.requestVideoFrameCallback === 'function';

    try {
        if (typeof video.play === 'function') await video.play();
    } catch {
        // A muted stream is allowed to start by itself; a refusal here must not
        // stop the frame wait.
    }

    if (!modeled) return hasSize();

    return new Promise((resolve) => {
        const startedAt = Date.now();
        let done = false;

        const finish = (value) => {
            if (done) return;
            done = true;
            resolve(value);
        };

        const check = () => {
            if (done) return;

            if (hasSize()) {
                // The size can be known before a frame is actually presented.
                // Where the browser can say that a frame reached the screen, it
                // is asked; the wait still times out if the callback never fires.
                if (typeof video.requestVideoFrameCallback === 'function') {
                    video.requestVideoFrameCallback(() => finish(true));
                    setTimeout(() => finish(hasSize()), Math.max(0, timeoutMs - (Date.now() - startedAt)));
                } else {
                    finish(true);
                }
                return;
            }

            if (Date.now() - startedAt >= timeoutMs) {
                finish(false);
                return;
            }

            setTimeout(check, 100);
        };

        check();
    });
}

/**
 * True when the middle of the snapshot is essentially flat black. A camera
 * that has just opened can hand back one such frame while the sensor settles;
 * the photo is taken again a moment later when that happens.
 */
function frameLooksBlack(canvas) {
    try {
        const ctx = canvas.getContext('2d');
        if (!ctx || typeof ctx.getImageData !== 'function' || !canvas.width || !canvas.height) return false;

        const width = Math.min(32, canvas.width);
        const height = Math.min(32, canvas.height);
        const data = ctx.getImageData(
            Math.floor((canvas.width - width) / 2),
            Math.floor((canvas.height - height) / 2),
            width,
            height).data;

        let brightest = 0;
        for (let i = 0; i < data.length; i += 4) {
            brightest = Math.max(brightest, data[i], data[i + 1], data[i + 2]);
        }
        return brightest < 8;
    } catch {
        return false;
    }
}

/** One photo in the list, with the verdict it is waiting for underneath it. */
function reviewRow(shot, index) {
    const photo = shot.photo
        ? `<img id="photo-${index}" class="photo-row-img" src="${escapeHtml(shot.photo)}" alt="${escapeHtml(shot.name)}">`
        : `<p id="photo-missing-${index}" class="photo-row-missing">${escapeHtml(shot.note || t('camera.notAssessed'))}</p>`;

    const judge = shot.photo
        ? `
                        <div class="step-actions">
                            <button id="btn-use-photo-${index}" class="btn btn-success">${t('camera.approveButton')}</button>
                            <button id="btn-camera-defect-${index}" class="btn btn-danger">${t('camera.rejectButton')}</button>
                        </div>
                        <p id="cam-reject-reason-${index}" class="step-question" hidden>${t('camera.rejectQuestion')}</p>
                        <div id="reject-controls-${index}" class="step-stack" hidden>
                            <div class="step-actions">
                                <button id="btn-reject-blurry-${index}" class="btn btn-secondary">${t('camera.rejectBlurry')}</button>
                                <button id="btn-reject-dark-${index}" class="btn btn-secondary">${t('camera.rejectDark')}</button>
                                <button id="btn-reject-artifacts-${index}" class="btn btn-secondary">${t('camera.rejectArtifacts')}</button>
                            </div>
                        </div>`
        : '';

    return `
        <div id="photo-row-${index}" class="photo-row">
            <p class="photo-row-name">${index + 1}. ${escapeHtml(shot.name)}</p>
            ${photo}
            ${judge}
            <div class="step-actions">
                <button id="btn-retake-${index}" class="btn btn-secondary">${t('camera.retakeButton')}</button>
            </div>
            <p id="photo-verdict-${index}" class="photo-row-verdict" hidden></p>
        </div>`;
}

/**
 * The names on a photo come from the browser and from the operator, and a
 * device label is a string somebody else wrote.
 */
function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    })[c]);
}

function capitalise(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
}
