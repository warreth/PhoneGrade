import { DeviceTest } from './DeviceTest.js';

/**
 * BatteryStatusTest: Check battery info if available.
 */
export class BatteryStatusTest extends DeviceTest {
    constructor() {
        super('battery', 'Battery Info', 'Read battery status and power mode');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Accessing battery API...');

        container.innerHTML = `
            <div style="padding: 20px;">
                <div id="battery-results" style="display: grid; grid-template-columns: 1fr; gap: 12px;">
                    <p style="text-align: center; color: #94a3b8;">Testing battery capabilities...</p>
                </div>
            </div>
        `;

        try {
            let battery = null;
            let batteryLevel = 'Unknown';
            let isCharging = 'Unknown';
            let chargingTime = 'Unknown';
            let dischargingTime = 'Unknown';

            // Check if Battery Status API is available (deprecated in some browsers, but still works in Chrome)
            if ('getBattery' in navigator) {
                battery = await navigator.getBattery();
                batteryLevel = (battery.level * 100).toFixed(0) + '%';
                isCharging = battery.charging ? 'Yes' : 'No';
                chargingTime = battery.chargingTime === Infinity ? 'Unknown' : battery.chargingTime + 's';
                dischargingTime = battery.dischargingTime === Infinity ? 'Unknown' : battery.dischargingTime + 's';
            } else {
                this.skip('Battery Status API not supported by this browser (common on iOS Safari)');
                return;
            }

            this.reportProgress(wsClient, 100, 'Battery info read');

            const resultsEl = container.querySelector('#battery-results');
            resultsEl.innerHTML = `
                <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 16px; display: flex; justify-content: space-between; align-items: center;">
                    <span style="color: #cbd5e1;">Level</span>
                    <span style="font-size: 24px; font-weight: 700; color: ${battery.level > 0.2 ? '#4ade80' : '#f87171'};">${batteryLevel}</span>
                </div>
                <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 16px; display: flex; justify-content: space-between; align-items: center;">
                    <span style="color: #cbd5e1;">Charging</span>
                    <span style="font-weight: 600; color: ${battery.charging ? '#4ade80' : '#facc15'};">${isCharging}</span>
                </div>
                <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 16px; display: flex; justify-content: space-between; align-items: center;">
                    <span style="color: #cbd5e1;">Charge Time</span>
                    <span style="font-weight: 600; color: #00d9ff;">${chargingTime}</span>
                </div>
            `;

            this.pass('Battery information retrieved successfully');
            this.details.level = battery.level;
            this.details.charging = battery.charging;
            this.details.chargingTime = battery.chargingTime;
            this.details.dischargingTime = battery.dischargingTime;

        } catch (error) {
            this.skip('Battery API available but access failed: ' + error.message);
        }
    }
}