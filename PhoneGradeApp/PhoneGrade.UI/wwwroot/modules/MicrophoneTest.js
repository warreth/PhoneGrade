import { DeviceTest } from './DeviceTest.js';

export class MicrophoneTest extends DeviceTest {
    constructor() {
        super('microphone', 'Microphone Array', 'Test all available microphones with playback');
        this.results = [];
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Checking media devices...');

        try {
            await navigator.mediaDevices.getUserMedia({ audio: true });
            
            const devices = await navigator.mediaDevices.enumerateDevices();
            const audioInputs = devices.filter(d => d.kind === 'audioinput');

            if (audioInputs.length === 0) {
                this.fail('No microphones detected');
                return;
            }

            container.innerHTML = `
                <h3 style="color: var(--color-accent); margin-bottom: 16px;">Multi-Microphone Test</h3>
                <p class="test-instructions" style="margin-bottom: 24px;">We will record a short clip from each microphone and play it back to you.</p>
                <div id="mic-container"></div>
            `;

            const micContainer = container.querySelector('#mic-container');

            for (let i = 0; i < audioInputs.length; i++) {
                const device = audioInputs[i];
                const micName = device.label || `Microphone ${i + 1}`;
                
                this.reportProgress(wsClient, (i / audioInputs.length) * 100, `Testing ${micName}...`);
                
                const micUI = document.createElement('div');
                micUI.style.cssText = 'background: var(--color-bg-secondary); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 16px; margin-bottom: 16px;';
                
                micUI.innerHTML = `
                    <p style="font-weight: 600; margin-bottom: 12px;">${micName}</p>
                    <div id="mic-status-${i}" style="margin-bottom: 16px; color: var(--color-text-secondary); font-size: 14px;">Waiting to record...</div>
                    <button id="btn-record-${i}" class="btn btn-primary" style="width: 100%; margin-bottom: 12px;">Start Recording (3s)</button>
                    <div id="playback-section-${i}" style="display: none; flex-direction: column; gap: 12px;">
                        <audio id="audio-player-${i}" controls style="width: 100%; margin-bottom: 12px;"></audio>
                        <p style="font-size: 14px; text-align: center;">Was the recording clear?</p>
                        <div style="display: flex; gap: 12px;">
                            <button id="btn-yes-${i}" class="btn btn-success" style="flex: 1; background: var(--color-success); color: #000; border: none;">Yes, clear</button>
                            <button id="btn-no-${i}" class="btn btn-error" style="flex: 1; background: var(--color-error); color: #fff; border: none;">No / Distorted</button>
                        </div>
                    </div>
                `;
                
                micContainer.appendChild(micUI);

                const stream = await navigator.mediaDevices.getUserMedia({ 
                    audio: { deviceId: { exact: device.deviceId } } 
                });
                
                const passed = await this.recordAndVerify(micUI, stream, i);
                stream.getTracks().forEach(t => t.stop());

                this.results.push({
                    name: micName,
                    id: device.deviceId,
                    passed: passed
                });
                
                micUI.style.opacity = '0.6';
            }

            this.reportProgress(wsClient, 100, 'Evaluation complete');

            const passedCount = this.results.filter(r => r.passed).length;
            if (passedCount === audioInputs.length) {
                this.pass('All microphones passed');
            } else if (passedCount > 0) {
                this.fail(`${passedCount} of ${audioInputs.length} microphones passed`);
            } else {
                this.fail('All microphones failed or were unclear');
            }

            this.details.microphones = this.results;

        } catch (error) {
            this.fail('Microphone API error: ' + error.message);
        }
    }

    async recordAndVerify(uiContainer, stream, index) {
        return new Promise((resolve) => {
            const recordBtn = uiContainer.querySelector(`#btn-record-${index}`);
            const statusDiv = uiContainer.querySelector(`#mic-status-${index}`);
            const playbackSection = uiContainer.querySelector(`#playback-section-${index}`);
            const audioPlayer = uiContainer.querySelector(`#audio-player-${index}`);
            const btnYes = uiContainer.querySelector(`#btn-yes-${index}`);
            const btnNo = uiContainer.querySelector(`#btn-no-${index}`);

            let mediaRecorder;
            let audioChunks = [];

            recordBtn.onclick = () => {
                recordBtn.style.display = 'none';
                statusDiv.textContent = 'Recording... Speak now!';
                statusDiv.style.color = 'var(--color-accent)';
                
                audioChunks = [];
                mediaRecorder = new MediaRecorder(stream);
                
                mediaRecorder.ondataavailable = e => {
                    if (e.data.size > 0) audioChunks.push(e.data);
                };
                
                mediaRecorder.onstop = () => {
                    const audioBlob = new Blob(audioChunks, { type: 'audio/webm' });
                    const audioUrl = URL.createObjectURL(audioBlob);
                    audioPlayer.src = audioUrl;
                    
                    statusDiv.textContent = 'Playback testing';
                    statusDiv.style.color = 'var(--color-text-secondary)';
                    playbackSection.style.display = 'flex';
                };
                
                mediaRecorder.start();
                setTimeout(() => {
                    if (mediaRecorder.state === 'recording') {
                        mediaRecorder.stop();
                    }
                }, 3000);
            };

            btnYes.onclick = () => {
                statusDiv.textContent = 'Passed';
                statusDiv.style.color = 'var(--color-success)';
                playbackSection.style.pointerEvents = 'none';
                resolve(true);
            };

            btnNo.onclick = () => {
                statusDiv.textContent = 'Failed';
                statusDiv.style.color = 'var(--color-error)';
                playbackSection.style.pointerEvents = 'none';
                resolve(false);
            };
        });
    }
}