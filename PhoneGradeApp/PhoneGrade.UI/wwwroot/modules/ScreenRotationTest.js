import { t } from './i18n.js';
import { DeviceTest } from './DeviceTest.js';

export class ScreenRotationTest extends DeviceTest {
    constructor() {
        super('rotation', t('rotation.name'), t('rotation.description'));
        this.orientationHistory = [];
        this.transitionCount = 0;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('rotation.starting'));

        let isModernApi = !!(window.screen && window.screen.orientation);
        
        container.innerHTML = `
            <div class="rotation-panel">
                <p class="rotation-label">${t('rotation.currentOrientation')}</p>
                <div id="orientation-display" class="rotation-display">
                    ${this.getOrientationType(isModernApi)}
                </div>
                <p class="rotation-prompt">${t('rotation.rotatePrompt')}</p>
                <p id="rotation-count" class="rotation-count">
                    ${t('rotation.detected', { count: 0 })}
                </p>
                <p id="safe-area-info" class="rotation-safe-area">
                    ${t('rotation.safeAreaTop', { top: this.getSafeAreaInset('top') })}
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
                rotationCountEl.textContent = t('rotation.detected', { count: this.transitionCount });
            }

            // Update safe area info
            const topInset = this.getSafeAreaInset('top');
            const rightInset = this.getSafeAreaInset('right');
            const bottomInset = this.getSafeAreaInset('bottom');
            const leftInset = this.getSafeAreaInset('left');
            
            if (safeAreaEl) {
                safeAreaEl.textContent = t('rotation.safeArea', {
                    top: topInset,
                    right: rightInset,
                    bottom: bottomInset,
                    left: leftInset
                });
            }

            const progress = Math.min(50 + (this.transitionCount * 10), 90);
            this.reportProgress(wsClient, progress, t('rotation.detectedType', { type: current.type }));
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
            let stopped = false;
            let pollTimer = null;

            /* Detaches the listener and marks the test as no longer running. Both the
             * timeout below and the button come through here, so neither can finish the
             * test twice and leave a status that disagrees with the report. */
            const settle = () => {
                if (stopped) return;
                stopped = true;
                clearTimeout(pollTimer);

                if (isModernApi) {
                    window.screen.orientation.removeEventListener('change', handleOrientationChange);
                } else {
                    window.removeEventListener('orientationchange', handleOrientationChange);
                }
            };

            /* The operator is asked to rotate the phone and then has nothing to do but
             * watch the clock. Thirty seconds is a long time to stand there when the
             * rotation is never going to arrive, and a device with auto-rotate switched
             * off would otherwise be recorded as a failure it did not deserve. So the
             * test says what it found instead.
             *
             * Offered inside the promise because settle has to exist before the button
             * can reach it. Offered above it, the button marked the test failed and
             * then threw on a resolve that was not in scope. */
            this.offerFaultButton(container, t('rotation.faultButton'), () => {
                this.fail(t('rotation.failed'));
                settle();
                resolve();
            });

            const checkComplete = () => {
                if (stopped) return;

                const elapsed = Date.now() - startTime;
                const progress = 10 + (elapsed / testDuration) * 80;
                this.reportProgress(wsClient, Math.min(progress, 90), t('rotation.waiting'));

                if (elapsed >= testDuration || this.transitionCount >= 2) {
                    settle();

                    if (this.transitionCount >= 1) {
                        this.pass(t('rotation.passed', { count: this.transitionCount }));
                        this.details.transitionCount = this.transitionCount;
                        this.details.orientationTypes = [...new Set(this.orientationHistory.map(o => o.type))];
                    } else {
                        this.fail(t('rotation.failed'));
                    }

                    resolve();
                } else {
                    pollTimer = setTimeout(checkComplete, 500);
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
