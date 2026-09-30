import { DeviceTest } from './DeviceTest.js';
import { CAPABILITY } from './MediaCapability.js';
import { t } from './i18n.js';

/**
 * How long the operator can go and change the location setting.
 *
 * Long enough to unlock the phone, find the browser's site permissions, switch
 * location on and come back, short enough that the suite is not held up by a
 * step nobody intends to finish. It is measured from the first refusal and is
 * never restarted, so pressing retry cannot keep the step alive for ever.
 */
const RETRY_GRACE_MS = 90000;

/** How often the remaining grace is rewritten on screen. */
const COUNTDOWN_TICK_MS = 1000;

/**
 * A window of time that is opened once and then only counted down.
 *
 * The old step started a fresh 45 s every time the browser said no, so an
 * operator on a phone that kept refusing could hold the suite on this step by
 * pressing retry. Here arming is refused the second time, which is what stops
 * the window from sliding, and the test can check that without a clock.
 */
export class RetryDeadline {
    constructor(graceMs) {
        this.graceMs = graceMs;
        this.at = null;
    }

    /** @returns {boolean} true only the first time it is opened. */
    arm(now = Date.now()) {
        if (this.at !== null) return false;
        this.at = now + this.graceMs;
        return true;
    }

    get isArmed() {
        return this.at !== null;
    }

    remainingMs(now = Date.now()) {
        if (this.at === null) return this.graceMs;
        return Math.max(0, this.at - now);
    }

    remainingSeconds(now = Date.now()) {
        return Math.ceil(this.remainingMs(now) / 1000);
    }

    clear() {
        this.at = null;
    }
}

export class LocationTest extends DeviceTest {
    constructor() {
        super('location', t('gps.stepName'), t('gps.stepDescription'));
    }

    /**
     * The browser's own timeout is 15 s, and the operator may then be away for the
     * grace period. 90 s of runner failsafe ended the step while they were still
     * in the settings.
     */
    getFailsafeMs() {
        return RETRY_GRACE_MS + 90000;
    }

    reset() {
        super.reset();
        this.details = {};
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('gps.waitingForPermission'));

        if (!hasGeolocation()) {
            return this.reportNoApi(wsClient, container);
        }

        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('gps.title')}</h3>
                    <div class="step-card">
                        <p class="step-lead">
                            ${t('gps.outdoorsHint')}
                        </p>

                        <button id="btn-request-location" class="btn btn-primary step-block">
                            ${t('gps.requestButton')}
                        </button>

                        <div id="location-status-area" class="step-stack" hidden>
                            <div id="loc-spinner" class="loc-spinner"></div>
                            <p id="loc-status">${t('gps.waitingForCoordinates')}</p>
                            <p id="loc-data" class="loc-readout" hidden></p>
                        </div>

                        <div id="location-error-area" class="step-stack" hidden>
                            <p class="step-note" id="loc-error-msg"></p>
                            <ol class="fix-steps" id="loc-fix-steps"></ol>
                            <div class="step-actions">
                                <button id="btn-retry-location" class="btn btn-primary">${t('gps.retryButton')}</button>
                                <button id="btn-loc-give-up" class="btn btn-secondary">${t('gps.giveUpButton')}</button>
                            </div>
                            <p class="step-hint" id="loc-grace-note" hidden></p>
                        </div>
                    </div>
                </div>
            </div>
        `;

        const btnRequest = container.querySelector('#btn-request-location');
        const statusArea = container.querySelector('#location-status-area');
        const errorArea = container.querySelector('#location-error-area');
        const spinnerEl = container.querySelector('#loc-spinner');
        const statusEl = container.querySelector('#loc-status');
        const dataEl = container.querySelector('#loc-data');
        const errorMsgEl = container.querySelector('#loc-error-msg');
        const fixStepsEl = container.querySelector('#loc-fix-steps');
        const graceNoteEl = container.querySelector('#loc-grace-note');
        const btnRetry = container.querySelector('#btn-retry-location');
        const btnGiveUp = container.querySelector('#btn-loc-give-up');

        return new Promise((resolve) => {
            let settled = false;
            let requestInFlight = false;
            let grantedOnce = false;

            // Opened on the first refusal and only counted down from there. A
            // retry re-asks the browser but never re-opens the window.
            const grace = new RetryDeadline(RETRY_GRACE_MS);
            let graceTimer = null;
            let countdownTimer = null;

            const settle = () => {
                if (settled) return;
                settled = true;
                if (graceTimer) clearTimeout(graceTimer);
                if (countdownTimer) clearInterval(countdownTimer);
                graceTimer = null;
                countdownTimer = null;
                resolve();
            };

            const showRemaining = () => {
                const left = grace.remainingSeconds();
                graceNoteEl.hidden = left <= 0;
                graceNoteEl.textContent = t('gps.graceNote', { seconds: left });
            };

            const armGrace = () => {
                if (!grace.arm()) return;

                showRemaining();
                countdownTimer = setInterval(showRemaining, COUNTDOWN_TICK_MS);
                graceTimer = setTimeout(() => {
                    graceNoteEl.hidden = true;
                    // A request the operator started is allowed to come back first,
                    // so a fix that arrives in the last seconds is not thrown away.
                    if (requestInFlight) {
                        graceTimer = setTimeout(() => {
                            if (!settled) {
                                this.skip(t('gps.timedOut'));
                                settle();
                            }
                        }, 20000);
                        return;
                    }
                    if (!settled) {
                        this.skip(t('gps.timedOut'));
                        settle();
                    }
                }, RETRY_GRACE_MS);
            };

            const requestLocation = () => {
                requestInFlight = true;
                btnRequest.hidden = true;
                errorArea.hidden = true;
                statusArea.hidden = false;
                spinnerEl.hidden = false;
                dataEl.hidden = true;
                statusEl.textContent = t('gps.waitingForCoordinates');
                statusEl.style.color = '';
                this.reportProgress(wsClient, 30, t('gps.acquiringFix'));

                navigator.geolocation.getCurrentPosition(
                    (position) => {
                        requestInFlight = false;
                        grantedOnce = true;
                        if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
                        if (graceTimer) { clearTimeout(graceTimer); graceTimer = null; }
                        grace.clear();
                        graceNoteEl.hidden = true;

                        const { latitude, longitude, accuracy } = position.coords;
                        const accurate = accuracy <= 100;

                        spinnerEl.hidden = true;
                        statusEl.textContent = accurate
                            ? t('gps.accuratePosition')
                            : t('gps.coarsePosition');
                        statusEl.style.color = accurate
                            ? 'var(--color-success-text)'
                            : 'var(--color-warning-text)';

                        dataEl.hidden = false;
                        const readout =
                            t('gps.latitude', { degrees: latitude.toFixed(5) }) + '\n' +
                            t('gps.longitude', { degrees: longitude.toFixed(5) }) + '\n' +
                            t('location.accuracy', { metres: accuracy.toFixed(1) });
                        dataEl.textContent = readout;

                        this.details.latitude = latitude;
                        this.details.longitude = longitude;
                        this.details.accuracy = accuracy;
                        this.details.accuracyGrade = accurate ? 'high' : 'low';

                        this.pass(accurate
                            ? t('gps.passAccurate', { metres: accuracy.toFixed(1) })
                            : t('gps.passCoarse', { metres: accuracy.toFixed(1) }));

                        this.reportProgress(wsClient, 100, t('gps.fixComplete'));
                        setTimeout(settle, 1500);
                    },
                    (error) => {
                        requestInFlight = false;
                        spinnerEl.hidden = true;
                        statusArea.hidden = true;
                        errorArea.hidden = false;
                        this.details.errorCode = error.code;
                        this.showLocationError(errorMsgEl, fixStepsEl, error, grantedOnce);
                        this.reportProgress(wsClient, grantedOnce ? 60 : 30, t('gps.fixFailed'));

                        // A refusal is a question to the operator, not an answer.
                        // A timeout or an unavailable position is a finding about the
                        // hardware, and it stands on its own.
                        if (error.code === error.PERMISSION_DENIED) {
                            armGrace();
                        } else {
                            this.details.failure = describeFailure(error);
                            this.fail(this.details.failure);
                            setTimeout(settle, 2500);
                        }
                    },
                    {
                        enableHighAccuracy: true,
                        timeout: 15000,
                        maximumAge: 0
                    }
                );
            };

            btnRequest.onclick = requestLocation;

            // A retry re-asks the browser. It does not touch the deadline and it
            // does not clear the verdict either, so a step that failed on a
            // timeout can still come back as a pass before the grace runs out.
            btnRetry.onclick = requestLocation;

            btnGiveUp.onclick = () => {
                this.details.failure = t('gps.giveUpFailure');
                this.skip(t('gps.giveUpSkip'));
                settle();
            };
        });
    }

    /**
     * Says what went wrong and, where there is something to do about it, lists
     * the settings to change in the order they have to be changed.
     *
     * The old step said "enable location in settings and retry" and stopped
     * there. On Android the browser keeps its own answer: turning location on in
     * the quick settings is not enough, the site permission in the browser's
     * settings has to be set to allow, or the retry returns the same refusal
     * without ever asking again. That is what made retry look broken.
     */
    showLocationError(errorMsgEl, fixStepsEl, error, grantedOnce) {
        const isAndroid = /Android/.test(navigator.userAgent);

        switch (error.code) {
            case error.PERMISSION_DENIED:
                errorMsgEl.textContent = t('gps.permissionDenied');
                fixStepsEl.innerHTML = [
                    isAndroid
                        ? t('gps.fixPermissionAndroid')
                        : t('gps.fixPermissionBrowser'),
                    isAndroid
                        ? t('gps.fixEnableLocationService')
                        : t('gps.fixEnableLocationService'),
                    t('gps.fixRetry')
                ].map(s => `<li>${s}</li>`).join('');
                break;
            case error.POSITION_UNAVAILABLE:
                errorMsgEl.textContent = t('gps.positionUnavailable');
                fixStepsEl.innerHTML = [
                    t('gps.fixGoOutside'),
                    t('gps.fixEnableGps'),
                    t('gps.fixRetry')
                ].map(s => `<li>${s}</li>`).join('');
                break;
            case error.TIMEOUT:
                errorMsgEl.textContent = t('gps.timeoutMessage');
                fixStepsEl.innerHTML = [
                    t('gps.fixGoOutside'),
                    t('gps.fixRetry')
                ].map(s => `<li>${s}</li>`).join('');
                break;
            default:
                errorMsgEl.textContent = t('gps.unknownError');
                fixStepsEl.innerHTML = [t('gps.fixRetry')].map(s => `<li>${s}</li>`).join('');
                break;
        }

        // A refusal after a fix that worked once is worth saying out loud: the
        // hardware is fine, and the only thing left is the browser's answer.
        if (grantedOnce && error.code === error.PERMISSION_DENIED) {
            errorMsgEl.textContent += ' ' + t('gps.workedEarlier');
        }
    }

    /** The one case nothing can be done about, and the only automatic skip left. */
    async reportNoApi(wsClient, container) {
        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('gps.title')}</h3>
                    <div class="step-card">
                        <p class="step-note">
                            ${t('gps.noApi')}
                        </p>
                    </div>
                </div>
            </div>
        `;

        this.details.capabilityGap = CAPABILITY.MISSING;
        await this.reportCapabilityGap(wsClient, 'navigator.geolocation', 'missing');
        this.skip(t('gps.noApiSkip'));
        this.reportProgress(wsClient, 100, t('gps.noApiProgress'));
    }

    async reportCapabilityGap(wsClient, missingApi, reason) {
        if (!wsClient || !wsClient.sessionId) return;
        try {
            const ua = navigator.userAgent;
            let osVersion = 'Unknown';
            if (/Android/.test(ua)) osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || 'Android Unknown';
            else if (/iPhone|iPad|iPod/.test(ua)) osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || 'iOS Unknown';

            await fetch(`${wsClient.baseUrl}/api/pwa/log-warning`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sessionId: wsClient.sessionId,
                    missingApi,
                    userAgent: ua,
                    osVersion,
                    reason
                })
            });
        } catch (e) {
            console.warn('Could not report the missing geolocation API:', e);
        }
    }
}

/** True only when there is something to call. */
function hasGeolocation() {
    return !!(navigator.geolocation && typeof navigator.geolocation.getCurrentPosition === 'function');
}

/** The sentence that goes on the label for a failed fix. */
function describeFailure(error) {
    switch (error.code) {
        case error.POSITION_UNAVAILABLE:
            return t('gps.failureUnavailable');
        case error.TIMEOUT:
            return t('gps.failureTimeout');
        default:
            return t('gps.failureUnknown', { detail: error.message || String(error.code) });
    }
}
