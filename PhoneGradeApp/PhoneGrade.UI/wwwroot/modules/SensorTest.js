import { DeviceTest } from './DeviceTest.js';
import {
    CAPABILITY,
    classifyMediaError,
    explainMediaError
} from './MediaCapability.js';

/**
 * The accelerometer and the gyroscope, read from the browser's motion events.
 *
 * The motion API is the one that is genuinely optional. Plenty of browsers do
 * not expose it at all, and on iOS it is behind a prompt that can be refused.
 * Either way the old step called skip() and moved on, which threw away a check
 * that was still available: a phone whose screen rotates when you turn it has a
 * working orientation sensor, and the operator can see that with their own eyes.
 *
 * So nothing here skips on its own any more. When the live sensors cannot be
 * read, the operator is shown the rotation check and asked to judge it, and the
 * note records that the verdict came from a human and not from the hardware. The
 * capability gap is still reported to the desktop, because the grade should know
 * the browser was not the one that answered.
 */
export class SensorTest extends DeviceTest {
    /**
     * How long the step listens before offering the hand-turned check.
     *
     * Long enough for a sensor that needs a gesture and a moment to settle, short
     * enough that the operator is not left watching an empty bubble.
     */
    static LISTEN_WINDOW_MS = 4000;

    constructor() {
        super('sensor', 'Bewegingssensoren', 'Controleer gyroscoop en versnellingsmeter');
        this.accelThreshold = 1.5;
        this.tiltThreshold = 15;
        this.accelWorking = false;
        this.gyroWorking = false;
        this.manualCheckUsed = false;
    }

    /**
     * A prompt to answer, four seconds of listening, and then a person reading a
     * card and physically turning the phone. 90 s is not enough for the last part
     * when the phone has to be picked up, and a timeout here would fail a sensor
     * that was never the problem.
     */
    getFailsafeMs() {
        return 120000;
    }

    reset() {
        super.reset();
        this.accelWorking = false;
        this.gyroWorking = false;
        this.manualCheckUsed = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Wachten op sensorgegevens...');

        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">Bewegingssensoren</h3>

                    <div class="step-card">
                        <p class="step-lead">
                            Beweeg en kantel het toestel in je hand. De gyroscoop laat
                            het bolletje bewegen en de versnellingsmeter vult de balk.
                        </p>

                        <div id="sensor-permission-area">
                            <button id="btn-request-sensors" class="btn btn-primary step-block">Meet de sensoren</button>
                        </div>

                        <div id="sensor-data-area" class="step-stack" hidden>
                            <div class="sensor-panel">
                                <div class="sensor-panel-title">Gyroscoop (kantelen)</div>
                                <div class="sensor-bowl">
                                    <div id="spirit-bubble" class="sensor-bubble"></div>
                                </div>
                                <div id="gyro-status" class="sensor-status">Kantel het toestel...</div>
                            </div>

                            <div class="sensor-panel">
                                <div class="sensor-panel-title">Versnellingsmeter (schudden)</div>
                                <div class="sensor-track">
                                    <div id="shake-bar" class="sensor-fill"></div>
                                </div>
                                <div id="accel-status" class="sensor-status">Schud het toestel...</div>
                            </div>
                        </div>

                        <div id="sensor-fallback-area" class="step-stack" hidden>
                            <p class="step-note" id="sensor-fallback-reason"></p>
                            <p class="step-question">
                                Draai het toestel een kwartslag. Roteert het scherm mee?
                            </p>
                            <div class="step-actions">
                                <button id="sensor-manual-yes" class="btn btn-success">Ja, het scherm roteert</button>
                                <button id="sensor-manual-no" class="btn btn-danger">Nee, er gebeurt niets</button>
                            </div>
                            <p class="step-hint">
                                Deze controle is met de hand gedaan, niet door de sensor
                                uitgelezen. Dat staat ook zo op het rapport.
                            </p>
                            <button id="btn-retry-sensors" class="btn btn-secondary">Toch de sensoren opnieuw proberen</button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        const btnRequest = container.querySelector('#btn-request-sensors');
        const permissionArea = container.querySelector('#sensor-permission-area');
        const dataArea = container.querySelector('#sensor-data-area');
        const fallbackArea = container.querySelector('#sensor-fallback-area');
        const fallbackReason = container.querySelector('#sensor-fallback-reason');
        const bubble = container.querySelector('#spirit-bubble');
        const shakeBar = container.querySelector('#shake-bar');
        const accelStatus = container.querySelector('#accel-status');
        const gyroStatus = container.querySelector('#gyro-status');
        const btnManualYes = container.querySelector('#sensor-manual-yes');
        const btnManualNo = container.querySelector('#sensor-manual-no');
        const btnRetry = container.querySelector('#btn-retry-sensors');

        return new Promise((resolve) => {
            let settled = false;
            let listening = false;

            const settle = (outcome, notes) => {
                if (settled) return;
                settled = true;
                stopListening();
                if (outcome === 'pass') this.pass(notes);
                else this.fail(notes);
                resolve();
            };

            const handleMotion = (event) => {
                const a = event.acceleration || event.accelerationIncludingGravity;
                if (!a) return;
                const mag = Math.sqrt((a.x || 0) * (a.x || 0) + (a.y || 0) * (a.y || 0) + (a.z || 0) * (a.z || 0));

                if (mag > 0) {
                    shakeBar.style.width = Math.min(100, Math.round((mag / 10) * 100)) + '%';
                    if (mag > this.accelThreshold) {
                        this.accelWorking = true;
                        accelStatus.textContent = 'Schudden gedetecteerd';
                        accelStatus.style.color = 'var(--color-text-primary)';
                        shakeBar.classList.add('is-good');
                        checkDone();
                    }
                }
            };

            const handleOrientation = (event) => {
                if (event.beta === null && event.gamma === null) return;
                const beta = Math.max(-45, Math.min(45, event.beta || 0));
                const gamma = Math.max(-45, Math.min(45, event.gamma || 0));

                const transX = (gamma / 45) * 45;
                const transY = (beta / 45) * 45;
                bubble.style.transform = 'translate(calc(-50% + ' + transX + 'px), calc(-50% + ' + transY + 'px))';

                if (Math.abs(beta) > this.tiltThreshold || Math.abs(gamma) > this.tiltThreshold) {
                    this.gyroWorking = true;
                    gyroStatus.textContent = 'Kanteling gedetecteerd';
                    gyroStatus.style.color = 'var(--color-text-primary)';
                    bubble.classList.add('is-good');
                    checkDone();
                }
            };

            const checkDone = () => {
                if (!(this.accelWorking && this.gyroWorking)) return;

                this.details.accelerometer = true;
                this.details.gyroscope = true;
                this.details.manualCheckUsed = false;

                this.reportProgress(wsClient, 100, 'Sensortest geslaagd');
                settle('pass', 'Versnellingsmeter en gyroscoop reageren op beweging');
            };

            const stopListening = () => {
                if (!listening) return;
                window.removeEventListener('devicemotion', handleMotion);
                window.removeEventListener('deviceorientation', handleOrientation);
                listening = false;
            };

            const startListening = () => {
                permissionArea.hidden = true;
                fallbackArea.hidden = true;
                dataArea.hidden = false;
                listening = true;

                window.addEventListener('devicemotion', handleMotion);
                window.addEventListener('deviceorientation', handleOrientation);

                // If nothing arrives, the manual check is offered rather than the
                // step being declared un-testable. It is additive: the listeners
                // stay on, so a slow sensor that wakes up later still produces the
                // real verdict.
                setTimeout(() => {
                    if (!settled && !this.accelWorking && !this.gyroWorking) {
                        showManualFallback(
                            'De browser gaf geen sensorgegevens door. De sensoren kunnen alsnog reageren.');
                    }
                }, SensorTest.LISTEN_WINDOW_MS);
            };

            const showManualFallback = (reason) => {
                this.manualCheckUsed = true;
                permissionArea.hidden = true;
                dataArea.hidden = false;
                fallbackArea.hidden = false;
                fallbackReason.textContent = reason;
                this.reportProgress(wsClient, 60, 'Handmatige controle in plaats van sensorgegevens');
            };

            const logMissingApi = async (missingApi, reason) => {
                if (!wsClient || !wsClient.sessionId) return;
                try {
                    const ua = navigator.userAgent;
                    let osVersion = 'Unknown';
                    if (/iPhone|iPad|iPod/.test(ua)) {
                        osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || 'iOS Unknown';
                    } else if (/Android/.test(ua)) {
                        osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || 'Android Unknown';
                    }

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
                    console.warn('Could not report the missing sensor API:', e);
                }
            };

            /**
             * Asks for the sensor permission where there is one to ask for.
             *
             * On Android there is no prompt and the events simply start once the
             * page has had a gesture. On iOS the request has to come from a click,
             * which is why this runs from the button and not on load.
             *
             * @returns {Promise<{granted: boolean, missing: string|null, reason: string, kind: string}>}
             */
            const requestSensorAccess = async () => {
                const missing = [];

                for (const [name, ctor] of [
                    ['DeviceMotionEvent', typeof DeviceMotionEvent !== 'undefined' ? DeviceMotionEvent : undefined],
                    ['DeviceOrientationEvent', typeof DeviceOrientationEvent !== 'undefined' ? DeviceOrientationEvent : undefined]
                ]) {
                    if (!ctor) {
                        missing.push(name);
                        continue;
                    }

                    if (typeof ctor.requestPermission === 'function') {
                        try {
                            const result = await ctor.requestPermission();
                            if (result !== 'granted') {
                                await logMissingApi(name, 'denied');
                                return {
                                    granted: false,
                                    missing: name,
                                    kind: CAPABILITY.DENIED,
                                    reason: explainMediaError({ kind: CAPABILITY.DENIED }, 'bewegingssensor')
                                };
                            }
                        } catch (e) {
                            const classified = classifyMediaError(e);
                            await logMissingApi(name, 'denied');
                            return {
                                granted: false,
                                missing: name,
                                kind: classified.kind,
                                reason: explainMediaError(classified, 'bewegingssensor')
                            };
                        }
                    }
                }

                if (missing.length) {
                    await logMissingApi(missing[0], 'missing');
                    return {
                        granted: false,
                        missing: missing[0],
                        kind: CAPABILITY.MISSING,
                        reason: `Deze browser ondersteunt ${missing.join(' en ')} niet.`
                    };
                }

                return { granted: true, missing: null, kind: null, reason: '' };
            };

            const begin = async () => {
                const access = await requestSensorAccess();

                if (access.granted) {
                    startListening();
                    return;
                }

                // Not a skip. The rotation check is available whatever the browser
                // does with its motion events, and it is the operator's to judge.
                this.details.capabilityGap = access.missing;
                showManualFallback(access.reason);
            };

            btnRequest.onclick = begin;
            btnRetry.onclick = begin;

            btnManualYes.onclick = () => {
                this.details.manualCheckUsed = true;
                this.details.rotationConfirmed = true;
                this.reportProgress(wsClient, 100, 'Rotatie bevestigd');
                settle('pass', 'Schermrotatie werkt; sensoren niet rechtstreeks uitgelezen');
            };

            btnManualNo.onclick = () => {
                this.details.manualCheckUsed = true;
                this.details.rotationConfirmed = false;
                this.reportProgress(wsClient, 100, 'Rotatie reageert niet');
                settle('fail', 'Scherm roteert niet en de sensoren gaven geen gegevens door');
            };
        });
    }
}
