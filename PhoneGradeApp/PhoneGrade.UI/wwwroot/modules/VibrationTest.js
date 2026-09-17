import { DeviceTest } from './DeviceTest.js';

/**
 * VibrationTest: Check haptic feedback/vibration capability.
 */
export class VibrationTest extends DeviceTest {
    constructor() {
        super('vibration', 'Vibration Engine', 'Test device haptic feedback and vibration motor');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Initializing vibration API...');

        // Check if Vibration API is available (iOS Safari does NOT support this currently, Android does)
        if (!('vibrate' in navigator)) {
            this.skip('Vibration API not supported on this browser/device (common on iOS)');
            return;
        }

        container.innerHTML = `
            <div style="padding: 20px; text-align: center;">
                <p style="margin-bottom: 20px; color: #cbd5e1;">Test the device vibration motor.</p>
                
                <div id="vibe-controls" style="display: flex; flex-direction: column; gap: 16px; align-items: center; margin-bottom: 30px;">
                    <button id="btn-vibe-short" class="btn btn-secondary" style="width: 200px;">Short Vibrate</button>
                    <button id="btn-vibe-long" class="btn btn-secondary" style="width: 200px;">Long Vibrate</button>
                    <button id="btn-vibe-pattern" class="btn btn-secondary" style="width: 200px;">Pattern Vibrate</button>
                </div>

                <div id="vibe-feedback" style="display: none; justify-content: center; gap: 12px;">
                    <button id="btn-yes" class="btn btn-primary" style="width: auto;">I felt it</button>
                    <button id="btn-no" class="btn btn-error" style="width: auto; background: #f87171; color: white; border: none;">I felt nothing</button>
                </div>
            </div>
        `;

        const btnShort = container.querySelector('#btn-vibe-short');
        const btnLong = container.querySelector('#btn-vibe-long');
        const btnPattern = container.querySelector('#btn-vibe-pattern');
        const feedbackDiv = container.querySelector('#vibe-feedback');
        const btnYes = container.querySelector('#btn-yes');
        const btnNo = container.querySelector('#btn-no');

        let hasVibrated = false;

        btnShort.onclick = () => {
            navigator.vibrate(200);
            hasVibrated = true;
            feedbackDiv.style.display = 'flex';
            this.reportProgress(wsClient, 50, 'Testing short vibration...');
        };

        btnLong.onclick = () => {
            navigator.vibrate(1000);
            hasVibrated = true;
            feedbackDiv.style.display = 'flex';
            this.reportProgress(wsClient, 50, 'Testing long vibration...');
        };

        btnPattern.onclick = () => {
            navigator.vibrate([200, 100, 200, 100, 500]);
            hasVibrated = true;
            feedbackDiv.style.display = 'flex';
            this.reportProgress(wsClient, 50, 'Testing vibration pattern...');
        };

        return new Promise((resolve) => {
            btnYes.onclick = () => {
                this.pass('Vibration motor is working correctly');
                this.details.working = true;
                this.reportProgress(wsClient, 100, 'Test complete');
                resolve();
            };

            btnNo.onclick = () => {
                this.fail('User reported no vibration felt. Motor may be defective.');
                this.details.working = false;
                this.reportProgress(wsClient, 100, 'Test complete');
                resolve();
            };
            
            // Allow skipping if user doesn't interact within 30s
            setTimeout(() => {
                if (!hasVibrated) {
                    this.skip('Test timed out');
                    resolve();
                }
            }, 30000);
        });
    }
}