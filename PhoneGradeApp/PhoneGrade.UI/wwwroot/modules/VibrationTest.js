import { DeviceTest } from './DeviceTest.js';

export class VibrationTest extends DeviceTest {
    constructor() {
        super('vibration', 'Trilmotor & Haptics', 'Controleer de Taptic Engine / trilmotor');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Trilmotor testen...');

        const isAndroid = /Android/i.test(navigator.userAgent);
        const hasVibrateApi = ('vibrate' in navigator);

        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px;">
                    <h3 style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Trilmotor &amp; Taptic Engine</h3>
                    
                    ${hasVibrateApi ? `
                        <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">
                            Druk op de knop om de trilmotor te activeren.
                        </p>
                        <button id="btn-vibe-pulse" class="btn btn-primary" style="width: 100%; margin-bottom: 16px;">Activeer Trilmotor</button>
                    ` : `
                        <div style="background: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 8px; padding: 14px; margin-bottom: 16px; text-align: left;">
                            <p style="font-size: 13px; font-weight: 600; color: #0f172a; margin-bottom: 6px;">📱 iOS Trilmotor Controle:</p>
                            <p style="font-size: 13px; color: #475569; line-height: 1.4;">
                                Schakel de <strong>stille modus schakelaar</strong> aan de zijkant van de iPhone om (of druk op de Actieknop).
                            </p>
                        </div>
                    `}

                    <p style="font-size: 14px; font-weight: 600; color: var(--color-text-primary); margin-bottom: 12px;">
                        Voel je een duidelijke trilling of haptische klik van het toestel?
                    </p>

                    <div style="display: flex; gap: 10px;">
                        <button id="btn-vibe-yes" class="btn btn-success" style="flex: 1;">Ja, trilt goed</button>
                        <button id="btn-vibe-no" class="btn btn-danger" style="flex: 1;">Nee, geen trilling</button>
                    </div>
                </div>
            </div>
        `;

        const btnPulse = container.querySelector('#btn-vibe-pulse');
        const btnYes = container.querySelector('#btn-vibe-yes');
        const btnNo = container.querySelector('#btn-vibe-no');

        if (btnPulse && hasVibrateApi) {
            btnPulse.onclick = () => {
                try {
                    navigator.vibrate([250, 100, 250, 100, 250]);
                } catch (e) {}
            };
        }

        return new Promise((resolve) => {
            btnYes.onclick = () => {
                this.pass('Trilmotor / Taptic Engine werkt naar behoren');
                this.reportProgress(wsClient, 100, 'Trilmotor geslaagd');
                resolve();
            };

            btnNo.onclick = () => {
                this.fail('Trilmotor / Taptic Engine reageert niet of defect');
                this.reportProgress(wsClient, 100, 'Trilmotor defect');
                resolve();
            };
        });
    }
}
