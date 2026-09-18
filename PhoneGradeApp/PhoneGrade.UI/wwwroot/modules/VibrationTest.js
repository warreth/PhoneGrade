import { DeviceTest } from './DeviceTest.js';

/**
 * VibrationTest: Check haptic feedback and vibration motor with visual pulses.
 */
export class VibrationTest extends DeviceTest {
    constructor() {
        super('vibration', 'Vibration Engine', 'Test device haptic feedback and vibration motor');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Testing vibration engine...');

        // Check if Vibration API is available (iOS Safari does not support this)
        const isSupported = ('vibrate' in navigator);

        container.innerHTML = `
            <div style="padding: 16px; text-align: center; max-width: 320px; margin: 0 auto;">
                <div style="font-size: 16px; font-weight: bold; margin-bottom: 6px;">Vibration & Haptics</div>
                <p class="test-instructions" style="margin-bottom: 24px;">Feel for vibration pulses while the visual indicator animates.</p>
                
                <!-- Visual Pulsing Ring -->
                <div style="position: relative; width: 120px; height: 120px; margin: 0 auto 30px auto; display: flex; align-items: center; justify-content: center;">
                    <div id="vibe-ring" style="position: absolute; width: 100%; height: 100%; border-radius: 50%; border: 3px solid var(--color-accent); opacity: 0; transform: scale(0.8); transition: all 0.3s ease-out;"></div>
                    <div id="vibe-core" style="width: 70px; height: 70px; border-radius: 50%; background: var(--color-bg-secondary); border: 2px solid var(--color-border); display: flex; align-items: center; justify-content: center; font-size: 12px; font-weight: bold; color: var(--color-text);">
                        Vibrate
                    </div>
                </div>

                <div id="vibe-controls" style="display: flex; flex-direction: column; gap: 12px; margin-bottom: 24px;">
                    <button id="btn-vibe-pulse" class="btn btn-primary" style="width: 100%;">Trigger Vibration</button>
                </div>

                <div id="vibe-feedback" style="display: none; flex-direction: column; gap: 12px;">
                    <p style="font-size: 13px; font-weight: 600;">Did you feel the phone vibrate?</p>
                    <div style="display: flex; gap: 12px;">
                        <button id="btn-yes" class="btn btn-success" style="flex: 1; background: var(--color-success); color: #000; border: none;">Yes, felt it</button>
                        <button id="btn-no" class="btn btn-error" style="flex: 1; background: var(--color-error); color: #fff; border: none;">No vibration</button>
                    </div>
                </div>
            </div>
        `;

        const ring = container.querySelector('#vibe-ring');
        const core = container.querySelector('#vibe-core');
        const btnPulse = container.querySelector('#btn-vibe-pulse');
        const feedbackDiv = container.querySelector('#vibe-feedback');
        const btnYes = container.querySelector('#btn-yes');
        const btnNo = container.querySelector('#btn-no');

        const triggerPulseAnimation = (duration) => {
            ring.style.opacity = '1';
            ring.style.transform = 'scale(1.3)';
            core.style.borderColor = 'var(--color-accent)';
            core.style.boxShadow = '0 0 15px var(--color-accent)';

            setTimeout(() => {
                ring.style.opacity = '0';
                ring.style.transform = 'scale(0.8)';
                core.style.borderColor = 'var(--color-border)';
                core.style.boxShadow = 'none';
            }, duration);
        };

        return new Promise((resolve) => {
            btnPulse.onclick = () => {
                if (isSupported) {
                    navigator.vibrate([200, 100, 200]);
                }
                triggerPulseAnimation(500);

                feedbackDiv.style.display = 'flex';
                this.reportProgress(wsClient, 50, 'Awaiting confirmation...');
            };

            btnYes.onclick = () => {
                this.pass('Vibration motor is working correctly');
                this.details.working = true;
                this.details.apiSupported = isSupported;
                resolve();
            };

            btnNo.onclick = () => {
                if (!isSupported) {
                    this.skip('Vibration API not supported on this browser/OS');
                } else {
                    this.fail('User reported no vibration felt: motor may be defective');
                }
                this.details.working = false;
                this.details.apiSupported = isSupported;
                resolve();
            };

            if (!isSupported) {
                // If on iOS or browser without Vibration API, notify immediately
                const note = document.createElement('p');
                note.style.cssText = 'font-size: 11px; color: var(--color-warning); margin-top: 12px;';
                note.textContent = 'Note: Web Vibration API is not supported on iOS Safari.';
                container.querySelector('#vibe-controls').appendChild(note);
            }
        });
    }
}
