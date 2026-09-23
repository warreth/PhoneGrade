import { DeviceTest } from './DeviceTest.js';

/**
 * DisplayTest: Fullscreen cycling through pure colors (Red, Green, Blue, White, Black)
 * to detect dead pixels, burn-in, and backlight bleed. Includes brightness check.
 */
export class DisplayTest extends DeviceTest {
    constructor() {
        super('display', 'Display & Dead Pixels', 'Inspect screen for dead pixels and color uniformity');
        this.colors = [
            { name: 'Red', hex: '#ff0000' },
            { name: 'Green', hex: '#00ff00' },
            { name: 'Blue', hex: '#0000ff' },
            { name: 'White', hex: '#ffffff' },
            { name: 'Black', hex: '#000000' }
        ];
        this.currentIndex = 0;
        this.defectsFound = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting display inspection...');

        // First, show brightness instruction overlay before color checks
        const brightnessOk = await this.showBrightnessCheck(container);
        if (!brightnessOk) {
            this.fail('Screen brightness insufficient or contrast defect');
            return;
        }

        for (let i = 0; i < this.colors.length; i++) {
            this.currentIndex = i;
            const color = this.colors[i];
            const progress = ((i + 1) / this.colors.length) * 100;
            
            this.reportProgress(wsClient, progress, `Checking: ${color.name}`);
            
            const hasIssue = await this.showColor(color, i);
            if (hasIssue) {
                this.defectsFound = true;
            }
        }

        if (this.defectsFound) {
            this.fail('Screen defects (dead pixels, lines, or burn-in) reported by technician');
        } else {
            this.pass('Display passed full color uniformity check');
        }
        
        this.details.defects = this.defectsFound;
        this.details.colorsChecked = this.colors.map(c => c.name);
    }

    showBrightnessCheck(container) {
        return new Promise((resolve) => {
            container.innerHTML = `
                <div style="position: fixed; inset: 0; background: var(--color-bg-primary); z-index: 10000; overflow-y: auto; padding: 20px; display: flex; flex-direction: column; align-items: center; justify-content: center;">
                    <div style="max-width: 450px; width: 100%; background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md);">
                        <h3 style="font-size: 20px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Display Quality & Brightness</h3>
                        <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 20px;">
                            Manually swipe to Control Center / Quick Settings and set brightness to 100 percent while inspecting white, black, and colored screens.
                        </p>

                        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 20px;">
                            <div style="height: 100px; background: #ffffff; border: 2px solid #cbd5e1; border-radius: 8px; display: flex; align-items: center; justify-content: center; font-weight: bold; color: #000000;">
                                100% White
                            </div>
                            <div style="height: 100px; background: #000000; border: 2px solid #334155; border-radius: 8px; display: flex; align-items: center; justify-content: center; font-weight: bold; color: #ffffff;">
                                0% Black
                            </div>
                        </div>

                        <p style="font-size: 14px; font-weight: 600; color: var(--color-text-primary); margin-bottom: 16px;">
                            Is the screen bright and are both panels clearly visible?
                        </p>

                        <div style="display: flex; gap: 12px;">
                            <button id="brightness-yes" class="btn btn-success" style="flex: 1;">Yes, bright and sharp</button>
                            <button id="brightness-no" class="btn btn-danger" style="flex: 1;">No, too dim / defect</button>
                        </div>
                    </div>
                </div>
            `;

            const btnYes = container.querySelector('#brightness-yes');
            const btnNo = container.querySelector('#brightness-no');

            btnYes.onclick = () => {
                this.details.brightnessLevel = 'Confirmed OK by user';
                resolve(true);
            };

            btnNo.onclick = () => {
                this.details.brightnessLevel = 'Defective / Too dim';
                resolve(false);
            };
        });
    }

    showColor(color, index) {
        return new Promise((resolve) => {
            const overlay = document.createElement('div');
            overlay.className = 'display-test-fullscreen';
            overlay.style.cssText = `position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; z-index: 99999; background: ${color.hex}; touch-action: none; user-select: none;`;

            // Minimalist helper bubble that fades out after 2 seconds to not obstruct pixels
            const hint = document.createElement('div');
            hint.style.cssText = 'position: absolute; bottom: 30px; left: 50%; transform: translateX(-50%); background: rgba(0,0,0,0.75); color: #fff; padding: 10px 18px; border-radius: 20px; font-size: 13px; font-weight: 600; pointer-events: auto; display: flex; gap: 14px; align-items: center; box-shadow: 0 4px 12px rgba(0,0,0,0.4);';
            hint.innerHTML = `
                <span>${color.name} (${index + 1}/${this.colors.length})</span>
                <span style="opacity: 0.6;">Tap screen to advance</span>
                <button id="btn-report-defect" style="background: #ef4444; color: #fff; border: none; border-radius: 12px; padding: 4px 10px; font-size: 11px; font-weight: bold; cursor: pointer;">Flag Defect</button>
            `;

            overlay.appendChild(hint);
            document.body.appendChild(overlay);

            // Handle defect button
            const defectBtn = hint.querySelector('#report-report-defect') || hint.querySelector('#btn-report-defect');
            let defectReported = false;

            if (defectBtn) {
                defectBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    defectReported = true;
                    cleanup();
                    resolve(true);
                });
            }

            const advance = () => {
                cleanup();
                this.haptic.tap();
                resolve(defectReported);
            };

            const cleanup = () => {
                overlay.removeEventListener('click', advance);
                overlay.removeEventListener('touchend', advance);
                if (document.body.contains(overlay)) {
                    document.body.removeChild(overlay);
                }
            };

            // Tap anywhere on overlay to advance
            overlay.addEventListener('click', advance);
            overlay.addEventListener('touchend', advance);
        });
    }
}
