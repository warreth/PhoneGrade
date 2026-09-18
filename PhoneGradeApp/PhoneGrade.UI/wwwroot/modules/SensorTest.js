import { DeviceTest } from './DeviceTest.js';

export class SensorTest extends DeviceTest {
    constructor() {
        super('sensor', 'Motion Sensors', 'Test accelerometer and device orientation');
        this.threshold = 1.0; 
        this.accelWorking = false;
        this.gyroWorking = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Awaiting sensor permission...');

        container.innerHTML = `
            <h3 style="color: var(--color-accent); margin-bottom: 16px;">Motion Sensors</h3>
            <p class="test-instructions">Shake and tilt your device. iOS requires permission to access motion sensors.</p>
            
            <div id="sensor-permission-area" style="text-align: center; margin-bottom: 24px;">
                <button id="btn-request-sensors" class="btn btn-primary">Enable Motion Sensors</button>
            </div>
            
            <div id="sensor-data-area" style="display: none; grid-template-columns: 1fr; gap: 16px; margin-top: 16px;">
                <div style="background: var(--color-bg-secondary); border-radius: var(--radius-lg); padding: 16px; border: 1px solid var(--color-border); text-align: center;">
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 8px;">Accelerometer (Shake)</p>
                    <div id="accel-status" style="font-size: 18px; font-weight: bold; color: var(--color-warning);">Waiting for movement...</div>
                </div>
                <div style="background: var(--color-bg-secondary); border-radius: var(--radius-lg); padding: 16px; border: 1px solid var(--color-border); text-align: center;">
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 8px;">Orientation (Tilt)</p>
                    <div id="gyro-status" style="font-size: 18px; font-weight: bold; color: var(--color-warning);">Waiting for tilt...</div>
                </div>
            </div>
        `;

        const btnRequest = container.querySelector('#btn-request-sensors');
        const permissionArea = container.querySelector('#sensor-permission-area');
        const dataArea = container.querySelector('#sensor-data-area');
        const accelStatus = container.querySelector('#accel-status');
        const gyroStatus = container.querySelector('#gyro-status');

        return new Promise((resolve) => {
            const handleMotion = (event) => {
                if (event.acceleration && (Math.abs(event.acceleration.x) > this.threshold || 
                                           Math.abs(event.acceleration.y) > this.threshold || 
                                           Math.abs(event.acceleration.z) > this.threshold)) {
                    this.accelWorking = true;
                    accelStatus.textContent = 'Movement Detected!';
                    accelStatus.style.color = 'var(--color-success)';
                    checkCompletion();
                }
            };

            const handleOrientation = (event) => {
                if (event.alpha !== null && event.beta !== null && event.gamma !== null) {
                    if (Math.abs(event.beta) > 10 || Math.abs(event.gamma) > 10) {
                        this.gyroWorking = true;
                        gyroStatus.textContent = 'Tilt Detected!';
                        gyroStatus.style.color = 'var(--color-success)';
                        checkCompletion();
                    }
                }
            };

            const checkCompletion = () => {
                if (this.accelWorking && this.gyroWorking) {
                    window.removeEventListener('devicemotion', handleMotion);
                    window.removeEventListener('deviceorientation', handleOrientation);
                    this.pass('Accelerometer and Orientation sensors are fully functional');
                    this.details.accelerometer = true;
                    this.details.orientation = true;
                    setTimeout(() => resolve(), 1000);
                }
            };

            const initSensors = () => {
                permissionArea.style.display = 'none';
                dataArea.style.display = 'grid';
                window.addEventListener('devicemotion', handleMotion);
                window.addEventListener('deviceorientation', handleOrientation);
                
                setTimeout(() => {
                    if (!this.accelWorking || !this.gyroWorking) {
                        window.removeEventListener('devicemotion', handleMotion);
                        window.removeEventListener('deviceorientation', handleOrientation);
                        
                        if (this.accelWorking || this.gyroWorking) {
                            this.fail(`Only ${this.accelWorking ? 'Accelerometer' : 'Orientation'} detected movement`);
                        } else {
                            this.fail('No sensor movement detected within time limit');
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
                                this.fail('Sensor permission denied');
                                resolve();
                            }
                        })
                        .catch(e => {
                            this.fail('Error requesting sensor permission: ' + e.message);
                            resolve();
                        });
                } else {
                    initSensors();
                }
            });
            
            if (typeof DeviceMotionEvent === 'undefined' || typeof DeviceMotionEvent.requestPermission !== 'function') {
                btnRequest.textContent = 'Start Sensor Test';
            }
        });
    }
}