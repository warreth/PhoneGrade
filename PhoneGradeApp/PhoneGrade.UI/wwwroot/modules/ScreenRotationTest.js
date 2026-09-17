import { DeviceTest } from './DeviceTest.js';

/**
 * ScreenRotationTest: Verify device orientation detection and transitions.
 * Tests landscape/portrait mode switching and safe-area handling on notched devices.
 */
export class ScreenRotationTest extends DeviceTest {
    constructor() {
        super('rotation', 'Screen Rotation', 'Rotate device to test orientation detection');
        this.orientationHistory = [];
        this.transitionCount = 0;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting rotation test...');

        if (!window.screen.orientation) {
            this.skip('Screen Orientation API not available');
            return;
        }

        container.innerHTML = `
            <div style="text-align: center; padding: 20px;">
                <p style="margin-bottom: 20px; color: #cbd5e1;">Current orientation:</p>
                <div id="orientation-display" style="font-size: 32px; font-weight: 700; color: #00d9ff; margin-bottom: 20px; font-family: monospace;">
                    ${window.screen.orientation.type}
                </div>
                <p style="color: #cbd5e1; margin-bottom: 10px;">Rotate your device to test</p>
                <p id="rotation-count" style="font-size: 14px; color: #94a3b8; font-family: monospace;">
                    Rotations detected: 0
                </p>
                <p id="safe-area-info" style="font-size: 12px; color: #64748b; margin-top: 20px; font-family: monospace;">
                    Safe area top: env(safe-area-inset-top)
                </p>
            </div>
        `;

        const orientationDisplay = container.querySelector('#orientation-display');
        const rotationCountEl = container.querySelector('#rotation-count');
        const safeAreaEl = container.querySelector('#safe-area-info');

        // Get initial orientation
        this.orientationHistory.push({
            type: window.screen.orientation.type,
            angle: window.screen.orientation.angle,
            timestamp: Date.now()
        });

        // Listen for orientation changes
        const handleOrientationChange = () => {
            const current = {
                type: window.screen.orientation.type,
                angle: window.screen.orientation.angle,
                timestamp: Date.now()
            };
            
            this.orientationHistory.push(current);
            this.transitionCount++;

            if (orientationDisplay) {
                orientationDisplay.textContent = current.type;
            }
            if (rotationCountEl) {
                rotationCountEl.textContent = `Rotations detected: ${this.transitionCount}`;
            }

            // Update safe area info
            const topInset = this.getSafeAreaInset('top');
            const rightInset = this.getSafeAreaInset('right');
            const bottomInset = this.getSafeAreaInset('bottom');
            const leftInset = this.getSafeAreaInset('left');
            
            if (safeAreaEl) {
                safeAreaEl.textContent = `Safe area - T:${topInset} R:${rightInset} B:${bottomInset} L:${leftInset}`;
            }

            const progress = Math.min(50 + (this.transitionCount * 10), 90);
            this.reportProgress(wsClient, progress, `Detected ${current.type} (${current.angle}°)`);
        };

        window.screen.orientation.addEventListener('change', handleOrientationChange);

        // Wait for user to rotate device (up to 30 seconds)
        const testDuration = 30000;
        const startTime = Date.now();

        return new Promise((resolve) => {
            const checkComplete = () => {
                const elapsed = Date.now() - startTime;
                const progress = 10 + (elapsed / testDuration) * 80;
                this.reportProgress(wsClient, Math.min(progress, 90), 'Waiting for rotation...');

                if (elapsed >= testDuration) {
                    window.screen.orientation.removeEventListener('change', handleOrientationChange);

                    if (this.transitionCount >= 1) {
                        this.pass(`Device orientation working - ${this.transitionCount} transitions detected`);
                        this.details.transitionCount = this.transitionCount;
                        this.details.orientationTypes = [...new Set(this.orientationHistory.map(o => o.type))];
                        this.details.angles = this.orientationHistory.map(o => o.angle);
                    } else {
                        this.fail('No orientation changes detected - try rotating device');
                    }

                    resolve();
                } else {
                    setTimeout(checkComplete, 500);
                }
            };

            checkComplete();
        });
    }

    getSafeAreaInset(side) {
        const value = getComputedStyle(document.documentElement).getPropertyValue(`--safe-area-inset-${side}`).trim();
        return value || '0px';
    }
}