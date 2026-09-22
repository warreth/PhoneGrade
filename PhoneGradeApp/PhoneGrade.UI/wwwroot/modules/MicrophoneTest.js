import { DeviceTest } from './DeviceTest.js';

export class MicrophoneTest extends DeviceTest {
    constructor() {
        super('microphone', 'Microfoon Test', 'Controleer audio-opname en microfoonfunctionaliteit');
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Microfoon testen...');

        // If MediaDevices is available (HTTPS or modern Android)
        if (navigator.mediaDevices && navigator.mediaDevices.getUserMedia) {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                return await this.runLiveMeterTest(wsClient, container, stream);
            } catch (err) {
                // Permission denied or blocked by HTTP context, fallback to audio input
            }
        }

        // Native iOS audio capture fallback (works 100% on HTTP)
        return await this.runAudioCaptureFallback(wsClient, container);
    }

    async runAudioCaptureFallback(wsClient, container) {
        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px;">
                    <h3 style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Microfoontest</h3>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">
                        Spreek een kort woord in via de voicerecorder van je toestel om de microfoon te testen.
                    </p>
                    <label class="btn btn-primary" style="display: block; width: 100%; padding: 12px; cursor: pointer; text-align: center; margin-bottom: 16px;">
                        Spreek Geluid In
                        <input type="file" id="audio-file-input" accept="audio/*" capture style="display: none;">
                    </label>
                    <div id="audio-feedback" style="display: none; flex-direction: column; gap: 10px;">
                        <audio id="audio-preview" controls style="width: 100%; margin-bottom: 8px;"></audio>
                        <p style="font-size: 13px; font-weight: 600;">Hoor je je eigen stem duidelijk terug?</p>
                        <div style="display: flex; gap: 10px;">
                            <button id="mic-yes" class="btn btn-success" style="flex: 1;">Ja, helder geluid</button>
                            <button id="mic-no" class="btn btn-danger" style="flex: 1;">Nee, geen geluid / ruis</button>
                        </div>
                    </div>
                    <div id="audio-status" style="font-size: 13px; font-weight: bold; color: var(--color-text-tertiary);">Wacht op opname...</div>
                </div>
            </div>
        `;

        const audioInput = container.querySelector('#audio-file-input');
        const audioFeedback = container.querySelector('#audio-feedback');
        const audioPreview = container.querySelector('#audio-preview');
        const audioStatus = container.querySelector('#audio-status');
        const btnYes = container.querySelector('#mic-yes');
        const btnNo = container.querySelector('#mic-no');

        return new Promise((resolve) => {
            audioInput.addEventListener('change', (e) => {
                if (e.target.files && e.target.files.length > 0) {
                    const file = e.target.files[0];
                    audioPreview.src = URL.createObjectURL(file);
                    audioStatus.style.display = 'none';
                    audioFeedback.style.display = 'flex';
                }
            });

            btnYes.onclick = () => {
                this.pass('Microfoonopname succesvol');
                this.reportProgress(wsClient, 100, 'Microfoon werkt');
                resolve();
            };

            btnNo.onclick = () => {
                this.fail('Microfoonopname niet hoorbaar of mislukt');
                this.reportProgress(wsClient, 100, 'Microfoon gefaald');
                resolve();
            };
        });
    }

    async runLiveMeterTest(wsClient, container, stream) {
        container.innerHTML = `
            <div style="padding: 20px; display: flex; flex-direction: column; align-items: center; width: 100%;">
                <div style="background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: 12px; padding: 20px; text-align: center; box-shadow: var(--shadow-md); width: 100%; max-width: 400px;">
                    <h3 style="font-size: 18px; font-weight: bold; margin-bottom: 8px; color: var(--color-text-primary);">Microfoontest</h3>
                    <p style="font-size: 13px; color: var(--color-text-secondary); margin-bottom: 16px;">Praat of maak geluid om de niveaumeter te activeren.</p>
                    <div style="background: var(--color-bg-tertiary); height: 28px; border-radius: 14px; overflow: hidden; margin-bottom: 12px;">
                        <div id="mic-vu-bar" style="background: linear-gradient(90deg, #22c55e 0%, #eab308 70%, #ef4444 100%); width: 0%; height: 100%; transition: width 50ms linear;"></div>
                    </div>
                    <div id="live-mic-status" style="font-size: 13px; font-weight: 600; color: var(--color-text-secondary);">Luisteren...</div>
                </div>
            </div>
        `;

        const vuBar = container.querySelector('#mic-vu-bar');
        const statusText = container.querySelector('#live-mic-status');

        const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        const analyser = audioCtx.createAnalyser();
        const source = audioCtx.createMediaStreamSource(stream);
        source.connect(analyser);
        analyser.fftSize = 256;

        const dataArray = new Uint8Array(analyser.frequencyBinCount);
        let peakLevel = 0;

        return new Promise((resolve) => {
            const checkAudio = () => {
                analyser.getByteFrequencyData(dataArray);
                let sum = 0;
                for (let i = 0; i < dataArray.length; i++) sum += dataArray[i];
                const avg = sum / dataArray.length;
                const pct = Math.min(100, Math.round((avg / 128) * 100));
                
                if (vuBar) vuBar.style.width = pct + '%';
                if (pct > peakLevel) peakLevel = pct;

                if (peakLevel > 20) {
                    if (statusText) {
                        statusText.textContent = 'Geluid gedetecteerd (' + peakLevel + '%)';
                        statusText.style.color = 'var(--color-success)';
                    }
                    setTimeout(() => {
                        stream.getTracks().forEach(t => t.stop());
                        audioCtx.close();
                        this.pass('Microfoon registreert audio (piek: ' + peakLevel + '%)');
                        resolve();
                    }, 1000);
                    return;
                }
                requestAnimationFrame(checkAudio);
            };

            requestAnimationFrame(checkAudio);

            setTimeout(() => {
                if (peakLevel <= 20) {
                    stream.getTracks().forEach(t => t.stop());
                    audioCtx.close();
                    this.fail('Geen audiosignaal gedetecteerd op microfoon');
                    resolve();
                }
            }, 10000);
        });
    }
}
