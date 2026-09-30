import { DeviceTest } from './DeviceTest.js';
import { t } from './i18n.js';

export class CallTest extends DeviceTest {
    constructor() {
        super('call', t('call.stepName'), t('call.stepDescription'));
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('call.progressSetup'));

        // Get configurable phone number from URL params or default to IMEI query
        const params = new URLSearchParams(window.location.search);
        const testNumber = params.get('testPhoneNumber') || '*#06#';

        container.innerHTML = `
            <div style="padding: 16px;">
                <h3 style="color: var(--color-accent); margin-bottom: 16px;">${t('call.heading')}</h3>
                <p class="test-instructions" style="margin-bottom: 24px;">
                    ${t('call.intro')}
                </p>

                <div style="background: var(--color-bg-secondary); border-radius: var(--radius-lg); padding: 24px; text-align: center; border: 1px solid var(--color-border); margin-bottom: 24px;">
                    <a href="tel:${testNumber}" id="make-call-btn" class="btn btn-primary" style="display: inline-block; width: 100%; margin-bottom: 16px; text-decoration: none;">
                        ${t('call.openDialer')}
                    </a>
                    <p style="font-size: 13px; color: var(--color-text-tertiary);">${t('call.dialerHint')}</p>
                </div>

                <div id="call-feedback" style="display: none; flex-direction: column; gap: 16px;">
                    <p style="text-align: center; font-weight: 600;">${t('call.didOpenQuestion')}</p>
                    <div style="display: flex; gap: 12px;">
                        <button id="btn-call-yes" class="btn btn-success" style="flex: 1; background: var(--color-success); color: #000; border: none;">${t('call.yesButton')}</button>
                        <button id="btn-call-no" class="btn btn-error" style="flex: 1; background: var(--color-error); color: #fff; border: none;">${t('call.noButton')}</button>
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
                this.reportProgress(wsClient, 50, t('call.progressAwaiting'));
            });

            btnYes.onclick = () => {
                this.pass(t('call.passDialerOpened'));
                this.details.supported = true;
                this.reportProgress(wsClient, 100, t('call.progressComplete'));
                resolve();
            };

            btnNo.onclick = () => {
                this.fail(t('call.failNotSupported'));
                this.details.supported = false;
                this.reportProgress(wsClient, 100, t('call.progressComplete'));
                resolve();
            };
        });
    }
}
