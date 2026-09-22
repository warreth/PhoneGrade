import { DeviceTest } from './DeviceTest.js';

export class CameraTest extends DeviceTest {
    constructor() {
        super('camera', 'Camera\'s & Flitser', 'Controleer voor-, achtercamera en flitser');
        this.frontWorking = false;
        this.backWorking = false;
        this.flashWorking = null;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Camera test voorbereiden...');

        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <h3 style="color: var(--color-text-primary); margin-bottom: 16px; font-size: 20px;">Camera Test</h3>
                
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px; margin-bottom: 20px;">
                    <p style="font-weight: bold; margin-bottom: 12px; color: var(--color-text-primary);">Stap 1: Voorcamera (Selfie)</p>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">Neem een testfoto met de voorcamera.</p>
                    <label class="btn btn-primary" style="display: block; width: 100%; padding: 12px; cursor: pointer; text-align: center; margin-bottom: 12px;">
                        Open Voorcamera
                        <input type="file" id="front-cam-input" accept="image/*,video/*" capture="user" style="display: none;">
                    </label>
                    <div id="front-feedback" style="display: none; flex-direction: column; gap: 8px;">
                        <p style="font-size: 13px; font-weight: 600;">Was de foto/video scherp en gelukt?</p>
                        <div style="display: flex; gap: 10px;">
                            <button id="front-yes" class="btn btn-success" style="flex: 1;">Ja, werkt perfect</button>
                            <button id="front-no" class="btn btn-danger" style="flex: 1;">Nee, defect / wazig</button>
                        </div>
                    </div>
                    <div id="front-status" style="font-size: 13px; font-weight: bold; color: var(--color-text-tertiary); margin-top: 8px;">Wacht op foto...</div>
                </div>

                <div id="back-cam-section" style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px; opacity: 0.5; pointer-events: none;">
                    <p style="font-weight: bold; margin-bottom: 12px; color: var(--color-text-primary);">Stap 2: Achtercamera &amp; Flitser</p>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">Zet de flitser (zaklamp) AAN tijdens de foto, of neem een video met flits.</p>
                    <label class="btn btn-primary" style="display: block; width: 100%; padding: 12px; cursor: pointer; text-align: center; margin-bottom: 12px;">
                        Open Achtercamera
                        <input type="file" id="back-cam-input" accept="image/*,video/*" capture="environment" style="display: none;">
                    </label>
                    <div id="back-feedback" style="display: none; flex-direction: column; gap: 8px;">
                        <p style="font-size: 13px; font-weight: 600;">Werkte de camera EN de flitser goed?</p>
                        <div style="display: flex; gap: 10px;">
                            <button id="back-yes" class="btn btn-success" style="flex: 1;">Ja, beide werken</button>
                            <button id="back-no" class="btn btn-danger" style="flex: 1;">Nee, camera of flits defect</button>
                        </div>
                    </div>
                    <div id="back-status" style="font-size: 13px; font-weight: bold; color: var(--color-text-tertiary); margin-top: 8px;">Wacht op foto...</div>
                </div>
            </div>
        `;

        const frontInput = container.querySelector('#front-cam-input');
        const frontFeedback = container.querySelector('#front-feedback');
        const frontYes = container.querySelector('#front-yes');
        const frontNo = container.querySelector('#front-no');
        const frontStatus = container.querySelector('#front-status');
        
        const backSection = container.querySelector('#back-cam-section');
        const backInput = container.querySelector('#back-cam-input');
        const backFeedback = container.querySelector('#back-feedback');
        const backYes = container.querySelector('#back-yes');
        const backNo = container.querySelector('#back-no');
        const backStatus = container.querySelector('#back-status');

        return new Promise(async (resolve) => {
            const testFront = () => new Promise(res => {
                frontInput.addEventListener('change', (e) => {
                    if (e.target.files && e.target.files.length > 0) {
                        frontStatus.style.display = 'none';
                        frontFeedback.style.display = 'flex';
                    }
                });
                
                frontYes.onclick = () => {
                    this.frontWorking = true;
                    frontFeedback.style.display = 'none';
                    frontStatus.style.display = 'block';
                    frontStatus.textContent = 'Voorcamera: Geslaagd';
                    frontStatus.style.color = 'var(--color-success)';
                    
                    backSection.style.opacity = '1';
                    backSection.style.pointerEvents = 'auto';
                    res();
                };
                
                frontNo.onclick = () => {
                    this.frontWorking = false;
                    frontFeedback.style.display = 'none';
                    frontStatus.style.display = 'block';
                    frontStatus.textContent = 'Voorcamera: Defect';
                    frontStatus.style.color = 'var(--color-error)';
                    
                    backSection.style.opacity = '1';
                    backSection.style.pointerEvents = 'auto';
                    res();
                };
            });
            
            const testBack = () => new Promise(res => {
                backInput.addEventListener('change', (e) => {
                    if (e.target.files && e.target.files.length > 0) {
                        backStatus.style.display = 'none';
                        backFeedback.style.display = 'flex';
                    }
                });
                
                backYes.onclick = () => {
                    this.backWorking = true;
                    backFeedback.style.display = 'none';
                    backStatus.style.display = 'block';
                    backStatus.textContent = 'Achtercamera & Flits: Geslaagd';
                    backStatus.style.color = 'var(--color-success)';
                    res();
                };
                
                backNo.onclick = () => {
                    this.backWorking = false;
                    backFeedback.style.display = 'none';
                    backStatus.style.display = 'block';
                    backStatus.textContent = 'Achtercamera & Flits: Defect';
                    backStatus.style.color = 'var(--color-error)';
                    res();
                };
            });

            await testFront();
            this.reportProgress(wsClient, 50, 'Voorcamera getest');
            
            await testBack();
            this.reportProgress(wsClient, 100, 'Alle camera\'s getest');

            if (this.frontWorking && this.backWorking) {
                this.pass('Voorcamera, achtercamera en flitser werken correct');
            } else if (this.frontWorking || this.backWorking) {
                this.fail('Slechts één camera werkt correct');
            } else {
                this.fail('Beide camera\'s of flitser defect');
            }
            
            this.details.frontCamera = this.frontWorking;
            this.details.rearCamera = this.backWorking;
            resolve();
        });
    }
}
