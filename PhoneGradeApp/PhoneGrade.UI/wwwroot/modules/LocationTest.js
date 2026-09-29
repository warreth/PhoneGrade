import { DeviceTest } from './DeviceTest.js';

// How long the retry button stays useful after a permission denial. Long enough
// to flip the setting in the OS and come back, short enough that the suite never
// sits on a single step.
const DENIAL_RETRY_GRACE_MS = 45000;

export class LocationTest extends DeviceTest {
    constructor() {
        super('location', 'GPS / Location', 'Test Geolocation API functionality');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Awaiting location permission...');

        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px;">
                    <h3 style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">GPS & Location</h3>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">
                        Allow location access when prompted to verify GPS hardware.
                    </p>

                    <div id="location-permission-area" style="margin-bottom: 16px;">
                        <button id="btn-request-location" class="btn btn-primary" style="width: 100%; padding: 12px; font-size: 16px; font-weight: bold;">Request Location Access</button>
                    </div>
                    
                    <div id="location-status-area" style="display: none;">
                        <div id="loc-spinner" style="display: inline-block; width: 30px; height: 30px; border: 3px solid rgba(0, 217, 255, 0.3); border-radius: 50%; border-top-color: var(--color-accent); animation: spin 1s ease-in-out infinite; margin-bottom: 16px;"></div>
                        <p id="loc-status" style="color: var(--color-text-secondary);">Requesting coordinates...</p>
                        <div id="loc-data" style="margin-top: 16px; font-family: monospace; color: var(--color-success); font-size: 14px; display: none;"></div>
                    </div>

                    <div id="location-error-area" style="display: none; margin-top: 16px;">
                        <p id="loc-error-msg" style="color: var(--color-error); margin-bottom: 12px;">Location access denied</p>
                        <button id="btn-retry-location" class="btn btn-secondary" style="width: 100%;">Retry</button>
                    </div>
                </div>
            </div>
        `;

        const btnRequest = container.querySelector('#btn-request-location');
        const permissionArea = container.querySelector('#location-permission-area');
        const statusArea = container.querySelector('#location-status-area');
        const errorArea = container.querySelector('#location-error-area');
        const statusEl = container.querySelector('#loc-status');
        const spinnerEl = container.querySelector('#loc-spinner');
        const dataEl = container.querySelector('#loc-data');
        const errorMsgEl = container.querySelector('#loc-error-msg');
        const btnRetry = container.querySelector('#btn-retry-location');

        // reason is 'missing' when the browser has no geolocation at all, and
        // 'denied' when it does but the operator refused the prompt. Only the
        // former is a capability gap that should cost the device a grade.
        const logMissingApi = async (missingApi, reason = 'missing') => {
            if (wsClient && wsClient.sessionId) {
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
                            missingApi: missingApi,
                            userAgent: ua,
                            osVersion: osVersion,
                            reason: reason
                        })
                    });
                } catch (e) {
                    console.error('Failed to report missing API', e);
                }
            }
        };

        // The promise that run() returns is created below, but requestLocation()
        // runs from a click long after that point, so the resolver is held here
        // where both sides can see it. Referencing `resolve` directly inside
        // requestLocation() was a ReferenceError that left the promise pending
        // and stalled the whole suite on the GPS step.
        let settleRun = null;
        let finishTimer = null;

        // Settles after an optional delay. Any pending delay is replaced, so a
        // retry that succeeds can take over from an earlier denial timeout.
        const finish = (delayMs = 0) => {
            if (finishTimer) {
                clearTimeout(finishTimer);
                finishTimer = null;
            }
            const release = () => {
                finishTimer = null;
                if (settleRun) settleRun();
            };
            if (delayMs <= 0) release();
            else finishTimer = setTimeout(release, delayMs);
        };

        const requestLocation = () => {
            permissionArea.style.display = 'none';
            statusArea.style.display = 'block';
            errorArea.style.display = 'none';

            navigator.geolocation.getCurrentPosition(
                (position) => {
                    const { latitude, longitude, accuracy } = position.coords;
                    
                    spinnerEl.style.display = 'none';
                    statusEl.textContent = 'Location acquired successfully';
                    statusEl.style.color = 'var(--color-success)';
                    
                    dataEl.style.display = 'block';
                    dataEl.innerHTML = `
                        Lat: ${latitude.toFixed(5)}<br>
                        Lon: ${longitude.toFixed(5)}<br>
                        Accuracy: ${accuracy.toFixed(1)}m
                    `;

                    if (accuracy <= 100) {
                        this.pass(`High accuracy GPS fix (${accuracy.toFixed(1)}m)`);
                    } else {
                        this.pass(`Low accuracy GPS fix (${accuracy.toFixed(1)}m) - Indoors?`);
                    }

                    this.details.latitude = latitude;
                    this.details.longitude = longitude;
                    this.details.accuracy = accuracy;
                    this.reportProgress(wsClient, 100, 'GPS test complete');
                    
                    finish(1500);
                },
                async (error) => {
                    spinnerEl.style.display = 'none';
                    statusArea.style.display = 'none';
                    errorArea.style.display = 'block';
                    
                    switch(error.code) {
                        case error.PERMISSION_DENIED:
                            errorMsgEl.textContent = 'Location permission denied. Enable location access in settings and retry.';
                            // The API is present, the operator refused it. Report
                            // it as a denial so it does not cap the grade.
                            await logMissingApi('navigator.geolocation', 'denied');
                            this.skip('User denied location permission');
                            // Keep the retry button usable for a while so the operator
                            // can switch on location in settings, but never wait
                            // indefinitely: the suite has to move on.
                            finish(DENIAL_RETRY_GRACE_MS);
                            break;
                        case error.POSITION_UNAVAILABLE:
                            errorMsgEl.textContent = 'Location information unavailable';
                            this.fail('GPS Hardware reported position unavailable');
                            finish(2000);
                            break;
                        case error.TIMEOUT:
                            errorMsgEl.textContent = 'Location request timed out';
                            this.fail('GPS Hardware timed out acquiring fix');
                            finish(2000);
                            break;
                        default:
                            errorMsgEl.textContent = 'An unknown error occurred';
                            this.fail('Unknown geolocation error: ' + error.message);
                            finish(2000);
                            break;
                    }
                    this.details.errorCode = error.code;
                },
                {
                    enableHighAccuracy: true,
                    timeout: 15000,
                    maximumAge: 0
                }
            );
        };

        return new Promise((resolve) => {
            settleRun = resolve;

            if (!navigator.geolocation) {
                permissionArea.style.display = 'none';
                errorArea.style.display = 'block';
                errorMsgEl.textContent = 'Geolocation API not supported';
                // A real capability gap, so it is reported as 'missing'. The
                // endpoint dedupes, so overlapping with the startup scan is fine.
                logMissingApi('navigator.geolocation', 'missing');
                this.skip('Geolocation API not supported by browser');
                finish(2000);
                return;
            }

            btnRequest.onclick = () => {
                requestLocation();
            };

            btnRetry.onclick = () => {
                requestLocation();
            };
        });
    }
}
