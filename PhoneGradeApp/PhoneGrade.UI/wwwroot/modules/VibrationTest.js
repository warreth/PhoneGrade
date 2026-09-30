import { DeviceTest } from './DeviceTest.js';
import { t } from './i18n.js';

/**
 * What the button asks the browser to run: three short pulses with gaps.
 *
 * Long enough to feel on a phone and short enough that the operator is not left
 * wondering whether anything was going to happen at all.
 */
const PULSE = [250, 100, 250, 100, 250];

/**
 * VibrationTest: does the motor run, and did the browser even try?
 *
 * Two questions that used to be one. navigator.vibrate() answers with a
 * boolean, and the old step threw the answer away: on a phone where the browser
 * refuses the call, the operator pressed the button, nothing happened, and
 * there was no way to tell a browser that will not vibrate from a motor that
 * will not run. The refusal is now written on the card next to the question the
 * operator is answering.
 *
 * Where the browser has no vibration API at all, the card says so and hands the
 * step over as a manual check. That is the phone position, but it is a browser
 * position too: Firefox on Android ships without it, and a desktop browser has
 * no motor behind it either way. Saying "manual" is what stops a step the page
 * cannot drive from being graded as hardware it never touched.
 */
export class VibrationTest extends DeviceTest {
    constructor() {
        super('vibration', t('vibration.stepName'), t('vibration.stepDescription'));
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('vibration.progressTesting'));

        const hasApi = typeof navigator.vibrate === 'function';

        this.details.browserApi = hasApi ? 'navigator.vibrate' : 'handmatig';

        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px;">
                    <h3 style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">${t('vibration.heading')}</h3>

                    ${hasApi ? `
                        <p class="step-hint">${t('vibration.pulseHint')}</p>
                        <button id="btn-vibe-pulse" class="btn btn-primary" style="width: 100%; margin-bottom: 8px;">${t('vibration.pulseButton')}</button>
                        <p id="vibe-echo" class="vibe-echo" role="status" aria-live="polite">${t('vibration.echoIdle')}</p>
                    ` : `
                        <div class="step-note">
                            <strong>${t('vibration.manualCheck')}</strong>
                            ${t('vibration.manualIntro')}
                            <strong>${t('vibration.manualSwitch')}</strong>
                            ${t('vibration.manualSwitchTail')}
                        </div>
                    `}

                    <p class="step-question" style="margin: 16px 0 12px;">
                        ${t('vibration.question')}
                    </p>

                    <div class="step-actions">
                        <button id="btn-vibe-yes" class="btn btn-success">${t('vibration.yesButton')}</button>
                        <button id="btn-vibe-no" class="btn btn-danger">${t('vibration.noButton')}</button>
                    </div>
                </div>
            </div>
        `;

        const echo = container.querySelector('#vibe-echo');

        const btnPulse = container.querySelector('#btn-vibe-pulse');
        if (btnPulse) {
            btnPulse.onclick = () => {
                this.details.vibrationPulsePressed = true;

                let accepted = false;
                try {
                    // The boolean is the whole point of the call. true means the
                    // browser took the pattern; false means nothing will vibrate,
                    // whatever the phone is capable of.
                    accepted = navigator.vibrate(PULSE) === true;
                } catch (e) {
                    accepted = false;
                    this.details.vibrationError = e && e.name ? e.name : String(e);
                }

                this.details.browserAccepted = accepted;

                if (echo) {
                    echo.className = accepted ? 'vibe-echo is-accepted' : 'vibe-echo is-refused';
                    echo.textContent = accepted
                        ? t('vibration.echoAccepted')
                        : t('vibration.echoRefused');
                }

                this.reportProgress(wsClient, 50, accepted
                    ? t('vibration.progressAccepted')
                    : t('vibration.progressRefused'));
            };
        }

        return new Promise((resolve) => {
            container.querySelector('#btn-vibe-yes').onclick = () => {
                this.pass(t('vibration.passVerdict'));
                this.reportProgress(wsClient, 100, t('vibration.progressPassed'));
                resolve();
            };

            container.querySelector('#btn-vibe-no').onclick = () => {
                this.fail(t('vibration.failVerdict'));
                this.reportProgress(wsClient, 100, t('vibration.progressFailed'));
                resolve();
            };
        });
    }
}
