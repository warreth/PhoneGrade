import { DeviceTest } from './DeviceTest.js';

export class ForceTouchTest extends DeviceTest {
    constructor() {
        super('forcetouch', 'Force Touch', 'Test PointerEvent pressure capability');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Ready for force touch');

        container.innerHTML = `
            <h3 style="color: var(--color-accent); margin-bottom: 16px;">Force Touch / 3D Touch</h3>
            <p class="test-instructions">Press firmly on the target below. The circle will grow as pressure increases.</p>
            
            <div style="display: flex; justify-content: center; align-items: center; height: 250px; background: var(--color-bg-secondary); border-radius: var(--radius-lg); margin-top: 24px; touch-action: none;" id="pressure-area">
                <div id="pressure-target" style="width: 80px; height: 80px; border-radius: 50%; background: var(--color-accent); display: flex; justify-content: center; align-items: center; color: #000; font-weight: bold; transition: transform 0.1s; box-shadow: 0 0 15px var(--color-accent);">
                    Press
                </div>
            </div>
            
            <div style="text-align: center; margin-top: 16px;">
                <p id="pressure-value" style="font-size: 24px; font-weight: bold; font-family: monospace;">0.00</p>
                <p style="font-size: 12px; color: var(--color-text-tertiary);">Pressure Level</p>
            </div>
            
            <div style="text-align: center; margin-top: 16px;">
                <button id="skip-pressure" class="btn btn-secondary">Device doesn't support pressure</button>
            </div>
        `;

        const area = container.querySelector('#pressure-area');
        const target = container.querySelector('#pressure-target');
        const valDisplay = container.querySelector('#pressure-value');
        const skipBtn = container.querySelector('#skip-pressure');
        
        let maxPressure = 0;

        return new Promise((resolve) => {
            const handlePointer = (e) => {
                e.preventDefault();
                
                // e.pressure ranges from 0 to 1. 0.5 is typical normal touch without pressure sensitivity hardware, 
                // but true force touch hardware will vary from 0.0 to 1.0 based on physical force.
                let pressure = e.pressure || 0;
                
                // Some devices report pressure=0 or 0.5 static if they don't support it.
                if (pressure > maxPressure) maxPressure = pressure;

                valDisplay.textContent = pressure.toFixed(2);
                
                // Visual feedback
                const scale = 1 + (pressure * 1.5);
                target.style.transform = `scale(${scale})`;
                
                if (pressure > 0.6) { // Detected higher than normal static touch
                    target.style.background = 'var(--color-success)';
                    target.style.boxShadow = '0 0 25px var(--color-success)';
                    
                    this.pass(`Force touch detected (Max pressure: ${maxPressure.toFixed(2)})`);
                    this.details.maxPressure = maxPressure;
                    this.details.supported = true;
                    
                    // Delay slightly before resolving to show user they succeeded
                    setTimeout(() => {
                        area.removeEventListener('pointerdown', handlePointer);
                        area.removeEventListener('pointermove', handlePointer);
                        resolve();
                    }, 1000);
                }
            };

            area.addEventListener('pointerdown', handlePointer, {passive: false});
            area.addEventListener('pointermove', handlePointer, {passive: false});
            
            area.addEventListener('pointerup', () => {
                target.style.transform = 'scale(1)';
            });

            skipBtn.addEventListener('click', () => {
                if (maxPressure > 0 && maxPressure !== 0.5) {
                    this.fail(`Skipped, but recorded pressure variance (${maxPressure.toFixed(2)})`);
                } else {
                    this.skip('Device does not support pressure sensitivity or Haptic Touch API is blocked');
                }
                this.details.maxPressure = maxPressure;
                this.details.supported = false;
                resolve();
            });
        });
    }
}