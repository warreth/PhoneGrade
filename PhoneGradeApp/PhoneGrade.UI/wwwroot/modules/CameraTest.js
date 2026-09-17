import { DeviceTest } from './DeviceTest.js';

/**
 * CameraTest: Front and back camera stream verification using getUserMedia.
 */
export class CameraTest extends DeviceTest {
    constructor() {
        super('camera', 'Camera Test', 'Test voor- en achtercamera');
        this.frontWorking = false;
        this.backWorking = false;
        this.frontStream = null;
        this.backStream = null;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Camera toegang vragen...');

        container.innerHTML = `
            <h3 style="color: #00ff88; margin-bottom: 16px;">Camera Test</h3>
            <p class="test-instructions">Controleer of beide camera's werken.</p>
            
            <div id="camera-test-area">
                <div id="front-camera-section" style="margin-bottom: 24px;">
                    <p style="margin-bottom: 8px;">Voorcamera:</p>
                    <div id="front-camera-preview" class="camera-preview" style="width: 100%; max-height: 200px; background: #1a1a1a; border-radius: 8px; display: flex; align-items: center; justify-content: center; min-height: 150px;">
                        <span style="color: #b0b0b0;">Laden...</span>
                    </div>
                    <div id="front-camera-buttons" style="margin-top: 8px;"></div>
                </div>
                
                <div id="back-camera-section">
                    <p style="margin-bottom: 8px;">Achtercamera:</p>
                    <div id="back-camera-preview" class="camera-preview" style="width: 100%; max-height: 200px; background: #1a1a1a; border-radius: 8px; display: flex; align-items: center; justify-content: center; min-height: 150px;">
                        <span style="color: #b0b0b0;">Laden...</span>
                    </div>
                    <div id="back-camera-buttons" style="margin-top: 8px;"></div>
                </div>
            </div>
        `;

        // Test front camera
        this.reportProgress(wsClient, 10, 'Test voorcamera...');
        await this.testCamera('front', container.querySelector('#front-camera-preview'), container.querySelector('#front-camera-buttons'));

        // Test back camera
        this.reportProgress(wsClient, 50, 'Test achtercamera...');
        await this.testCamera('back', container.querySelector('#back-camera-preview'), container.querySelector('#back-camera-buttons'));

        this.reportProgress(wsClient, 100, 'Klaar');

        // Determine result
        if (this.frontWorking && this.backWorking) {
            this.pass('Beide camera's werken');
            this.details.frontCamera = 'working';
            this.details.backCamera = 'working';
        } else if (this.frontWorking || this.backWorking) {
            const working = this.frontWorking ? 'voorkant' : 'achterkant';
            const broken = this.frontWorking ? 'achterkant' : 'voorkant';
            this.fail(`Alleen ${working} camera werkt - ${broken} defect`);
            this.details.frontCamera = this.frontWorking ? 'working' : 'failed';
            this.details.backCamera = this.backWorking ? 'working' : 'failed';
        } else {
            this.fail('Geen enkele camera werkt');
            this.details.frontCamera = 'failed';
            this.details.backCamera = 'failed';
        }

        // Stop all streams
        if (this.frontStream) this.frontStream.getTracks().forEach(t => t.stop());
        if (this.backStream) this.backStream.getTracks().forEach(t => t.stop());
    }

    async testCamera(facing, previewDiv, buttonsDiv) {
        const isFront = facing === 'front';
        const constraints = {
            video: {
                facingMode: isFront ? 'user' : 'environment'
            }
        };

        try {
            const stream = await navigator.mediaDevices.getUserMedia(constraints);
            
            if (isFront) {
                this.frontStream = stream;
            } else {
                this.backStream = stream;
            }

            // Create video element
            previewDiv.innerHTML = '';
            const video = document.createElement('video');
            video.srcObject = stream;
            video.autoplay = true;
            video.playsInline = true;
            video.style.cssText = 'width: 100%; height: auto; border-radius: 8px;';
            previewDiv.appendChild(video);

            // Add confirmation buttons
            buttonsDiv.innerHTML = `
                <button class="btn-primary camera-confirm" style="width: auto; padding: 8px 16px; margin-right: 8px;">Werkt</button>
                <button class="btn-secondary camera-fail" style="width: auto; padding: 8px 16px;">Werkt niet</button>
            `;

            return new Promise((resolve) => {
                buttonsDiv.querySelector('.camera-confirm').onclick = () => {
                    if (isFront) this.frontWorking = true;
                    else this.backWorking = true;
                    resolve();
                };

                buttonsDiv.querySelector('.camera-fail').onclick = () => {
                    resolve();
                };
            });

        } catch (error) {
            previewDiv.innerHTML = `<span style="color: #ff4444;">Fout: ${error.message}</span>`;
            buttonsDiv.innerHTML = `<button class="btn-secondary camera-skip" style="width: auto; padding: 8px 16px;">Overslaan</button>`;
            
            return new Promise((resolve) => {
                buttonsDiv.querySelector('.camera-skip').onclick = () => resolve();
            });
        }
    }
}