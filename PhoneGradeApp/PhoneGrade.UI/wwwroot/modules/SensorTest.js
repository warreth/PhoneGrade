import { DeviceTest } from './DeviceTest.js';

/**
 * SensorTest: Accelerometer and gyroscope orientation check.
 */
export class SensorTest extends DeviceTest {
    constructor() {
        super('sensor', 'Sensor Test', 'Test versnellingssensor en gyroscoop');
        this.accelerationData = [];
        this.rotationData = [];
        this.threshold = 0.5; // m/s² for acceleration, deg/s for rotation
        this.minReadings = 50; // Minimum sensor readings required
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Sensor toegang vragen...');

        // Check if sensor APIs are available
        const hasAccelerometer = 'Accelerometer' in window;
        const hasGyroscope = 'Gyroscope' in window;

        container.innerHTML = `
            <h3 style="color: #00ff88; margin-bottom: 16px;">Sensor Test</h3>
            <p class="test-instructions">Houd het toestel stil, schud het langzaam, draai het.</p>
            <p style="margin-bottom: 16px; font-size: 12px; color: #b0b0b0;">Dit test de versnellingssensor en gyroscoop.</p>
            
            <div class="sensor-values">
                <div class="sensor-value">
                    <div class="sensor-label">X (X-as)</div>
                    <div class="sensor-number" id="accel-x">0.00</div>
                </div>
                <div class="sensor-value">
                    <div class="sensor-label">Y (Y-as)</div>
                    <div class="sensor-number" id="accel-y">0.00</div>
                </div>
                <div class="sensor-value">
                    <div class="sensor-label">Z (Z-as)</div>
                    <div class="sensor-number" id="accel-z">0.00</div>
                </div>
                <div class="sensor-value">
                    <div class="sensor-label">Rotatie X</div>
                    <div class="sensor-number" id="gyro-x">0.0</div>
                </div>
                <div class="sensor-value">
                    <div class="sensor-label">Rotatie Y</div>
                    <div class="sensor-number" id="gyro-y">0.0</div>
                </div>
                <div class="sensor-value">
                    <div class="sensor-label">Rotatie Z</div>
                    <div class="sensor-number" id="gyro-z">0.0</div>
                </div>
            </div>
            
            <div id="sensor-status" style="text-align: center; margin: 16px 0; color: #b0b0b0;">Wachten op sensor data...</div>
            <p style="text-align: center; font-size: 12px; color: #b0b0b0;">Voor: ${hasAccelerometer ? 'Accelerometer beschikbaar' : 'Geen accelerometer'} | Gyroscoop: ${hasGyroscope ? 'Beschikbaar' : 'Geen gyroscoop'}</p>
        `;

        if (!hasAccelerometer && !hasGyroscope) {
            this.skip('Geen sensor ondersteuning op deze browser/device');
            return;
        }

        this.reportProgress(wsClient, 20, 'Sensor data verzamelen...');

        // Read sensor for 5 seconds
        const duration = 5000;
        const startTime = Date.now();
        const readings = [];
        const readThreshold = 30;

        return new Promise((resolve) => {
            let accelerometer, gyroscope;
            let readingCount = 0;

            // Setup accelerometer if available
            if (hasAccelerometer) {
                accelerometer = new Accelerometer({ frequency: 60 });
                accelerometer.addEventListener('reading', () => {
                    const x = accelerometer.acceleration.x || 0;
                    const y = accelerometer.acceleration.y || 0;
                    const z = accelerometer.acceleration.z || 0;

                    readings.push({ x, y, z, t: Date.now() });

                    // Update UI
                    container.querySelector('#accel-x').textContent = x.toFixed(2);
                    container.querySelector('#accel-y').textContent = y.toFixed(2);
                    container.querySelector('#accel-z').textContent = z.toFixed(2);

                    readingCount++;
                });
                accelerometer.start();
            }

            // Setup gyroscope if available
            if (hasGyroscope) {
                gyroscope = new Gyroscope({ frequency: 60 });
                gyroscope.addEventListener('reading', () => {
                    const x = gyroscope.rotationX || 0;
                    const y = gyroscope.rotationY || 0;
                    const z = gyroscope.rotationZ || 0;

                    readings.push({ x, y, z, t: Date.now() });

                    // Update UI
                    container.querySelector('#gyro-x').textContent = x.toFixed(1);
                    container.querySelector('#gyro-y').textContent = y.toFixed(1);
                    container.querySelector('#gyro-z').textContent = z.toFixed(1);

                    readingCount++;
                });
                gyroscope.start();
            }

            // Check status periodically
            const checkStatus = () => {
                const elapsed = Date.now() - startTime;
                const progress = 20 + (elapsed / duration) * 80;
                this.reportProgress(wsClient, progress, 'Sensor data...');

                if (readingCount >= readThreshold) {
                    container.querySelector('#sensor-status').textContent = 'Data verzameld!';
                    container.querySelector('#sensor-status').style.color = '#00ff88';
                }

                if (elapsed >= duration) {
                    // Cleanup
                    if (accelerometer) accelerometer.stop();
                    if (gyroscope) gyroscope.stop();

                    // Analyze results
                    const accelData = readings.filter(r => r.hasOwnProperty('x'));
                    const gyroData = readings.filter(r => r.hasOwnProperty('x') && !r.hasOwnProperty('acceleration'));

                    if (accelerometer) {
                        const hasMotion = accelData.some(r => 
                            Math.abs(r.x) > this.threshold || 
                            Math.abs(r.y) > this.threshold || 
                            Math.abs(r.z) > (this.threshold + 9.8) // Gravity offset
                        );

                        this.frontWorking = hasMotion;
                    }

                    if (gyroscope) {
                        const hasRotation = gyroData.some(r => 
                            Math.abs(r.x) > this.threshold || 
                            Math.abs(r.y) > this.threshold || 
                            Math.abs(r.z) > this.threshold
                        );

                        this.backWorking = hasRotation;
                    }

                    if (hasAccelerometer && hasGyroscope) {
                        const accelWorks = this.frontWorking;
                        const gyroWorks = this.backWorking;

                        if (accelWorks && gyroWorks) {
                            this.pass('Alle sensoren detecteren beweging');
                            this.details.accelerometer = 'working';
                            this.details.gyroscope = 'working';
                        } else if (accelWorks || gyroWorks) {
                            const working = accelWorks ? 'versnellingssensor' : 'gyroscoop';
                            this.fail(`Alleen ${working} werkt`);
                            this.details.accelerometer = accelWorks ? 'working' : 'failed';
                            this.details.gyroscope = gyroWorks ? 'working' : 'failed';
                        } else {
                            this.fail('Geen sensoren detecteren beweging');
                            this.details.accelerometer = 'failed';
                            this.details.gyroscope = 'failed';
                        }
                    } else {
                        this.skip('Niet alle sensoren beschikbaar');
                    }

                    resolve();
                } else {
                    setTimeout(checkStatus, 100);
                }
            };

            checkStatus();
        });
    }
}