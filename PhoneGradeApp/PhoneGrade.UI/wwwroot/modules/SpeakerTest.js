import { DeviceTest } from './DeviceTest.js';

/**
 * SpeakerTest: Left/right audio channel balance playback test.
 */
export class SpeakerTest extends DeviceTest {
    constructor() {
        super('speaker', 'Speaker Test', 'Test linker en rechter speaker');
        this.audioContext = null;
        this.oscillator = null;
        this.leftWorking = false;
        this.rightWorking = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Audio context initialiseren...');

        try {
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();

            container.innerHTML = `
                <h3 style="color: #00ff88; margin-bottom: 16px;">Speaker Test</h3>
                <p class="test-instructions">Luister naar de tonen en bevestig of je ze hoort.</p>
                
                <div id="speaker-test-area" style="margin: 20px 0;">
                    <div id="left-speaker-test" style="margin-bottom: 24px;">
                        <p style="margin-bottom: 12px;">Linker Speaker:</p>
                        <button id="play-left-btn" class="btn-primary" style="width: auto; padding: 12px 24px;">Speel Links</button>
                        <span id="left-status" style="margin-left: 12px; color: #b0b0b0;">Nog niet getest</span>
                    </div>
                    
                    <div id="right-speaker-test">
                        <p style="margin-bottom: 12px;">Rechter Speaker:</p>
                        <button id="play-right-btn" class="btn-primary" style="width: auto; padding: 12px 24px;">Speel Rechts</button>
                        <span id="right-status" style="margin-left: 12px; color: #b0b0b0;">Nog niet getest</span>
                    </div>
                </div>
                
                <div id="speaker-result" style="margin-top: 24px;"></div>
            `;

            const playLeftBtn = container.querySelector('#play-left-btn');
            const playRightBtn = container.querySelector('#play-right-btn');
            const leftStatus = container.querySelector('#left-status');
            const rightStatus = container.querySelector('#right-status');
            const resultDiv = container.querySelector('#speaker-result');

            // Test left speaker
            const testLeft = () => new Promise((resolve) => {
                playLeftBtn.disabled = true;
                leftStatus.textContent = 'Afspelen...';
                leftStatus.style.color = '#ffaa00';

                this.playTone('left', 1000, 1000, () => {
                    leftStatus.textContent = 'Hoorde je de toon?';
                    leftStatus.style.color = '#b0b0b0';

                    const confirmBtn = document.createElement('button');
                    confirmBtn.textContent = 'Ja, gehoord';
                    confirmBtn.className = 'btn-primary';
                    confirmBtn.style.cssText = 'width: auto; padding: 8px 16px; margin-left: 8px;';
                    confirmBtn.onclick = () => {
                        this.leftWorking = true;
                        leftStatus.textContent = 'OK';
                        leftStatus.style.color = '#00ff88';
                        confirmBtn.remove();
                        skipBtn.remove();
                        resolve();
                    };

                    const skipBtn = document.createElement('button');
                    skipBtn.textContent = 'Nee';
                    skipBtn.className = 'btn-secondary';
                    skipBtn.style.cssText = 'width: auto; padding: 8px 16px; margin-left: 8px;';
                    skipBtn.onclick = () => {
                        leftStatus.textContent = 'NIET GEHOORD';
                        leftStatus.style.color = '#ff4444';
                        confirmBtn.remove();
                        skipBtn.remove();
                        resolve();
                    };

                    playLeftBtn.parentElement.appendChild(confirmBtn);
                    playLeftBtn.parentElement.appendChild(skipBtn);
                });
            });

            // Test right speaker
            const testRight = () => new Promise((resolve) => {
                playRightBtn.disabled = true;
                rightStatus.textContent = 'Afspelen...';
                rightStatus.style.color = '#ffaa00';

                this.playTone('right', 1000, 1000, () => {
                    rightStatus.textContent = 'Hoorde je de toon?';
                    rightStatus.style.color = '#b0b0b0';

                    const confirmBtn = document.createElement('button');
                    confirmBtn.textContent = 'Ja, gehoord';
                    confirmBtn.className = 'btn-primary';
                    confirmBtn.style.cssText = 'width: auto; padding: 8px 16px; margin-left: 8px;';
                    confirmBtn.onclick = () => {
                        this.rightWorking = true;
                        rightStatus.textContent = 'OK';
                        rightStatus.style.color = '#00ff88';
                        confirmBtn.remove();
                        skipBtn.remove();
                        resolve();
                    };

                    const skipBtn = document.createElement('button');
                    skipBtn.textContent = 'Nee';
                    skipBtn.className = 'btn-secondary';
                    skipBtn.style.cssText = 'width: auto; padding: 8px 16px; margin-left: 8px;';
                    skipBtn.onclick = () => {
                        rightStatus.textContent = 'NIET GEHOORD';
                        rightStatus.style.color = '#ff4444';
                        confirmBtn.remove();
                        skipBtn.remove();
                        resolve();
                    };

                    playRightBtn.parentElement.appendChild(confirmBtn);
                    playRightBtn.parentElement.appendChild(skipBtn);
                });
            });

            this.reportProgress(wsClient, 20, 'Test linker speaker...');
            await testLeft();

            this.reportProgress(wsClient, 60, 'Test rechter speaker...');
            await testRight();

            this.reportProgress(wsClient, 100, 'Klaar');

            // Determine result
            if (this.leftWorking && this.rightWorking) {
                this.pass('Beide speakers werken correct');
                this.details.leftSpeaker = 'working';
                this.details.rightSpeaker = 'working';
            } else if (this.leftWorking || this.rightWorking) {
                const working = this.leftWorking ? 'links' : 'rechts';
                const broken = this.leftWorking ? 'rechts' : 'links';
                this.fail(`Alleen ${working} speaker werkt - ${broken} defect`);
                this.details.leftSpeaker = this.leftWorking ? 'working' : 'failed';
                this.details.rightSpeaker = this.rightWorking ? 'working' : 'failed';
            } else {
                this.fail('Geen enkele speaker werkt');
                this.details.leftSpeaker = 'failed';
                this.details.rightSpeaker = 'failed';
            }

            this.audioContext.close();

        } catch (error) {
            this.fail('Audio fout: ' + error.message);
        }
    }

    playTone(channel, frequency, duration, callback) {
        const oscillator = this.audioContext.createOscillator();
        const gainNode = this.audioContext.createGain();
        const panner = this.audioContext.createStereoPanner();

        oscillator.type = 'sine';
        oscillator.frequency.value = frequency;

        // Pan left or right
        panner.pan.value = channel === 'left' ? -1 : 1;

        // Fade in/out to avoid clicks
        gainNode.gain.setValueAtTime(0, this.audioContext.currentTime);
        gainNode.gain.linearRampToValueAtTime(0.5, this.audioContext.currentTime + 0.05);
        gainNode.gain.linearRampToValueAtTime(0, this.audioContext.currentTime + duration / 1000);

        oscillator.connect(panner);
        panner.connect(gainNode);
        gainNode.connect(this.audioContext.destination);

        oscillator.start();
        oscillator.stop(this.audioContext.currentTime + duration / 1000);

        setTimeout(callback, duration);
    }
}