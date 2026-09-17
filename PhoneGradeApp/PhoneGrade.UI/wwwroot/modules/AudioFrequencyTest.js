import { DeviceTest } from './DeviceTest.js';

/**
 * AudioFrequencyTest: Play tone sweeps to test audio frequency response.
 */
export class AudioFrequencyTest extends DeviceTest {
    constructor() {
        super('frequency', 'Audio Frequency', 'Test speaker frequency response');
        this.audioContext = null;
        this.frequencies = [
            { hz: 100, name: 'Low Bass', heard: null },
            { hz: 500, name: 'Mid Bass', heard: null },
            { hz: 2000, name: 'Mid Range', heard: null },
            { hz: 5000, name: 'High Range', heard: null },
            { hz: 10000, name: 'Treble', heard: null }
        ];
        this.currentFreqIndex = -1;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Initializing audio context...');

        try {
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            
            container.innerHTML = `
                <div style="padding: 20px;">
                    <p style="margin-bottom: 20px; color: #cbd5e1;">Test your speaker's frequency response across the spectrum.</p>
                    
                    <div id="freq-controls" style="display: flex; flex-direction: column; gap: 16px; align-items: center;">
                        <button id="start-sweep-btn" class="btn btn-primary" style="width: auto;">Start Frequency Sweep</button>
                    </div>

                    <div id="freq-status" style="margin-top: 30px; display: none;">
                        <div style="text-align: center; margin-bottom: 20px;">
                            <p style="color: #94a3b8; font-size: 14px; margin-bottom: 8px;">Playing:</p>
                            <div id="current-hz" style="font-size: 32px; font-weight: 700; color: #00d9ff; font-family: monospace;">---</div>
                        </div>

                        <div id="freq-feedback" style="display: none; justify-content: center; gap: 12px; margin-bottom: 30px;">
                            <button id="btn-heard-yes" class="btn btn-primary" style="width: auto;">I hear it</button>
                            <button id="btn-heard-no" class="btn btn-secondary" style="width: auto;">Cannot hear it</button>
                        </div>
                    </div>

                    <div id="freq-results" style="margin-top: 20px;"></div>
                </div>
            `;

            const startBtn = container.querySelector('#start-sweep-btn');
            const freqStatus = container.querySelector('#freq-status');
            const freqFeedback = container.querySelector('#freq-feedback');
            const currentHz = container.querySelector('#current-hz');
            const resultsDiv = container.querySelector('#freq-results');
            
            const btnYes = container.querySelector('#btn-heard-yes');
            const btnNo = container.querySelector('#btn-heard-no');

            return new Promise((resolve) => {
                startBtn.onclick = async () => {
                    startBtn.style.display = 'none';
                    freqStatus.style.display = 'block';
                    
                    for (let i = 0; i < this.frequencies.length; i++) {
                        this.currentFreqIndex = i;
                        const freq = this.frequencies[i];
                        
                        currentHz.textContent = `${freq.hz} Hz`;
                        freqFeedback.style.display = 'flex';
                        
                        const progress = (i / this.frequencies.length) * 100;
                        this.reportProgress(wsClient, progress, `Testing ${freq.hz}Hz...`);
                        
                        await this.playToneWithFeedback(freq, btnYes, btnNo);
                        freqFeedback.style.display = 'none';
                    }

                    // Complete
                    currentHz.textContent = 'Done';
                    this.renderResults(resultsDiv);
                    
                    const heardCount = this.frequencies.filter(f => f.heard).length;
                    
                    if (heardCount === this.frequencies.length) {
                        this.pass('Full frequency spectrum heard');
                    } else if (heardCount >= this.frequencies.length - 2) {
                        this.pass(`Acceptable response (${heardCount}/${this.frequencies.length} heard)`);
                    } else {
                        this.fail(`Poor frequency response (${heardCount}/${this.frequencies.length} heard)`);
                    }

                    this.details.frequencyResponse = this.frequencies;
                    
                    if (this.audioContext) {
                        this.audioContext.close();
                    }
                    
                    resolve();
                };
            });

        } catch (error) {
            this.fail('Audio initialization failed: ' + error.message);
        }
    }

    async playToneWithFeedback(freq, btnYes, btnNo) {
        return new Promise((resolve) => {
            const oscillator = this.audioContext.createOscillator();
            const gainNode = this.audioContext.createGain();
            
            oscillator.type = 'sine';
            oscillator.frequency.value = freq.hz;
            
            oscillator.connect(gainNode);
            gainNode.connect(this.audioContext.destination);
            
            // Start silent, fade in
            gainNode.gain.setValueAtTime(0, this.audioContext.currentTime);
            gainNode.gain.linearRampToValueAtTime(0.5, this.audioContext.currentTime + 0.1);
            
            oscillator.start();

            // Set up button handlers
            const cleanup = () => {
                gainNode.gain.linearRampToValueAtTime(0, this.audioContext.currentTime + 0.1);
                setTimeout(() => {
                    oscillator.stop();
                    oscillator.disconnect();
                    gainNode.disconnect();
                }, 150);
            };

            const handleYes = () => {
                freq.heard = true;
                cleanup();
                btnYes.removeEventListener('click', handleYes);
                btnNo.removeEventListener('click', handleNo);
                resolve();
            };

            const handleNo = () => {
                freq.heard = false;
                cleanup();
                btnYes.removeEventListener('click', handleYes);
                btnNo.removeEventListener('click', handleNo);
                resolve();
            };

            btnYes.addEventListener('click', handleYes);
            btnNo.addEventListener('click', handleNo);
            
            // Play tone for up to 5 seconds
            setTimeout(() => {
                if (freq.heard === null) {
                    handleNo(); // Treat timeout as not heard
                }
            }, 5000);
        });
    }

    renderResults(container) {
        container.innerHTML = this.frequencies.map(f => `
            <div style="display: flex; justify-content: space-between; padding: 12px; background: #1a1f2e; border: 1px solid #334155; border-radius: 8px; margin-bottom: 8px;">
                <div>
                    <span style="color: #00d9ff; font-family: monospace; font-weight: 700;">${f.hz}Hz</span>
                    <span style="color: #94a3b8; font-size: 12px; margin-left: 8px;">${f.name}</span>
                </div>
                <div style="color: ${f.heard ? '#4ade80' : '#f87171'}; font-weight: 600;">
                    ${f.heard ? 'Heard' : 'Missed'}
                </div>
            </div>
        `).join('');
    }
}