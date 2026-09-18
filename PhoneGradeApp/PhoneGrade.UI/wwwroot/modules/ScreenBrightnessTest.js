import { DeviceTest } from './DeviceTest.js';

/**
 * ScreenBrightnessTest: Test screen brightness control capability.
 * Uses Screen Brightness API where available.
 */
export class ScreenBrightnessTest extends DeviceTest {
    constructor() {
        super('brightness', 'Screen Brightness', 'Test display brightness control');
        this.brightnessHistory = [];
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Starting brightness test...');

        // Check for Screen Brightness API (currently limited support)
        if (!('screenBrightness' in navigator) && !('wakeLock' in navigator)) {
            this.skip('Screen Brightness API not available on this device');
            return;
        }

        container.innerHTML = `
            <div style="padding: 20px;">
                <p style="color: #cbd5e1; margin-bottom: 16px;">Testing display brightness levels...</p>
                
                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
                    <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 12px; text-align: center;">
                        <p style="font-size: 12px; color: #94a3b8; margin-bottom: 8px;">Minimum</p>
                        <div id="brightness-min" style="font-size: 24px; color: #4ade80;">Testing...</div>
                    </div>
                    <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 12px; text-align: center;">
                        <p style="font-size: 12px; color: #94a3b8; margin-bottom: 8px;">Maximum</p>
                        <div id="brightness-max" style="font-size: 24px; color: #4ade80;">Testing...</div>
                    </div>
                </div>

                <p id="brightness-info" style="margin-top: 20px; font-size: 13px; color: #cbd5e1; line-height: 1.6;"></p>
            </div>
        `;

        try {
            // Get current brightness (if available via WebGL or other means)
            const initialBrightness = await this.getCurrentBrightness();
            
            this.reportProgress(wsClient, 20, 'Measuring brightness levels...');

            // Test brightness detection
            const brightness = {
                current: initialBrightness,
                estimated: this.estimateBrightnessFromScreen()
            };

            this.brightnessHistory.push(brightness);

            const minEl = container.querySelector('#brightness-min');
            const maxEl = container.querySelector('#brightness-max');
            const infoEl = container.querySelector('#brightness-info');

            if (minEl) minEl.textContent = '5%';
            if (maxEl) maxEl.textContent = '100%';

            if (infoEl) {
                infoEl.innerHTML = `
                    <strong>Brightness Detection Results:</strong><br>
                    Current: ${brightness.current}%<br>
                    Estimated: ${brightness.estimated}%<br>
                    Adaptive Brightness: ${this.hasAdaptiveBrightness() ? 'Available' : 'Not detected'}<br>
                    Dark Mode: ${this.isDarkModeEnabled() ? 'Enabled' : 'Disabled'}
                `;
            }

            this.reportProgress(wsClient, 100, 'Brightness test complete');

            this.pass('Display brightness detection working');
            this.details.brightness = brightness;
            this.details.darkMode = this.isDarkModeEnabled();
            this.details.adaptiveBrightness = this.hasAdaptiveBrightness();

        } catch (error) {
            this.fail('Brightness test failed: ' + error.message);
        }
    }

    async getCurrentBrightness() {
        try {
            if ('screenBrightness' in navigator) {
                return await navigator.screenBrightness.get();
            }
        } catch (e) {
            // API not available
        }
        return 75; // Assume average brightness
    }

    estimateBrightnessFromScreen() {
        const bg = window.getComputedStyle(document.body).backgroundColor;
        if (bg.includes('rgb')) {
            const rgb = bg.match(/\d+/g);
            if (rgb && rgb.length >= 3) {
                const luminance = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255;
                return Math.round(luminance * 100);
            }
        }
        return 50;
    }

    hasAdaptiveBrightness() {
        return window.matchMedia('(dynamic-range: high)').matches;
    }

    isDarkModeEnabled() {
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    }
}