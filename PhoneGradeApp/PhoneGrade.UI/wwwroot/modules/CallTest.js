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
            <div class="call-panel">
                <h3 class="call-heading">${t('call.heading')}</h3>
                <p class="test-instructions call-intro">
                    ${t('call.intro')}
                </p>

                <div class="call-card">
                    <a href="tel:${testNumber}" id="make-call-btn" class="btn btn-primary call-button">
                        ${t('call.openDialer')}
                    </a>
                    <p class="call-hint">${t('call.dialerHint')}</p>
                </div>

                <div id="call-feedback" class="call-feedback" hidden>
                    <p class="call-question">${t('call.didOpenQuestion')}</p>
                    <div class="call-actions">
                        <button id="btn-call-yes" class="btn btn-success">${t('call.yesButton')}</button>
                        <button id="btn-call-no" class="btn btn-danger">${t('call.noButton')}</button>
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
                feedbackSection.hidden = false;
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
