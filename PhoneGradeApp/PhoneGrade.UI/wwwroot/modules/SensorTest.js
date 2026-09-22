import { DeviceTest } from './DeviceTest.js';
import { VisualFeedback } from './VisualFeedback.js';

export class SensorTest extends DeviceTest {
    constructor() {
        super('sensor', 'Bewegingssensoren', 'Controleer gyroscoop en versnellingsmeter');
        this.accelThreshold = 1.5; 
        this.tiltThreshold = 15;
        this.accelWorking = false;
        this.gyroWorking = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Wachten op sensorgegevens...');

        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px;">
                    <h3 style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Bewegingssensoren</h3>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">
                        Beweeg en kantel het toestel in je hand.
                    </p>

                    <div id="sensor-permission-area" style="margin-bottom: 16px;">
                        <button id="btn-request-sensors" class="btn btn-primary" style="width: 100%;">Activeer Sensoren</button>
                    </div>

                    <div id="sensor-data-area" style="display: none; flex-direction: column; gap: 14px;">
                        <div style="background: var(--color-bg-tertiary); border-radius: 8px; padding: 14px;">
                            <div style="font-size: 13px; font-weight: 600; margin-bottom: 8px;">Gyroscoop (Kantelen)</div>
                            <div style="position: relative; width: 120px; height: 120px; border-radius: 50%; border: 2px solid #94a3b8; background: #0f172a; margin: 0 auto; overflow: hidden;">
                                <div id="spirit-bubble" style="position: absolute; top: 50%; left: 50%; width: 24px; height: 24px; border-radius: 50%; background: #38bdf8; transform: translate(-50%, -50%); transition: transform 0.08s ease-out;"></div>
                            </div>
                            <div id="gyro-status" style="font-size: 12px; font-weight: bold; color: var(--color-text-secondary); margin-top: 8px;">Kantel het toestel...</div>
                        </div>

                        <div style="background: var(--color-bg-tertiary); border-radius: 8px; padding: 14px;">
                            <div style="font-size: 13px; font-weight: 600; margin-bottom: 8px;">Versnellingsmeter (Schudden)</div>
                            <div style="height: 16px; border-radius: 8px; background: #334155; overflow: hidden; position: relative;">
                                <div id="shake-bar" style="position: absolute; left: 0; top: 0; bottom: 0; width: 0%; background: #38bdf8; transition: width 0.08s linear;"></div>
                            </div>
                            <div id="accel-status" style="font-size: 12px; font-weight: bold; color: var(--color-text-secondary); margin-top: 8px;">Schud het toestel...</div>
                        </div>
                    </div>

                    <div id="sensor-fallback-area" style="display: none; flex-direction: column; gap: 10px; margin-top: 14px;">
                        <p style="font-size: 13px; color: var(--color-warning);">Sensoren niet direct via browser uit te lezen. Roteert het scherm als je het toestel draait?</p>
                        <div style="display: flex; gap: 10px;">
                            <button id="sensor-manual-yes" class="btn btn-success" style="flex: 1;">Ja, roteert goed</button>
                            <button id="sensor-manual-no" class="btn btn-danger" style="flex: 1;">Nee, roteert niet</button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        const btnRequest = container.querySelector('#btn-request-sensors');
        const permissionArea = container.querySelector('#sensor-permission-area');
        const dataArea = container.querySelector('#sensor-data-area');
        const fallbackArea = container.querySelector('#sensor-fallback-area');
        const bubble = container.querySelector('#spirit-bubble');
        const shakeBar = container.querySelector('#shake-bar');
        const accelStatus = container.querySelector('#accel-status');
        const gyroStatus = container.querySelector('#gyro-status');
        const btnManualYes = container.querySelector('#sensor-manual-yes');
        const btnManualNo = container.querySelector('#sensor-manual-no');

        return new Promise((resolve) => {
            const handleMotion = (event) => {
                const a = event.acceleration || event.accelerationIncludingGravity;
                if (!a) return;
                const mag = Math.sqrt((a.x||0)*(a.x||0) + (a.y||0)*(a.y||0) + (a.z||0)*(a.z||0));
                
                if (mag > 0) {
                    shakeBar.style.width = Math.min(100, Math.round((mag / 10) * 100)) + '%';
                    if (mag > this.accelThreshold) {
                        this.accelWorking = true;
                        accelStatus.textContent = 'Schudden gedetecteerd';
                        accelStatus.style.color = 'var(--color-success)';
                        shakeBar.style.background = '#22c55e';
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
                    gyroStatus.style.color = 'var(--color-success)';
                    bubble.style.background = '#22c55e';
                    checkDone();
                }
            };

            const checkDone = () => {
                if (this.accelWorking && this.gyroWorking) {
                    window.removeEventListener('devicemotion', handleMotion);
                    window.removeEventListener('deviceorientation', handleOrientation);
                    this.pass('Bewegingssensoren (versnelling en gyroscoop) werken');
                    this.reportProgress(wsClient, 100, 'Sensortest geslaagd');
                    setTimeout(resolve, 800);
                }
            };

            const startListening = () => {
                permissionArea.style.display = 'none';
                dataArea.style.display = 'flex';

                window.addEventListener('devicemotion', handleMotion);
                window.addEventListener('deviceorientation', handleOrientation);

                // If after 4 seconds no hardware data is received, show visual orientation fallback
                setTimeout(() => {
                    if (!this.accelWorking && !this.gyroWorking) {
                        fallbackArea.style.display = 'flex';
                    }
                }, 4000);
            };

            btnRequest.onclick = async () => {
                try {
                    if (typeof DeviceMotionEvent !== 'undefined' && typeof DeviceMotionEvent.requestPermission === 'function') {
                        const res = await DeviceMotionEvent.requestPermission();
                        if (res === 'granted') {
                            startListening();
                            return;
                        }
                    }
                    if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function') {
                        const res = await DeviceOrientationEvent.requestPermission();
                        if (res === 'granted') {
                            startListening();
                            return;
                        }
                    }
                } catch (e) {
                    // Fallback to direct listener or manual fallback
                }
                startListening();
            };

            btnManualYes.onclick = () => {
                this.pass('Schermrotatie en oriëntatiesensor werken');
                resolve();
            };

            btnManualNo.onclick = () => {
                this.fail('Bewegingssensoren / gyroscoop reageren niet');
                resolve();
            };
        });
    }
}
