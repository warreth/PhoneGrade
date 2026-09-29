import { DeviceTest } from './DeviceTest.js';

export class CameraTest extends DeviceTest {
    constructor() {
        super('camera', 'Camera & Torch', 'Test front and rear cameras with live video and photo review');
        this.frontWorking = false;
        this.backWorking = false;
        this.torchActive = false;
        // Held on the instance so dispose() can reach the stream even when the
        // step is abandoned before its own cleanup runs.
        this._stream = null;
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

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting camera tests...');

        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 450px;">
                    <h3 id="cam-step-title" style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Camera Inspection</h3>
                    <p id="cam-instructions" style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">
                        Initializing live camera feed...
                    </p>

                    <div id="video-container" style="position: relative; width: 100%; height: 260px; background: #000; border-radius: 8px; overflow: hidden; margin-bottom: 16px;">
                        <video id="live-video" autoplay playsinline muted style="width: 100%; height: 100%; object-fit: cover;"></video>
                        <canvas id="photo-canvas" style="display: none; width: 100%; height: 100%; object-fit: cover;"></canvas>
                        
                        <div id="torch-overlay" style="display: none; position: absolute; top: 10px; left: 10px; right: 10px; background: rgba(0,0,0,0.7); color: #fff; padding: 6px 12px; border-radius: 6px; font-size: 11px;">
                            Inspect the photo carefully. Ensure the lighting is adequate before confirming.
                        </div>
                    </div>

                    <div id="review-instructions" style="display: none; margin-bottom: 12px; font-size: 12px; color: var(--color-text-secondary);">
                        Inspect the captured photo for blurriness, lens dust, or sensor artifacts before confirming.
                    </div>

                    <div id="live-controls" style="display: flex; gap: 10px;">
                        <button id="btn-capture" class="btn btn-primary" style="flex: 1; padding: 12px; font-weight: bold;">Capture Photo</button>
                        <button id="btn-camera-defect" class="btn btn-danger" style="flex: 1; padding: 12px; font-weight: bold;">Camera Defect</button>
                    </div>

                    <div id="review-controls" style="display: none; gap: 10px;">
                        <button id="btn-retake" class="btn btn-secondary" style="flex: 1; padding: 12px;">Retake Photo</button>
                        <button id="btn-use-photo" class="btn btn-success" style="flex: 1; padding: 12px; font-weight: bold;">Use Photo</button>
                    </div>

                    <p id="cam-reject-reason" style="display: none; margin-top: 12px; font-size: 13px; color: var(--color-text-secondary);">
                        What is wrong with this camera?
                    </p>
                    <div id="reject-controls" style="display: none; gap: 10px; margin-top: 8px;">
                        <button id="btn-reject-blurry" class="btn btn-secondary" style="flex: 1; padding: 10px; font-size: 13px;">Blurry</button>
                        <button id="btn-reject-dark" class="btn btn-secondary" style="flex: 1; padding: 10px; font-size: 13px;">Too Dark</button>
                        <button id="btn-reject-artifacts" class="btn btn-secondary" style="flex: 1; padding: 10px; font-size: 13px;">Artifacts</button>
                    </div>
                </div>
            </div>
        `;

        const stepTitle = container.querySelector('#cam-step-title');
        const instructions = container.querySelector('#cam-instructions');
        const video = container.querySelector('#live-video');
        const canvas = container.querySelector('#photo-canvas');
        const torchOverlay = container.querySelector('#torch-overlay');
        const reviewInstructions = container.querySelector('#review-instructions');
        const liveControls = container.querySelector('#live-controls');
        const reviewControls = container.querySelector('#review-controls');
        const btnCapture = container.querySelector('#btn-capture');
        const btnRetake = container.querySelector('#btn-retake');
        const btnUsePhoto = container.querySelector('#btn-use-photo');
        const btnDefect = container.querySelector('#btn-camera-defect');
        const rejectReason = container.querySelector('#cam-reject-reason');
        const rejectControls = container.querySelector('#reject-controls');
        const btnRejectBlurry = container.querySelector('#btn-reject-blurry');
        const btnRejectDark = container.querySelector('#btn-reject-dark');
        const btnRejectArtifacts = container.querySelector('#btn-reject-artifacts');

        // stopStream() lives on the instance so dispose() can stop the camera
        // when this step is abandoned by a skip or by the failsafe.
        const stopStream = () => {
            this.stopStream();
            video.srcObject = null;
        };

        const startCamera = async (facingMode) => {
            stopStream();
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: facingMode }
                });
                this._stream = stream;
                video.srcObject = stream;
                video.style.display = 'block';
                canvas.style.display = 'none';
                
                const track = stream.getVideoTracks()[0];

                // Try to toggle torch for back camera
                if (facingMode === 'environment') {
                    try {
                        await track.applyConstraints({
                            advanced: [{ torch: true }]
                        });
                        this.torchActive = true;
                    } catch (e) {
                        this.torchActive = false;
                        torchOverlay.style.display = 'block';
                    }
                }
            } catch (err) {
                console.error('Camera stream error:', err);
                throw err;
            }
        };

        // Returns false when the video has not produced a frame yet, so a black
        // image cannot be accepted as a good photo.
        const takeSnapshot = () => {
            if (!video.videoWidth || !video.videoHeight) {
                instructions.textContent = 'No image yet - wait for the live feed, then capture.';
                return false;
            }

            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

            video.style.display = 'none';
            canvas.style.display = 'block';
            
            liveControls.style.display = 'none';
            reviewControls.style.display = 'flex';
            reviewInstructions.style.display = 'block';
            return true;
        };

        const retakePhoto = () => {
            canvas.style.display = 'none';
            video.style.display = 'block';
            
            liveControls.style.display = 'flex';
            reviewControls.style.display = 'none';
            reviewInstructions.style.display = 'none';
            rejectReason.style.display = 'none';
            rejectControls.style.display = 'none';
        };

        const showRejectOptions = () => {
            rejectReason.style.display = 'block';
            rejectControls.style.display = 'flex';
        };

        /**
         * Wires the buttons for one camera step. The operator either accepts the
         * photo, or names a defect - which is what makes the fail path reachable
         * instead of every camera silently passing.
         */
        const runStep = (label) => new Promise((settleStep) => {
            let decided = false;

            const decide = (working, note) => {
                if (decided) return;
                decided = true;
                this.details[label] = { working, note };
                settleStep();
            };

            btnCapture.onclick = () => {
                takeSnapshot();
            };

            btnRetake.onclick = () => {
                retakePhoto();
            };

            btnUsePhoto.onclick = () => {
                retakePhoto();
                decide(true, 'Photo accepted by operator');
            };

            btnDefect.onclick = () => {
                showRejectOptions();
            };

            const rejectWith = (note) => decide(false, note);
            btnRejectBlurry.onclick = () => rejectWith('Blurry photo');
            btnRejectDark.onclick = () => rejectWith('Too dark to inspect');
            btnRejectArtifacts.onclick = () => rejectWith('Sensor artifacts or lens dust');
        });

        return new Promise(async (resolve) => {
            // Retries reuse the same instance, so a previous run's verdict must
            // not leak into this one.
            this.frontWorking = false;
            this.backWorking = false;
            this.torchActive = false;
            this.details = {};

            try {
                // Step 1: Rear Camera Test
                stepTitle.textContent = 'Step 1: Rear Camera & Torch';
                instructions.textContent = 'Frame an object and capture a photo using the rear camera.';
                await startCamera('environment');

                await runStep('rearCamera');
                this.backWorking = this.details.rearCamera?.working === true;
                this.reportProgress(wsClient, 50,
                    this.backWorking ? 'Rear camera verified' : 'Rear camera rejected');

                // Reset UI for Step 2
                retakePhoto();
                torchOverlay.style.display = 'none';

                // Step 2: Front Camera Test
                stepTitle.textContent = 'Step 2: Front Camera (Selfie)';
                instructions.textContent = 'Frame your face and capture a photo using the front camera.';
                await startCamera('user');

                await runStep('frontCamera');
                this.frontWorking = this.details.frontCamera?.working === true;
                this.reportProgress(wsClient, 100,
                    this.frontWorking ? 'Front camera verified' : 'Front camera rejected');

                stopStream();
                this.details.torchActivated = this.torchActive;

                const rejected = [];
                if (!this.backWorking) rejected.push('rear');
                if (!this.frontWorking) rejected.push('front');
                if (rejected.length === 0) {
                    this.pass('Both front and rear cameras passed live inspection');
                } else {
                    const notes = rejected.map(side => {
                        const key = side === 'rear' ? 'rearCamera' : 'frontCamera';
                        return `${side}: ${this.details[key]?.note || 'not verified'}`;
                    }).join('; ');
                    this.fail(`Camera rejected - ${notes}`);
                }

                resolve();
            } catch (err) {
                stopStream();
                this.fail('Failed to access camera media stream: ' + err.message);
                resolve();
            }
        });
    }
}
