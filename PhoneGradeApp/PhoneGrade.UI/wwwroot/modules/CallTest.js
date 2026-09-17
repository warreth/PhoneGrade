import { DeviceTest } from './DeviceTest.js';

export class CallTest extends DeviceTest {
    constructor() {
        super('call', 'SIM / Calling', 'Verify cellular call capabilities');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Setting up call interface...');

        const testNumber = '*#06#';

        container.innerHTML = `
            <div style="padding: 16px;">
                <h3 style="color: var(--color-accent); margin-bottom: 16px;">Call Capability Test</h3>
                <p class="test-instructions" style="margin-bottom: 24px;">
                    We will attempt to open the native phone dialer. 
                    This checks if the device recognizes a SIM card and supports cellular calls.
                </p>

                <div style="background: var(--color-bg-secondary); border-radius: var(--radius-lg); padding: 24px; text-align: center; border: 1px solid var(--color-border); margin-bottom: 24px;">
                    <a href="tel:${testNumber}" id="make-call-btn" class="btn btn-primary" style="display: inline-block; width: 100%; margin-bottom: 16px; text-decoration: none;">
                        Open Dialer
                    </a>
                    <p style="font-size: 13px; color: var(--color-text-tertiary);">If supported, the dialer should open.</p>
                </div>

                <div id="call-feedback" style="display: none; flex-direction: column; gap: 16px;">
                    <p style="text-align: center; font-weight: 600;">Did the dialer open successfully?</p>
                    <div style="display: flex; gap: 12px;">
                        <button id="btn-call-yes" class="btn btn-success" style="flex: 1; background: var(--color-success); color: #000; border: none;">Yes, it opened</button>
                        <button id="btn-call-no" class="btn btn-error" style="flex: 1; background: var(--color-error); color: #fff; border: none;">No / Not supported</button>
                    </div>
                </div>
            </div>
        `;

        const callBtn = container.querySelector('#make-call-btn');
        const feedbackSection = container.querySelector('#call-feedback');
        const btnYes = container.querySelector('#btn-call-yes');
        const btnNo = container.querySelector('#btn-call-no');

        return new Promise((resolve) => {
            callBtn.addEventListener('click', () => {
                feedbackSection.style.display = 'flex';
                this.reportProgress(wsClient, 50, 'Awaiting user confirmation...');
            });

            btnYes.onclick = () => {
                this.pass('Dialer opened, cellular call supported');
                this.details.supported = true;
                this.reportProgress(wsClient, 100, 'Test complete');
                resolve();
            };

            btnNo.onclick = () => {
                this.fail('Dialer did not open or not supported');
                this.details.supported = false;
                this.reportProgress(wsClient, 100, 'Test complete');
                resolve();
            };
        });
    }
}