import { DeviceTest } from './DeviceTest.js';

export class CameraTest extends DeviceTest {
    constructor() {
        super('camera', 'Cameras & Flash', 'Verify front/rear cameras, video recording, and flashlight');
        this.frontWorking = false;
        this.backWorking = false;
        this.flashWorking = null; // null = untested/unsupported
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Requesting camera permissions...');

        container.innerHTML = `
            <h3 style="color: var(--color-accent); margin-bottom: 16px;">Cameras & Flash</h3>
            <p class="test-instructions">Test video recording for both cameras. If supported, the flash will be tested on the rear camera.</p>
            
            <div id="camera-test-area">
                <div id="camera-preview-container" class="camera-preview" style="width: 100%; height: 300px; background: #000; border-radius: var(--radius-lg); display: flex; align-items: center; justify-content: center; position: relative; overflow: hidden;">
                    <span id="camera-status-text" style="color: var(--color-text-secondary); z-index: 2;">Initializing...</span>
                    <video id="camera-video" style="width: 100%; height: 100%; object-fit: cover; position: absolute; top: 0; left: 0; display: none;" autoplay playsinline muted></video>
                    <div id="flash-overlay" style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; background: rgba(255,255,255,0.8); display: none; z-index: 3;"></div>
                    <div id="camera-badge" style="position: absolute; top: 10px; right: 10px; background: var(--color-accent); color: #000; padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 12px; z-index: 4; display: none;"></div>
                </div>
                
                <div id="camera-controls" style="margin-top: 16px; display: none; flex-direction: column; gap: 12px;">
                    <button id="btn-record" class="btn btn-primary">Record 3s Video</button>
                    <button id="btn-test-flash" class="btn btn-secondary" style="display: none;">Test Flashlight (Torch)</button>
                </div>

                <div id="playback-section" style="margin-top: 16px; display: none; flex-direction: column; gap: 12px; background: var(--color-bg-secondary); padding: 16px; border-radius: var(--radius-lg);">
                    <p style="font-weight: 600; text-align: center;">Review Recording</p>
                    <video id="playback-video" controls playsinline style="width: 100%; border-radius: 8px; max-height: 200px; background: #000;"></video>
                    <div style="display: flex; gap: 12px; margin-top: 8px;">
                        <button id="btn-camera-yes" class="btn btn-success" style="flex: 1; background: var(--color-success); color: #000; border: none;">Video Looks Good</button>
                        <button id="btn-camera-no" class="btn btn-error" style="flex: 1; background: var(--color-error); color: #fff; border: none;">Failed / Blurry</button>
                    </div>
                </div>

                <div id="flash-feedback" style="margin-top: 16px; display: none; flex-direction: column; gap: 12px; background: var(--color-bg-secondary); padding: 16px; border-radius: var(--radius-lg);">
                    <p style="font-weight: 600; text-align: center;">Did the flash/torch turn on?</p>
                    <div style="display: flex; gap: 12px;">
                        <button id="btn-flash-yes" class="btn btn-success" style="flex: 1; background: var(--color-success); color: #000; border: none;">Yes</button>
                        <button id="btn-flash-no" class="btn btn-error" style="flex: 1; background: var(--color-error); color: #fff; border: none;">No</button>
                    </div>
                </div>
            </div>
        `;

        const videoEl = container.querySelector('#camera-video');
        const statusText = container.querySelector('#camera-status-text');
        const controlsDiv = container.querySelector('#camera-controls');
        const btnRecord = container.querySelector('#btn-record');
        const btnTestFlash = container.querySelector('#btn-test-flash');
        const playbackSection = container.querySelector('#playback-section');
        const playbackVideo = container.querySelector('#playback-video');
        const badge = container.querySelector('#camera-badge');

        // Phase 1: Front Camera
        this.reportProgress(wsClient, 10, 'Testing front camera...');
        await this.testCameraFlow(wsClient, 'user', container, videoEl, statusText, controlsDiv, btnRecord, btnTestFlash, playbackSection, playbackVideo, badge);

        // Phase 2: Back Camera
        this.reportProgress(wsClient, 50, 'Testing rear camera...');
        playbackSection.style.display = 'none';
        controlsDiv.style.display = 'none';
        videoEl.style.display = 'none';
        statusText.style.display = 'block';
        statusText.textContent = 'Switching to rear camera...';
        
        await this.testCameraFlow(wsClient, 'environment', container, videoEl, statusText, controlsDiv, btnRecord, btnTestFlash, playbackSection, playbackVideo, badge);

        this.reportProgress(wsClient, 100, 'Camera tests complete');

        if (this.frontWorking && this.backWorking) {
            this.pass('Both cameras working' + (this.flashWorking === true ? ' and flash working' : ''));
        } else if (this.frontWorking || this.backWorking) {
            this.fail(`Only ${this.frontWorking ? 'front' : 'rear'} camera working`);
        } else {
            this.fail('Both cameras failed');
        }

        this.details.frontCamera = this.frontWorking;
        this.details.rearCamera = this.backWorking;
        this.details.flash = this.flashWorking;
    }

    async testCameraFlow(wsClient, facingMode, container, videoEl, statusText, controlsDiv, btnRecord, btnTestFlash, playbackSection, playbackVideo, badge) {
        const isFront = facingMode === 'user';
        badge.textContent = isFront ? 'FRONT' : 'REAR';
        badge.style.display = 'block';
        
        // Mirror front camera
        videoEl.style.transform = isFront ? 'scaleX(-1)' : 'none';

        let stream;
        try {
            stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { exact: facingMode } },
                audio: true // Record audio with video
            });
        } catch (e) {
            try {
                stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: facingMode },
                    audio: true
                });
            } catch (fallbackError) {
                statusText.textContent = `${isFront ? 'Front' : 'Rear'} camera failed: ${fallbackError.message}`;
                statusText.style.color = 'var(--color-error)';
                await new Promise(r => setTimeout(r, 2000));
                return;
            }
        }

        videoEl.srcObject = stream;
        videoEl.style.display = 'block';
        statusText.style.display = 'none';
        controlsDiv.style.display = 'flex';

        // Check torch capabilities on the video track
        const videoTrack = stream.getVideoTracks()[0];
        const capabilities = videoTrack.getCapabilities ? videoTrack.getCapabilities() : {};
        
        if (!isFront && capabilities.torch) {
            btnTestFlash.style.display = 'block';
            
            const flashFeedback = container.querySelector('#flash-feedback');
            const btnFlashYes = container.querySelector('#btn-flash-yes');
            const btnFlashNo = container.querySelector('#btn-flash-no');
            
            const flashHandler = async () => {
                try {
                    await videoTrack.applyConstraints({ advanced: [{ torch: true }] });
                    btnTestFlash.style.display = 'none';
                    flashFeedback.style.display = 'flex';
                } catch (e) {
                    this.flashWorking = false;
                    btnTestFlash.textContent = 'Torch not supported';
                    btnTestFlash.disabled = true;
                }
            };
            
            btnTestFlash.onclick = flashHandler;
            
            btnFlashYes.onclick = async () => {
                this.flashWorking = true;
                flashFeedback.style.display = 'none';
                try { await videoTrack.applyConstraints({ advanced: [{ torch: false }] }); } catch (e) {}
            };
            
            btnFlashNo.onclick = async () => {
                this.flashWorking = false;
                flashFeedback.style.display = 'none';
                try { await videoTrack.applyConstraints({ advanced: [{ torch: false }] }); } catch (e) {}
            };
        } else {
            btnTestFlash.style.display = 'none';
        }

        return new Promise((resolve) => {
            let mediaRecorder;
            let chunks = [];

            const recordHandler = () => {
                btnRecord.disabled = true;
                btnRecord.textContent = 'Recording (3s)...';
                btnRecord.style.background = 'var(--color-error)';
                btnRecord.style.color = '#fff';
                
                chunks = [];
                mediaRecorder = new MediaRecorder(stream);
                
                mediaRecorder.ondataavailable = e => {
                    if (e.data.size > 0) chunks.push(e.data);
                };
                
                mediaRecorder.onstop = () => {
                    btnRecord.style.display = 'none';
                    controlsDiv.style.display = 'none';
                    
                    const blob = new Blob(chunks, { type: 'video/mp4' }); // generic, browser will adapt
                    playbackVideo.src = URL.createObjectURL(blob);
                    
                    videoEl.style.display = 'none';
                    playbackSection.style.display = 'flex';
                    playbackVideo.play().catch(e => console.log('Auto-play prevented'));
                };
                
                mediaRecorder.start();
                setTimeout(() => {
                    if (mediaRecorder.state === 'recording') {
                        mediaRecorder.stop();
                    }
                }, 3000);
            };

            btnRecord.onclick = recordHandler;

            const btnYes = container.querySelector('#btn-camera-yes');
            const btnNo = container.querySelector('#btn-camera-no');

            const completeFlow = (success) => {
                if (isFront) this.frontWorking = success;
                else this.backWorking = success;
                
                stream.getTracks().forEach(t => t.stop());
                
                btnRecord.onclick = null;
                btnYes.onclick = null;
                btnNo.onclick = null;
                btnTestFlash.onclick = null;
                
                btnRecord.disabled = false;
                btnRecord.textContent = 'Record 3s Video';
                btnRecord.style.background = '';
                btnRecord.style.color = '';
                btnRecord.style.display = 'block';
                
                resolve();
            };

            btnYes.onclick = () => completeFlow(true);
            btnNo.onclick = () => completeFlow(false);
        });
    }
}