import { DeviceTest } from './DeviceTest.js';
import { CAPABILITY } from './MediaCapability.js';

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
        super('location', 'GPS / Locatie', 'Controleer de Geolocation API');
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
        this.reportProgress(wsClient, 0, 'Wachten op locatietoegang...');

        if (!hasGeolocation()) {
            return this.reportNoApi(wsClient, container);
        }

        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">GPS en locatie</h3>
                    <div class="step-card">
                        <p class="step-lead">
                            Sta locatietoegang toe wanneer de browser erom vraagt. Zet
                            het toestel liefst buiten of bij een raam, dan doet de
                            eerste fix er het langst over.
                        </p>

                        <button id="btn-request-location" class="btn btn-primary step-block">
                            Locatietoegang aanvragen
                        </button>

                        <div id="location-status-area" class="step-stack" hidden>
                            <div id="loc-spinner" class="loc-spinner"></div>
                            <p id="loc-status">Wachten op coördinaten...</p>
                            <p id="loc-data" class="loc-readout" hidden></p>
                        </div>

                        <div id="location-error-area" class="step-stack" hidden>
                            <p class="step-note" id="loc-error-msg"></p>
                            <ol class="fix-steps" id="loc-fix-steps"></ol>
                            <div class="step-actions">
                                <button id="btn-retry-location" class="btn btn-primary">Opnieuw proberen</button>
                                <button id="btn-loc-give-up" class="btn btn-secondary">Locatietoegang lukt niet</button>
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
                graceNoteEl.textContent = `Over ${left} seconden gaat deze stap verder.`;
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
                                this.skip('Locatietoegang niet verleend binnen de ingestelde tijd');
                                settle();
                            }
                        }, 20000);
                        return;
                    }
                    if (!settled) {
                        this.skip('Locatietoegang niet verleend binnen de ingestelde tijd');
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
                statusEl.textContent = 'Wachten op coördinaten...';
                statusEl.style.color = '';
                this.reportProgress(wsClient, 30, 'GPS-fix ophalen...');

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
                            ? 'Nauwkeurige positie gevonden'
                            : 'Positie gevonden, maar grof';
                        statusEl.style.color = accurate
                            ? 'var(--color-success-text)'
                            : 'var(--color-warning-text)';

                        dataEl.hidden = false;
                        dataEl.textContent =
                            `Breedtegraad: ${latitude.toFixed(5)}\n` +
                            `Lengtegraad: ${longitude.toFixed(5)}\n` +
                            `Nauwkeurigheid: ${accuracy.toFixed(1)} m`;

                        this.details.latitude = latitude;
                        this.details.longitude = longitude;
                        this.details.accuracy = accuracy;
                        this.details.accuracyGrade = accurate ? 'high' : 'low';

                        this.pass(accurate
                            ? `Nauwkeurige GPS-fix op ${accuracy.toFixed(1)} m`
                            : `GPS-fix op ${accuracy.toFixed(1)} m, grover dan 100 m (binnen?)`);

                        this.reportProgress(wsClient, 100, 'GPS-test afgerond');
                        setTimeout(settle, 1500);
                    },
                    (error) => {
                        requestInFlight = false;
                        spinnerEl.hidden = true;
                        statusArea.hidden = true;
                        errorArea.hidden = false;
                        this.details.errorCode = error.code;
                        this.showLocationError(errorMsgEl, fixStepsEl, error, grantedOnce);
                        this.reportProgress(wsClient, grantedOnce ? 60 : 30, 'GPS-fix niet verkregen');

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
                this.details.failure = 'Locatietoegang niet verleend';
                this.skip('Locatietoegang niet verleend; GPS is niet gecontroleerd');
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
                errorMsgEl.textContent = 'De browser gaf geen toestemming voor de locatie.';
                fixStepsEl.innerHTML = [
                    isAndroid
                        ? 'Open de browserinstellingen en zet <b>Locatie</b> op <b>Toestaan</b> voor deze site.'
                        : 'Open de website-instellingen van de browser en zet <b>Locatie</b> op <b>Toestaan</b>.',
                    isAndroid
                        ? 'Zet de locatiedienst van het toestel aan.'
                        : 'Zet de locatiedienst van het toestel aan.',
                    'Druk op <b>Opnieuw proberen</b>.'
                ].map(s => `<li>${s}</li>`).join('');
                break;
            case error.POSITION_UNAVAILABLE:
                errorMsgEl.textContent = 'Het toestel gaf geen positie door.';
                fixStepsEl.innerHTML = [
                    'Ga naar buiten of zet het toestel bij een raam.',
                    'Zet de gps aan in de instellingen van het toestel.',
                    'Druk op <b>Opnieuw proberen</b>.'
                ].map(s => `<li>${s}</li>`).join('');
                break;
            case error.TIMEOUT:
                errorMsgEl.textContent = 'Binnen 15 seconden geen positie ontvangen.';
                fixStepsEl.innerHTML = [
                    'Ga naar buiten of zet het toestel bij een raam.',
                    'Druk op <b>Opnieuw proberen</b>.'
                ].map(s => `<li>${s}</li>`).join('');
                break;
            default:
                errorMsgEl.textContent = 'Er ging iets anders mis bij het ophalen van de locatie.';
                fixStepsEl.innerHTML = ['Druk op <b>Opnieuw proberen</b>.'].map(s => `<li>${s}</li>`).join('');
                break;
        }

        // A refusal after a fix that worked once is worth saying out loud: the
        // hardware is fine, and the only thing left is the browser's answer.
        if (grantedOnce && error.code === error.PERMISSION_DENIED) {
            errorMsgEl.textContent += ' De GPS werkte eerder in deze run, dus dit is een instelling van de browser.';
        }
    }

    /** The one case nothing can be done about, and the only automatic skip left. */
    async reportNoApi(wsClient, container) {
        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">GPS en locatie</h3>
                    <div class="step-card">
                        <p class="step-note">
                            Deze browser heeft geen Geolocation API. De GPS van het
                            toestel is daarmee niet te controleren.
                        </p>
                    </div>
                </div>
            </div>
        `;

        this.details.capabilityGap = CAPABILITY.MISSING;
        await this.reportCapabilityGap(wsClient, 'navigator.geolocation', 'missing');
        this.skip('Geolocation API ontbreekt in deze browser');
        this.reportProgress(wsClient, 100, 'Geolocation niet beschikbaar');
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
            return 'GPS gaf geen positie door (position unavailable)';
        case error.TIMEOUT:
            return 'GPS gaf binnen 15 seconden geen fix (timeout)';
        default:
            return 'Onbekende locatiefout: ' + (error.message || String(error.code));
    }
}
