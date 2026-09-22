import { DeviceTest } from './DeviceTest.js';

export class ScreenRotationTest extends DeviceTest {
    constructor() {
        super('rotation', 'Screen Rotation', 'Rotate device to test orientation detection');
        this.orientationHistory = [];
        this.transitionCount = 0;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting rotation test...');

        let isModernApi = !!(window.screen && window.screen.orientation);
        
        container.innerHTML = `
            <div style="text-align: center; padding: 20px;">
                <p style="margin-bottom: 20px; color: #64748b;">Current orientation:</p>
                <div id="orientation-display" style="font-size: 32px; font-weight: 700; color: #2563eb; margin-bottom: 20px; font-family: monospace;">
                    ${this.getOrientationType(isModernApi)}
                </div>
                <p style="color: #64748b; margin-bottom: 10px;">Rotate your device to test</p>
                <p id="rotation-count" style="font-size: 14px; color: #94a3b8; font-family: monospace;">
                    Rotations detected: 0
                </p>
                <p id="safe-area-info" style="font-size: 12px; color: #64748b; margin-top: 20px; font-family: monospace;">
                    Safe area top: ${this.getSafeAreaInset('top')}
                </p>
            </div>
        `;

        const orientationDisplay = container.querySelector('#orientation-display');
        const rotationCountEl = container.querySelector('#rotation-count');
        const safeAreaEl = container.querySelector('#safe-area-info');

        // Get initial orientation
        this.orientationHistory.push({
            type: this.getOrientationType(isModernApi),
            timestamp: Date.now()
        });

        const handleOrientationChange = () => {
            const currentType = this.getOrientationType(isModernApi);
            const current = {
                type: currentType,
                timestamp: Date.now()
            };
            
            // Only count if it actually changed
            if (this.orientationHistory.length > 0 && 
                this.orientationHistory[this.orientationHistory.length - 1].type === currentType) {
                return;
            }
            
            this.orientationHistory.push(current);
            this.transitionCount++;

            if (orientationDisplay) {
                orientationDisplay.textContent = current.type;
            }
            if (rotationCountEl) {
                rotationCountEl.textContent = 'Rotations detected: ' + this.transitionCount;
            }

            // Update safe area info
            const topInset = this.getSafeAreaInset('top');
            const rightInset = this.getSafeAreaInset('right');
            const bottomInset = this.getSafeAreaInset('bottom');
            const leftInset = this.getSafeAreaInset('left');
            
            if (safeAreaEl) {
                safeAreaEl.textContent = 'Safe area - T:' + topInset + ' R:' + rightInset + ' B:' + bottomInset + ' L:' + leftInset;
            }

            const progress = Math.min(50 + (this.transitionCount * 10), 90);
            this.reportProgress(wsClient, progress, 'Detected ' + current.type);
        };

        if (isModernApi) {
            window.screen.orientation.addEventListener('change', handleOrientationChange);
        } else {
            window.addEventListener('orientationchange', handleOrientationChange);
        }

        // Wait for user to rotate device (up to 30 seconds)
        const testDuration = 30000;
        const startTime = Date.now();

        return new Promise((resolve) => {
            const checkComplete = () => {
                const elapsed = Date.now() - startTime;
                const progress = 10 + (elapsed / testDuration) * 80;
                this.reportProgress(wsClient, Math.min(progress, 90), 'Waiting for rotation...');

                if (elapsed >= testDuration || this.transitionCount >= 2) {
                    if (isModernApi) {
                        window.screen.orientation.removeEventListener('change', handleOrientationChange);
                    } else {
                        window.removeEventListener('orientationchange', handleOrientationChange);
                    }

                    if (this.transitionCount >= 1) {
                        this.pass('Device orientation working - ' + this.transitionCount + ' transitions detected');
                        this.details.transitionCount = this.transitionCount;
                        this.details.orientationTypes = [...new Set(this.orientationHistory.map(o => o.type))];
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

    getOrientationType(isModernApi) {
        if (isModernApi && window.screen.orientation) {
            return window.screen.orientation.type;
        }
        
        if (typeof window.orientation !== 'undefined') {
            return Math.abs(window.orientation) === 90 ? 'landscape' : 'portrait';
        }
        
        return window.innerWidth > window.innerHeight ? 'landscape' : 'portrait';
    }

    getSafeAreaInset(side) {
        const value = getComputedStyle(document.documentElement).getPropertyValue('--safe-area-inset-' + side).trim();
        return value || '0px';
    }
}
