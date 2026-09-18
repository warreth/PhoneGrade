import { DeviceTest } from './DeviceTest.js';

/**
 * DisplayTest: Fullscreen cycling through pure colors (Red, Green, Blue, White, Black)
 * to detect dead pixels, burn-in, and backlight bleed.
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
