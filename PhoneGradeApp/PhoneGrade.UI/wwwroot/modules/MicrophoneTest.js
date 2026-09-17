import { DeviceTest } from './DeviceTest.js';

/**
 * MicrophoneTest: Uses Web Audio API to detect decibel input spikes from speech.
 */
export class MicrophoneTest extends DeviceTest {
    constructor() {
        super('microphone', 'Microfoon Test', 'Praat om de microfoon te testen');
        this.audioContext = null;
        this.analyser = null;
        this.microphone = null;
        this.maxDecibels = -Infinity;
        this.threshold = -30; // dB threshold for detecting speech
        this.detectionCount = 0;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Microfoon toegang vragen...');

        try {
            // Request microphone access
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
            
            this.reportProgress(wsClient, 20, 'Microfoon geactiveerd...');

            // Create audio context and analyser
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            this.analyser = this.audioContext.createAnalyser();
            this.analyser.fftSize = 256;
            
            this.microphone = this.audioContext.createMediaStreamSource(stream);
            this.microphone.connect(this.analyser);

            // Create UI
            container.innerHTML = `
                <h3 style="color: #00ff88; margin-bottom: 16px;">Microfoon Test</h3>
                <p class="test-instructions">Praat in de microfoon. De balk moet bewegen wanneer je geluid maakt.</p>
                <div class="audio-visualizer" style="width: 100%; height: 120px; background: #1a1a1a; border-radius: 8px; margin: 16px 0; position: relative; overflow: hidden;">
                    <div id="audio-level-bar" style="height: 100%; background: #00ff88; width: 0%; transition: width 0.05s;"></div>
                </div>
                <p id="mic-status" style="text-align: center; margin: 16px 0;">Wachten op geluid...</p>
                <p id="mic-db" style="text-align: center; font-size: 24px; font-weight: 600; color: #00ff88;">0 dB</p>
            `;

            const levelBar = container.querySelector('#audio-level-bar');
            const statusText = container.querySelector('#mic-status');
            const dbText = container.querySelector('#mic-db');

            // Analyze audio for 5 seconds
            const duration = 5000;
            const startTime = Date.now();
            const dataArray = new Uint8Array(this.analyser.frequencyBinCount);

            return new Promise((resolve) => {
                const analyze = () => {
                    const elapsed = Date.now() - startTime;
                    
                    if (elapsed >= duration) {
                        // Cleanup
                        stream.getTracks().forEach(track => track.stop());
                        this.audioContext.close();

                        if (this.detectionCount > 0) {
                            this.pass(`Microfoon werkt - max ${Math.round(this.maxDecibels)} dB gedetecteerd`);
                            this.details.maxDecibels = Math.round(this.maxDecibels);
                            this.details.detections = this.detectionCount;
                        } else {
                            this.fail('Geen geluid gedetecteerd - microfoon mogelijk defect');
                        }
                        resolve();
                        return;
                    }

                    // Get audio data
                    this.analyser.getByteFrequencyData(dataArray);
                    
                    // Calculate average volume
                    const average = dataArray.reduce((a, b) => a + b) / dataArray.length;
                    
                    // Convert to decibels (approximate)
                    const decibels = average > 0 ? 20 * Math.log10(average / 255) * 2 : -Infinity;
                    
                    if (decibels > this.maxDecibels) {
                        this.maxDecibels = decibels;
                    }

                    // Check if above threshold
                    if (decibels > this.threshold) {
                        this.detectionCount++;
                        statusText.textContent = 'Geluid gedetecteerd!';
                        statusText.style.color = '#00ff88';
                    }

                    // Update UI
                    const percentage = Math.max(0, Math.min(100, (decibels + 60) * 1.67));
                    levelBar.style.width = percentage + '%';
                    dbText.textContent = Math.round(decibels) + ' dB';

                    // Update progress
                    const progress = 20 + (elapsed / duration) * 80;
                    this.reportProgress(wsClient, progress, 'Luisteren...');

                    requestAnimationFrame(analyze);
                };

                analyze();
            });

        } catch (error) {
            if (error.name === 'NotAllowedError') {
                this.skip('Microfoon toegang geweigerd door gebruiker');
            } else if (error.name === 'NotFoundError') {
                this.skip('Geen microfoon gevonden op dit apparaat');
            } else {
                this.fail('Microfoon fout: ' + error.message);
            }
        }
    }
}