import { DeviceTest } from './DeviceTest.js';

export class SpeakerTest extends DeviceTest {
    constructor() {
        super('speaker', 'Speaker & Earpiece', 'Test top earpiece and bottom loudspeaker');
        this.audioContext = null;
        this.earpieceWorking = false;
        this.loudspeakerWorking = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Ready for audio test...');

        container.innerHTML = `
            <h3 style="color: #00d9ff; margin-bottom: 16px;">Audio Output Test</h3>
            <p class="test-instructions">Follow the instructions to test both the top earpiece and bottom loudspeaker.</p>
            
            <div id="speaker-test-area" style="margin: 20px 0;">
                <div id="earpiece-section" style="margin-bottom: 24px; padding: 16px; background: var(--color-bg-secondary); border-radius: var(--radius-lg); border: 1px solid var(--color-border);">
                    <p style="margin-bottom: 12px; font-weight: 600;">Top Earpiece Test:</p>
                    <p style="margin-bottom: 16px; font-size: 13px; color: var(--color-text-secondary);">Hold the phone to your ear after pressing play.</p>
                    <button id="play-earpiece-btn" class="btn btn-primary" style="width: 100%; margin-bottom: 16px;">Play Earpiece Tone</button>
                    <div id="earpiece-feedback" style="display: none; justify-content: center; gap: 12px;">
                        <button id="earpiece-yes" class="btn btn-success" style="background: var(--color-success); color: #000; border: none;">I heard it</button>
                        <button id="earpiece-no" class="btn btn-error" style="background: var(--color-error); color: #fff; border: none;">No sound</button>
                    </div>
                    <p id="earpiece-status" style="text-align: center; margin-top: 12px; color: var(--color-text-tertiary); font-size: 13px;">Not tested</p>
                </div>
                
                <div id="loudspeaker-section" style="padding: 16px; background: var(--color-bg-secondary); border-radius: var(--radius-lg); border: 1px solid var(--color-border); opacity: 0.5; pointer-events: none;">
                    <p style="margin-bottom: 12px; font-weight: 600;">Loudspeaker Test:</p>
                    <p style="margin-bottom: 16px; font-size: 13px; color: var(--color-text-secondary);">A loud tone will play from the main speaker.</p>
                    <button id="play-loud-btn" class="btn btn-primary" style="width: 100%; margin-bottom: 16px;">Play Loud Tone</button>
                    <div id="loud-feedback" style="display: none; justify-content: center; gap: 12px;">
                        <button id="loud-yes" class="btn btn-success" style="background: var(--color-success); color: #000; border: none;">I heard it</button>
                        <button id="loud-no" class="btn btn-error" style="background: var(--color-error); color: #fff; border: none;">No sound</button>
                    </div>
                    <p id="loud-status" style="text-align: center; margin-top: 12px; color: var(--color-text-tertiary); font-size: 13px;">Not tested</p>
                </div>
            </div>
        `;

        const playEarpieceBtn = container.querySelector('#play-earpiece-btn');
        const playLoudBtn = container.querySelector('#play-loud-btn');
        
        const earpieceFeedback = container.querySelector('#earpiece-feedback');
        const loudFeedback = container.querySelector('#loud-feedback');
        
        const earpieceYes = container.querySelector('#earpiece-yes');
        const earpieceNo = container.querySelector('#earpiece-no');
        const loudYes = container.querySelector('#loud-yes');
        const loudNo = container.querySelector('#loud-no');

        const earpieceStatus = container.querySelector('#earpiece-status');
        const loudStatus = container.querySelector('#loud-status');
        const loudSection = container.querySelector('#loudspeaker-section');

        const initAudioContext = () => {
            if (!this.audioContext) {
                this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            }
            if (this.audioContext.state === 'suspended') {
                this.audioContext.resume();
            }
        };

        return new Promise(async (resolve) => {
            const testEarpiece = () => new Promise((res) => {
                playEarpieceBtn.onclick = () => {
                    initAudioContext();
                    playEarpieceBtn.disabled = true;
                    earpieceStatus.textContent = 'Playing low volume tone...';
                    this.playTone(800, 3000, 0.05, () => {
                        playEarpieceBtn.style.display = 'none';
                        earpieceFeedback.style.display = 'flex';
                        earpieceStatus.textContent = 'Did you hear it from the earpiece?';
                    });
                };

                earpieceYes.onclick = () => {
                    this.earpieceWorking = true;
                    earpieceStatus.textContent = 'Passed';
                    earpieceStatus.style.color = 'var(--color-success)';
                    earpieceFeedback.style.display = 'none';
                    loudSection.style.opacity = '1';
                    loudSection.style.pointerEvents = 'auto';
                    res();
                };

                earpieceNo.onclick = () => {
                    this.earpieceWorking = false;
                    earpieceStatus.textContent = 'Failed';
                    earpieceStatus.style.color = 'var(--color-error)';
                    earpieceFeedback.style.display = 'none';
                    loudSection.style.opacity = '1';
                    loudSection.style.pointerEvents = 'auto';
                    res();
                };
            });

            const testLoudspeaker = () => new Promise((res) => {
                playLoudBtn.onclick = () => {
                    initAudioContext();
                    playLoudBtn.disabled = true;
                    loudStatus.textContent = 'Playing loud tone...';
                    this.playTone(1000, 3000, 1.0, () => {
                        playLoudBtn.style.display = 'none';
                        loudFeedback.style.display = 'flex';
                        loudStatus.textContent = 'Did you hear it from the loudspeaker?';
                    });
                };

                loudYes.onclick = () => {
                    this.loudspeakerWorking = true;
                    loudStatus.textContent = 'Passed';
                    loudStatus.style.color = 'var(--color-success)';
                    loudFeedback.style.display = 'none';
                    res();
                };

                loudNo.onclick = () => {
                    this.loudspeakerWorking = false;
                    loudStatus.textContent = 'Failed';
                    loudStatus.style.color = 'var(--color-error)';
                    loudFeedback.style.display = 'none';
                    res();
                };
            });

            this.reportProgress(wsClient, 10, 'Testing earpiece...');
            await testEarpiece();

            this.reportProgress(wsClient, 50, 'Testing loudspeaker...');
            await testLoudspeaker();

            this.reportProgress(wsClient, 100, 'Finishing audio tests...');

            if (this.earpieceWorking && this.loudspeakerWorking) {
                this.pass('Both earpiece and loudspeaker are working');
            } else if (this.earpieceWorking || this.loudspeakerWorking) {
                const working = this.earpieceWorking ? 'Earpiece' : 'Loudspeaker';
                const failing = this.earpieceWorking ? 'Loudspeaker' : 'Earpiece';
                this.fail(`${working} is working, but ${failing} failed`);
            } else {
                this.fail('Both earpiece and loudspeaker failed');
            }

            this.details.earpiece = this.earpieceWorking;
            this.details.loudspeaker = this.loudspeakerWorking;

            if (this.audioContext) {
                this.audioContext.close();
            }
            resolve();
        });
    }

    playTone(frequency, durationMs, volume, onComplete) {
        if (!this.audioContext) return;
        const oscillator = this.audioContext.createOscillator();
        const gainNode = this.audioContext.createGain();

        oscillator.type = 'sine';
        oscillator.frequency.value = frequency;

        gainNode.gain.setValueAtTime(0, this.audioContext.currentTime);
        gainNode.gain.linearRampToValueAtTime(volume, this.audioContext.currentTime + 0.1);
        gainNode.gain.setValueAtTime(volume, this.audioContext.currentTime + (durationMs / 1000) - 0.1);
        gainNode.gain.linearRampToValueAtTime(0, this.audioContext.currentTime + (durationMs / 1000));

        oscillator.connect(gainNode);
        gainNode.connect(this.audioContext.destination);

        oscillator.start();
        oscillator.stop(this.audioContext.currentTime + (durationMs / 1000));

        setTimeout(onComplete, durationMs + 100);
    }
}