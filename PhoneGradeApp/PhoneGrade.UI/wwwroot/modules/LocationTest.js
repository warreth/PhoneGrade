import { DeviceTest } from './DeviceTest.js';

export class LocationTest extends DeviceTest {
    constructor() {
        super('location', 'GPS / Location', 'Test Geolocation API functionality');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Awaiting location permission...');

        container.innerHTML = `
            <h3 style="color: var(--color-accent); margin-bottom: 16px;">GPS & Location</h3>
            <p class="test-instructions">Allow location access when prompted to verify GPS hardware.</p>
            
            <div style="background: var(--color-bg-secondary); border-radius: var(--radius-lg); padding: 24px; margin-top: 24px; border: 1px solid var(--color-border); text-align: center;">
                <div id="loc-spinner" style="display: inline-block; width: 30px; height: 30px; border: 3px solid rgba(0, 217, 255, 0.3); border-radius: 50%; border-top-color: var(--color-accent); animation: spin 1s ease-in-out infinite; margin-bottom: 16px;"></div>
                <p id="loc-status" style="color: var(--color-text-secondary);">Requesting coordinates...</p>
                <div id="loc-data" style="margin-top: 16px; font-family: monospace; color: var(--color-success); font-size: 14px; display: none;"></div>
            </div>
        `;

        const statusEl = container.querySelector('#loc-status');
        const spinnerEl = container.querySelector('#loc-spinner');
        const dataEl = container.querySelector('#loc-data');

        return new Promise((resolve) => {
            if (!navigator.geolocation) {
                statusEl.textContent = 'Geolocation API not supported';
                statusEl.style.color = 'var(--color-error)';
                spinnerEl.style.display = 'none';
                this.skip('Geolocation API not supported by browser');
                resolve();
                return;
            }

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
                    resolve();
                },
                (error) => {
                    spinnerEl.style.display = 'none';
                    statusEl.style.color = 'var(--color-error)';
                    
                    switch(error.code) {
                        case error.PERMISSION_DENIED:
                            statusEl.textContent = 'User denied location request';
                            this.skip('User denied location permission');
                            break;
                        case error.POSITION_UNAVAILABLE:
                            statusEl.textContent = 'Location information unavailable';
                            this.fail('GPS Hardware reported position unavailable');
                            break;
                        case error.TIMEOUT:
                            statusEl.textContent = 'Location request timed out';
                            this.fail('GPS Hardware timed out acquiring fix');
                            break;
                        default:
                            statusEl.textContent = 'An unknown error occurred';
                            this.fail('Unknown geolocation error: ' + error.message);
                            break;
                    }
                    this.details.errorCode = error.code;
                    resolve();
                },
                {
                    enableHighAccuracy: true,
                    timeout: 15000,
                    maximumAge: 0
                }
            );
        });
    }
}