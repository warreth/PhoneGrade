import { DeviceTest } from './DeviceTest.js';

export class CameraTest extends DeviceTest {
    constructor() {
        super('camera', 'Camera & Torch', 'Test front and rear cameras with live video and photo review');
        this.frontWorking = false;
        this.backWorking = false;
        this.torchActive = false;
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
                            Ensure light source is adequate. Enable flash manually on screen if available.
                        </div>
                    </div>

                    <div id="review-instructions" style="display: none; margin-bottom: 12px; font-size: 12px; color: var(--color-text-secondary);">
                        Inspect the captured photo for blurriness, lens dust, or sensor artifacts before confirming.
                    </div>

                    <div id="live-controls" style="display: flex; gap: 10px;">
                        <button id="btn-capture" class="btn btn-primary" style="flex: 1; padding: 12px; font-weight: bold;">Capture Photo</button>
                    </div>

                    <div id="review-controls" style="display: none; gap: 10px;">
                        <button id="btn-retake" class="btn btn-secondary" style="flex: 1; padding: 12px;">Retake Photo</button>
                        <button id="btn-use-photo" class="btn btn-success" style="flex: 1; padding: 12px; font-weight: bold;">Use Photo</button>
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

        let currentStream = null;
        let currentTrack = null;

        const stopStream = () => {
            if (currentStream) {
                currentStream.getTracks().forEach(t => t.stop());
                currentStream = null;
                currentTrack = null;
            }
        };

        const startCamera = async (facingMode) => {
            stopStream();
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: facingMode }
                });
                currentStream = stream;
                video.srcObject = stream;
                video.style.display = 'block';
                canvas.style.display = 'none';
                
                const track = stream.getVideoTracks()[0];
                currentTrack = track;

                // Try to toggle torch for back camera
                if (facingMode === 'environment') {
                    try {
                        await track.applyConstraints({
                            advanced: [{ torch: true }]
                        });
                        this.torchActive = true;
                    } catch (e) {
                        torchOverlay.style.display = 'block';
                    }
                }
            } catch (err) {
                console.error('Camera stream error:', err);
                throw err;
            }
        };

        const takeSnapshot = () => {
            canvas.width = video.videoWidth || 640;
            canvas.height = video.videoHeight || 480;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

            video.style.display = 'none';
            canvas.style.display = 'block';
            
            liveControls.style.display = 'none';
            reviewControls.style.display = 'flex';
            reviewInstructions.style.display = 'block';
        };

        const retakePhoto = () => {
            canvas.style.display = 'none';
            video.style.display = 'block';
            
            liveControls.style.display = 'flex';
            reviewControls.style.display = 'none';
            reviewInstructions.style.display = 'none';
        };

        return new Promise(async (resolve) => {
            try {
                // Step 1: Rear Camera Test
                stepTitle.textContent = 'Step 1: Rear Camera & Torch';
                instructions.textContent = 'Frame an object and capture a photo using the rear camera.';
                await startCamera('environment');

                const rearPhotoPromise = new Promise((resRear) => {
                    btnCapture.onclick = () => {
                        takeSnapshot();
                    };

                    btnRetake.onclick = () => {
                        retakePhoto();
                    };

                    btnUsePhoto.onclick = () => {
                        this.backWorking = true;
                        resRear();
                    };
                });

                await rearPhotoPromise;
                this.reportProgress(wsClient, 50, 'Rear camera verified');

                // Reset UI for Step 2
                retakePhoto();
                torchOverlay.style.display = 'none';

                // Step 2: Front Camera Test
                stepTitle.textContent = 'Step 2: Front Camera (Selfie)';
                instructions.textContent = 'Frame your face and capture a photo using the front camera.';
                await startCamera('user');

                const frontPhotoPromise = new Promise((resFront) => {
                    btnCapture.onclick = () => {
                        takeSnapshot();
                    };

                    btnRetake.onclick = () => {
                        retakePhoto();
                    };

                    btnUsePhoto.onclick = () => {
                        this.frontWorking = true;
                        resFront();
                    };
                });

                await frontPhotoPromise;
                this.reportProgress(wsClient, 100, 'Front camera verified');

                stopStream();

                if (this.frontWorking && this.backWorking) {
                    this.pass('Both front and rear cameras passed live inspection');
                } else {
                    this.fail('One or more cameras failed inspection');
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
