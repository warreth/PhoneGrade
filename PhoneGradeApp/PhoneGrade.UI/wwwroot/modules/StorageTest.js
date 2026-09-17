import { DeviceTest } from './DeviceTest.js';

/**
 * StorageTest: Check available storage capabilities and quotas.
 */
export class StorageTest extends DeviceTest {
    constructor() {
        super('storage', 'Storage capacity', 'Check available browser storage quota');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Estimating storage quota...');

        container.innerHTML = `
            <div style="padding: 20px;">
                <div id="storage-results" style="display: grid; grid-template-columns: 1fr; gap: 12px;">
                    <p style="text-align: center; color: #94a3b8;">Estimating storage limits...</p>
                </div>
            </div>
        `;

        try {
            if (navigator.storage && navigator.storage.estimate) {
                const estimate = await navigator.storage.estimate();
                
                // Convert bytes to GB/MB
                const quotaGB = (estimate.quota / (1024 * 1024 * 1024)).toFixed(2);
                const usageMB = (estimate.usage / (1024 * 1024)).toFixed(2);
                const percentage = ((estimate.usage / estimate.quota) * 100).toFixed(2);

                this.reportProgress(wsClient, 100, 'Storage estimation complete');

                const resultsEl = container.querySelector('#storage-results');
                resultsEl.innerHTML = `
                    <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 16px;">
                        <p style="font-size: 12px; color: #94a3b8; margin-bottom: 4px;">Total App Quota Limit</p>
                        <div style="font-size: 28px; font-weight: 700; color: #00d9ff; font-family: monospace;">${quotaGB} GB</div>
                    </div>
                    <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 16px;">
                        <p style="font-size: 12px; color: #94a3b8; margin-bottom: 4px;">Currently Used</p>
                        <div style="font-size: 28px; font-weight: 700; color: #facc15; font-family: monospace;">${usageMB} MB</div>
                    </div>
                    <div style="background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; padding: 16px;">
                        <p style="font-size: 12px; color: #94a3b8; margin-bottom: 4px;">Usage Percentage</p>
                        <div style="font-size: 28px; font-weight: 700; color: #4ade80; font-family: monospace;">${percentage}%</div>
                    </div>
                `;

                this.pass('Storage capabilities verified');
                this.details.quota = estimate.quota;
                this.details.usage = estimate.usage;
                
            } else {
                this.skip('Storage estimation API not supported');
            }
        } catch (error) {
            this.fail('Storage estimation failed: ' + error.message);
        }
    }
}