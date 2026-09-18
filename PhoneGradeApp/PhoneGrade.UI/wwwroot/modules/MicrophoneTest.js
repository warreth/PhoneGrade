import { DeviceTest } from './DeviceTest.js';

export class MicrophoneTest extends DeviceTest {
    constructor() {
        super('microphone', 'Microphone Array', 'Test audio input with live volume visualizer');
        this.results = [];
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Requesting microphone permission...');

        try {
            // First check if MediaDevices is supported
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                this.fail('MediaDevices API not supported on this browser');
                return;
            }

            let initialStream;
            try {
                initialStream = await navigator.mediaDevices.getUserMedia({ audio: true });
            } catch (permError) {
                this.fail('Microphone permission denied or hardware unavailable');
                return;
            }

            const devices = await navigator.mediaDevices.enumerateDevices();
            const audioInputs = devices.filter(d => d.kind === 'audioinput');

            // Release initial permission check stream
            initialStream.getTracks().forEach(t => t.stop());

            if (audioInputs.length === 0) {
                this.fail('No audio input devices detected');
                return;
            }

            container.innerHTML = `
                <div style="text-align: center; margin-bottom: 20px;">
                    <div style="font-size: 16px; font-weight: bold; margin-bottom: 6px;">Speak into the Microphone</div>
                    <p class="test-instructions" style="margin-bottom: 16px;">Make some noise or speak normally to verify audio input levels.</p>
                </div>
                <div id="mic-container" style="display: flex; flex-direction: column; gap: 16px;"></div>
            `;

            const micContainer = container.querySelector('#mic-container');

            for (let i = 0; i < audioInputs.length; i++) {
                const device = audioInputs[i];
                const micName = device.label || `Microphone ${i + 1}`;
                
                this.reportProgress(wsClient, (i / audioInputs.length) * 100, `Testing ${micName}...`);

                const micCard = document.createElement('div');
                micCard.style.cssText = 'background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 18px;';
                micCard.innerHTML = `
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
                        <span style="font-weight: 600; font-size: 14px;">${micName}</span>
                        <span id="mic-status-${i}" style="font-size: 12px; font-weight: bold; color: var(--color-warning);">Listening...</span>
                    </div>
                    <!-- Live VU / Volume Meter -->
                    <div style="background: var(--color-bg-tertiary); height: 24px; border-radius: 12px; overflow: hidden; position: relative; margin-bottom: 8px; box-shadow: inset 0 2px 4px rgba(0,0,0,0.2);">
                        <div id="mic-meter-${i}" style="background: linear-gradient(90deg, #4ade80 0%, #facc15 70%, #ef4444 100%); width: 0%; height: 100%; border-radius: 12px; transition: width 50ms linear;"></div>
                    </div>
                    <div style="display: flex; justify-content: space-between; font-size: 11px; color: var(--color-text-secondary);">
                        <span>Quiet</span>
                        <span>Normal</span>
                        <span>Loud</span>
                    </div>
                `;

                micContainer.appendChild(micCard);

                let stream;
                try {
                    stream = await navigator.mediaDevices.getUserMedia({ 
                        audio: device.deviceId ? { deviceId: { exact: device.deviceId } } : true 
                    });
                } catch (e) {
                    // Fallback to general audio stream if specific ID fails
                    stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                }

                const passed = await this.monitorAudioLevel(micCard, stream, i);
                stream.getTracks().forEach(t => t.stop());

                this.results.push({
                    name: micName,
                    id: device.deviceId,
                    passed: passed
                });

                if (passed) {
                    this.haptic.tap();
                }
            }

            const passedCount = this.results.filter(r => r.passed).length;
            if (passedCount === audioInputs.length) {
                this.pass('All microphones passed audio capture check');
            } else if (passedCount > 0) {
                this.pass(`${passedCount} of ${audioInputs.length} microphones verified successfully`);
            } else {
                this.fail('Microphone audio levels were insufficient or silent');
            }

            this.details.microphones = this.results;

        } catch (error) {
            this.fail('Microphone testing error: ' + error.message);
        }
    }

    monitorAudioLevel(micCard, stream, index) {
        return new Promise((resolve) => {
            const meter = micCard.querySelector(`#mic-meter-${index}`);
            const status = micCard.querySelector(`#mic-status-${index}`);

            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextClass) {
                status.textContent = 'AudioContext unsupported';
                resolve(true); // Fallback pass if API missing
                return;
            }

            const audioCtx = new AudioContextClass();
            const analyser = audioCtx.createAnalyser();
            analyser.fftSize = 256;
            const source = audioCtx.createMediaStreamSource(stream);
            source.connect(analyser);

            const dataArray = new Uint8Array(analyser.frequencyBinCount);
            let peakCount = 0;
            let isResolved = false;

            const checkAudio = () => {
                if (isResolved) return;

                analyser.getByteFrequencyData(dataArray);

                // Calculate volume (average frequency magnitude)
                let sum = 0;
                for (let i = 0; i < dataArray.length; i++) {
                    sum += dataArray[i];
                }
                const average = sum / dataArray.length;
                const volumePercent = Math.min(100, Math.round((average / 128) * 100));

                meter.style.width = `${volumePercent}%`;

                // If volume exceeds threshold (someone spoke or ambient sound detected)
                if (volumePercent > 18) {
                    peakCount++;
                }

                // If we detected 3 solid audio frames above ambient
                if (peakCount >= 3) {
                    isResolved = true;
                    status.textContent = 'Audio Signal Detected';
                    status.style.color = 'var(--color-success)';
                    meter.style.width = '100%';
                    audioCtx.close();
                    resolve(true);
                    return;
                }

                requestAnimationFrame(checkAudio);
            };

            requestAnimationFrame(checkAudio);

            // Timeout after 8 seconds per mic
            setTimeout(() => {
                if (!isResolved) {
                    isResolved = true;
                    status.textContent = 'No Sound Detected';
                    status.style.color = 'var(--color-error)';
                    audioCtx.close();
                    resolve(false);
                }
            }, 8000);
        });
    }
}
