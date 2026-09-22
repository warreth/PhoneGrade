import { DeviceTest } from './DeviceTest.js';

export class ScreenBrightnessTest extends DeviceTest {
    constructor() {
        super('brightness', 'Helderheid & Contrast', 'Controleer schermhelderheid en contrastweergave');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Helderheidstest gestart...');

        container.innerHTML = `
            <div style="position: fixed; inset: 0; background: var(--color-bg-primary); z-index: 10000; overflow-y: auto; padding: 20px; display: flex; flex-direction: column; align-items: center; justify-content: center;">
                <div style="max-width: 450px; width: 100%; background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md);">
                    <h3 style="font-size: 20px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Helderheid &amp; Contrast</h3>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 20px;">
                        Zet de helderheid van je toestel op maximaal in het Bedieningspaneel (Control Center).
                    </p>

                    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 20px;">
                        <div style="height: 100px; background: #ffffff; border: 2px solid #cbd5e1; border-radius: 8px; display: flex; align-items: center; justify-content: center; font-weight: bold; color: #000000;">
                            100% Wit
                        </div>
                        <div style="height: 100px; background: #000000; border: 2px solid #334155; border-radius: 8px; display: flex; align-items: center; justify-content: center; font-weight: bold; color: #ffffff;">
                            0% Zwart
                        </div>
                    </div>

                    <p style="font-size: 14px; font-weight: 600; color: var(--color-text-primary); margin-bottom: 16px;">
                        Is het scherm helder en zijn beide vlakken duidelijk zichtbaar?
                    </p>

                    <div style="display: flex; gap: 12px;">
                        <button id="brightness-yes" class="btn btn-success" style="flex: 1;">Ja, helder en scherp</button>
                        <button id="brightness-no" class="btn btn-danger" style="flex: 1;">Nee, te zwak / defect</button>
                    </div>
                </div>
            </div>
        `;

        const btnYes = container.querySelector('#brightness-yes');
        const btnNo = container.querySelector('#brightness-no');

        return new Promise((resolve) => {
            btnYes.onclick = () => {
                this.reportProgress(wsClient, 100, 'Helderheid goedgekeurd');
                this.pass('Schermhelderheid en contrast in orde');
                this.details.brightnessLevel = 'Confirmed OK by user';
                resolve();
            };

            btnNo.onclick = () => {
                this.reportProgress(wsClient, 100, 'Helderheid gefaald');
                this.fail('Schermhelderheid onvoldoende of contrastfout');
                this.details.brightnessLevel = 'Defective / Too dim';
                resolve();
            };
        });
    }
}
