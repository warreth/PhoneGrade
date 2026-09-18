import { DeviceTest } from './DeviceTest.js';
import { VisualFeedback } from './VisualFeedback.js';

export class SensorTest extends DeviceTest {
    constructor() {
        super('sensor', 'Motion Sensors', 'Test accelerometer and gyroscope');
        this.accelThreshold = 2.0; 
        this.tiltThreshold = 20;
        this.accelWorking = false;
        this.gyroWorking = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Awaiting sensor data...');

        container.innerHTML = `
            <div style="text-align: center; margin-bottom: 20px;">
                <div style="font-size: 16px; font-weight: bold; margin-bottom: 6px;">Motion Sensors</div>
                <p class="test-instructions">Shake and tilt the device in your hand.</p>
            </div>
            
            <div id="sensor-permission-area" style="text-align: center; margin-bottom: 24px;">
                <button id="btn-request-sensors" class="btn btn-primary" style="padding: 12px 24px; font-weight: bold;">Enable Motion Sensors</button>
                <p style="font-size: 11px; margin-top: 8px; color: var(--color-text-secondary);">Required for iOS Safari</p>
            </div>
            
            <div id="sensor-data-area" style="display: none; flex-direction: column; gap: 16px; align-items: center;">
                
                <!-- Gyroscope Spirit Level Visualizer -->
                <div style="width: 100%; max-width: 250px; background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 20px; text-align: center;">
                    <div style="font-size: 14px; font-weight: bold; margin-bottom: 16px; color: var(--color-text);">Gyrometer (Tilt)</div>
                    <div style="position: relative; width: 150px; height: 150px; border-radius: 50%; border: 3px solid var(--color-border); background: var(--color-bg-tertiary); margin: 0 auto; overflow: hidden; box-shadow: inset 0 4px 10px rgba(0,0,0,0.5);">
                        <!-- Crosshairs -->
                        <div style="position: absolute; top: 50%; left: 0; right: 0; height: 1px; background: rgba(255,255,255,0.1);"></div>
                        <div style="position: absolute; top: 0; bottom: 0; left: 50%; width: 1px; background: rgba(255,255,255,0.1);"></div>
                        
                        <!-- The Spirit Bubble -->
                        <div id="spirit-bubble" style="position: absolute; top: 50%; left: 50%; width: 30px; height: 30px; border-radius: 50%; background: var(--color-accent); transform: translate(-50%, -50%); box-shadow: 0 0 15px var(--color-accent); transition: transform 0.1s ease-out;"></div>
                    </div>
                    <div id="gyro-status" style="font-size: 14px; font-weight: bold; color: var(--color-warning); margin-top: 16px;">Tilt device to activate</div>
                </div>

                <!-- Accelerometer Shake Indicator -->
                <div style="width: 100%; max-width: 250px; background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 20px; text-align: center;">
                    <div style="font-size: 14px; font-weight: bold; margin-bottom: 16px; color: var(--color-text);">Accelerometer (Shake)</div>
                    <div id="shake-bar-container" style="width: 100%; height: 20px; border-radius: 10px; background: var(--color-bg-tertiary); overflow: hidden; position: relative;">
                        <div id="shake-bar" style="position: absolute; left: 50%; top: 0; bottom: 0; width: 0%; background: var(--color-accent); transform: translateX(-50%); transition: width 0.1s linear;"></div>
                    </div>
                    <div id="accel-status" style="font-size: 14px; font-weight: bold; color: var(--color-warning); margin-top: 16px;">Shake device to activate</div>
                </div>

            </div>
        `;

        const btnRequest = container.querySelector('#btn-request-sensors');
        const permissionArea = container.querySelector('#sensor-permission-area');
        const dataArea = container.querySelector('#sensor-data-area');
        
        const bubble = container.querySelector('#spirit-bubble');
        const shakeBar = container.querySelector('#shake-bar');
        
        const accelStatus = container.querySelector('#accel-status');
        const gyroStatus = container.querySelector('#gyro-status');

        return new Promise((resolve) => {
            const handleMotion = (event) => {
                if (!event.accelerationIncludingGravity && !event.acceleration) return;

                // Prefer pure acceleration (w/o gravity), fallback to with gravity and subtract approx baseline
                const accelX = (event.acceleration && event.acceleration.x) || 0;
                const accelY = (event.acceleration && event.acceleration.y) || 0;
                const accelZ = (event.acceleration && event.acceleration.z) || 0;
                
                const magnitude = Math.sqrt(accelX*accelX + accelY*accelY + accelZ*accelZ);
                
                // Visualizer
                if (magnitude > 0) {
                    const widthPct = Math.min(100, (magnitude / 10) * 100);
                    shakeBar.style.width = `${widthPct}%`;
                    
                    if (magnitude > this.accelThreshold && !this.accelWorking) {
                        this.accelWorking = true;
                        accelStatus.textContent = 'Shake Detected';
                        accelStatus.style.color = 'var(--color-success)';
                        shakeBar.style.background = 'var(--color-success)';
                        this.haptic.progress();
                        checkCompletion();
                    }
                }
            };

            const handleOrientation = (event) => {
                if (event.beta === null || event.gamma === null) return;

                // Limit beta and gamma to bounds for the bubble
                // beta is front-to-back tilt (-180 to 180)
                // gamma is left-to-right tilt (-90 to 90)
                const maxTilt = 45;
                const normalizedBeta = Math.max(-maxTilt, Math.min(maxTilt, event.beta));
                const normalizedGamma = Math.max(-maxTilt, Math.min(maxTilt, event.gamma));

                // Map tilt to pixels (radius is 75px, max translation is roughly 60px to stay inside)
                const transY = (normalizedBeta / maxTilt) * 60;
                const transX = (normalizedGamma / maxTilt) * 60;

                bubble.style.transform = `translate(calc(-50% + ${transX}px), calc(-50% + ${transY}px))`;

                if (!this.gyroWorking && (Math.abs(event.beta) > this.tiltThreshold || Math.abs(event.gamma) > this.tiltThreshold)) {
                    this.gyroWorking = true;
                    gyroStatus.textContent = 'Tilt Detected';
                    gyroStatus.style.color = 'var(--color-success)';
                    bubble.style.background = 'var(--color-success)';
                    bubble.style.boxShadow = '0 0 15px var(--color-success)';
                    this.haptic.progress();
                    checkCompletion();
                }
            };

            const checkCompletion = () => {
                if (this.accelWorking && this.gyroWorking) {
                    window.removeEventListener('devicemotion', handleMotion);
                    window.removeEventListener('deviceorientation', handleOrientation);
                    this.pass('Motion sensors functional');
                    this.details.accelerometer = true;
                    this.details.orientation = true;
                    setTimeout(() => resolve(), 800);
                }
            };

            const initSensors = () => {
                permissionArea.style.display = 'none';
                dataArea.style.display = 'flex';
                VisualFeedback.fadeIn(dataArea);
                
                window.addEventListener('devicemotion', handleMotion);
                window.addEventListener('deviceorientation', handleOrientation);
                
                setTimeout(() => {
                    if (!this.accelWorking || !this.gyroWorking) {
                        window.removeEventListener('devicemotion', handleMotion);
                        window.removeEventListener('deviceorientation', handleOrientation);
                        
                        if (this.accelWorking || this.gyroWorking) {
                            this.fail(`Partial failure: ${this.accelWorking ? 'Gyroscope missing' : 'Accelerometer missing'}`);
                        } else {
                            this.fail('No motion data received');
                        }
                        this.details.accelerometer = this.accelWorking;
                        this.details.orientation = this.gyroWorking;
                        resolve();
                    }
                }, 15000);
            };

            btnRequest.addEventListener('click', () => {
                if (typeof DeviceMotionEvent !== 'undefined' && typeof DeviceMotionEvent.requestPermission === 'function') {
                    DeviceMotionEvent.requestPermission()
                        .then(permissionState => {
                            if (permissionState === 'granted') {
                                initSensors();
                            } else {
                                this.fail('Sensor permission denied by user');
                                resolve();
                            }
                        })
                        .catch(e => {
                            this.fail('Failed to request sensor permissions');
                            resolve();
                        });
                } else {
                    initSensors();
                }
            });
            
            // Auto-start if permission API is not required (e.g., Android Chrome)
            if (typeof DeviceMotionEvent === 'undefined' || typeof DeviceMotionEvent.requestPermission !== 'function') {
                btnRequest.textContent = 'Begin Test';
                setTimeout(initSensors, 500); // Auto-start slightly delayed
            }
        });
    }
}
