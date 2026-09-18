import { DeviceTest } from './DeviceTest.js';

/**
 * DisplayTest: Fullscreen cycling through colors (Red, Green, Blue, White, Black)
 * to detect dead pixels and backlight bleeding.
 */
export class DisplayTest extends DeviceTest {
    constructor() {
        super('display', 'Display Test', 'Controleer op dode pixels en backlight bleeding');
        this.colors = [
            { name: 'Rood', hex: '#ff0000' },
            { name: 'Groen', hex: '#00ff00' },
            { name: 'Blauw', hex: '#0000ff' },
            { name: 'Wit', hex: '#ffffff' },
            { name: 'Zwart', hex: '#000000' }
        ];
        this.currentIndex = 0;
        this.userConfirmed = [];
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starten...');

        for (let i = 0; i < this.colors.length; i++) {
            this.currentIndex = i;
            const color = this.colors[i];
            const progress = ((i + 1) / this.colors.length) * 100;
            
            this.reportProgress(wsClient, progress - 20, `Kleur: ${color.name}`);
            
            await this.showColor(container, color, i);
            
            this.userConfirmed.push(color.name);
        }

        this.pass('Alle kleuren getoond - visueel geinspecteerd');
        this.details.colorsTested = this.userConfirmed;
    }

    async showColor(container, color, index) {
        return new Promise((resolve) => {
            // Create fullscreen overlay
            const overlay = document.createElement('div');
            overlay.className = 'display-test-fullscreen';
            overlay.style.cssText = `position: fixed; top: 0; left: 0; width: 100%; height: 100%; z-index: 9999; background: ${color.hex}; display: flex; align-items: center; justify-content: center; flex-direction: column;`;

            // Info text
            const info = document.createElement('div');
            info.className = 'display-test-info';
            info.style.cssText = 'position: absolute; bottom: 40px; left: 50%; transform: translateX(-50%); background: rgba(0,0,0,0.8); padding: 16px 24px; border-radius: 8px; color: white; font-size: 16px; text-align: center;';
            info.innerHTML = `
                <p style="margin-bottom: 12px;">Kleur: <strong>${color.name}</strong> (${index + 1}/${this.colors.length})</p>
                <p style="margin-bottom: 16px; font-size: 14px; opacity: 0.8;">Controleer op dode pixels of vlekken</p>
                <button id="next-color-btn" style="padding: 12px 24px; font-size: 16px; background: #00ff88; color: #1a1a1a; border: none; border-radius: 8px; cursor: pointer;">Volgende</button>
            `;

            overlay.appendChild(info);
            document.body.appendChild(overlay);

            // Handle next button
            const btn = info.querySelector('#next-color-btn');
            btn.addEventListener('click', () => {
                document.body.removeChild(overlay);
                resolve();
            }, { once: true });
        });
    }
}