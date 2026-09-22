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
            <div style="position: fixed; inset: 0; background: var(--color-bg-primary); z-index: 10000; overflow-y: auto; padding: 24px;">
                <h3 style="color: var(--color-text-primary); margin-bottom: 16px; font-size: 24px; text-align: center;">Audio Test</h3>
                
                <div style="background: #fef3c7; border: 1px solid #f59e0b; color: #92400e; padding: 12px; border-radius: 8px; margin-bottom: 24px; font-weight: 600; text-align: center;">
                    ⚠️ Zet stille modus uit en zet je media volume aan!
                </div>

                <div id="speaker-test-area" style="margin: 20px 0; max-width: 500px; margin-left: auto; margin-right: auto;">
                    <div id="earpiece-section" style="margin-bottom: 24px; padding: 16px; background: var(--color-bg-secondary); border-radius: var(--radius-lg); border: 1px solid var(--color-border); box-shadow: var(--shadow-sm);">
                        <p style="margin-bottom: 12px; font-weight: 600; color: var(--color-text-primary);">Top Oorluidspreker (Earpiece):</p>
                        <p style="margin-bottom: 16px; font-size: 13px; color: var(--color-text-secondary);">Houd de telefoon tegen je oor na het klikken op de knop.</p>
                        <button id="play-earpiece-btn" class="btn btn-primary" style="width: 100%; margin-bottom: 16px;">Speel Geluid</button>
                        <div id="earpiece-feedback" style="display: none; justify-content: center; gap: 12px;">
                            <button id="earpiece-yes" class="btn btn-success" style="flex: 1;">Ik hoorde het</button>
                            <button id="earpiece-no" class="btn btn-danger" style="flex: 1;">Geen geluid</button>
                        </div>
                        <p id="earpiece-status" style="text-align: center; margin-top: 12px; color: var(--color-text-tertiary); font-size: 13px; font-weight: 600;">Niet getest</p>
                    </div>
                    
                    <div id="loudspeaker-section" style="padding: 16px; background: var(--color-bg-secondary); border-radius: var(--radius-lg); border: 1px solid var(--color-border); box-shadow: var(--shadow-sm); opacity: 0.5; pointer-events: none;">
                        <p style="margin-bottom: 12px; font-weight: 600; color: var(--color-text-primary);">Onderste Luidspreker (Loudspeaker):</p>
                        <p style="margin-bottom: 16px; font-size: 13px; color: var(--color-text-secondary);">Een helder geluid wordt afgespeeld via de hoofdluidspreker.</p>
                        <button id="play-loud-btn" class="btn btn-primary" style="width: 100%; margin-bottom: 16px;">Speel Geluid</button>
                        <div id="loud-feedback" style="display: none; justify-content: center; gap: 12px;">
                            <button id="loud-yes" class="btn btn-success" style="flex: 1;">Ik hoorde het</button>
                            <button id="loud-no" class="btn btn-danger" style="flex: 1;">Geen geluid</button>
                        </div>
                        <p id="loud-status" style="text-align: center; margin-top: 12px; color: var(--color-text-tertiary); font-size: 13px; font-weight: 600;">Niet getest</p>
                    </div>
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
                    earpieceStatus.textContent = 'Bezig met afspelen...';
                    this.playMelody(0.05, () => {
                        playEarpieceBtn.style.display = 'none';
                        earpieceFeedback.style.display = 'flex';
                        earpieceStatus.textContent = 'Hoorde je het geluid (oorluidspreker)?';
                    });
                };

                earpieceYes.onclick = () => {
                    this.earpieceWorking = true;
                    earpieceStatus.textContent = 'Geslaagd';
                    earpieceStatus.style.color = 'var(--color-success)';
                    earpieceFeedback.style.display = 'none';
                    loudSection.style.opacity = '1';
                    loudSection.style.pointerEvents = 'auto';
                    res();
                };

                earpieceNo.onclick = () => {
                    this.earpieceWorking = false;
                    earpieceStatus.textContent = 'Gefaald';
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
                    loudStatus.textContent = 'Bezig met afspelen...';
                    this.playMelody(1.0, () => {
                        playLoudBtn.style.display = 'none';
                        loudFeedback.style.display = 'flex';
                        loudStatus.textContent = 'Hoorde je het geluid (hoofdluidspreker)?';
                    });
                };

                loudYes.onclick = () => {
                    this.loudspeakerWorking = true;
                    loudStatus.textContent = 'Geslaagd';
                    loudStatus.style.color = 'var(--color-success)';
                    loudFeedback.style.display = 'none';
                    res();
                };

                loudNo.onclick = () => {
                    this.loudspeakerWorking = false;
                    loudStatus.textContent = 'Gefaald';
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
                this.fail(working + ' is working, but ' + failing + ' failed');
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

    playMelody(volume, onComplete) {
        if (!this.audioContext) return;
        
        // C major arpeggio melody: C4, E4, G4, C5
        const notes = [261.63, 329.63, 392.00, 523.25];
        const noteLength = 0.15;
        
        let startTime = this.audioContext.currentTime;
        
        for (let i = 0; i < notes.length; i++) {
            const osc = this.audioContext.createOscillator();
            const gain = this.audioContext.createGain();
            
            osc.type = 'triangle';
            osc.frequency.value = notes[i];
            
            gain.gain.setValueAtTime(0, startTime);
            gain.gain.linearRampToValueAtTime(volume, startTime + 0.05);
            gain.gain.setValueAtTime(volume, startTime + noteLength - 0.05);
            gain.gain.linearRampToValueAtTime(0, startTime + noteLength);
            
            osc.connect(gain);
            gain.connect(this.audioContext.destination);
            
            osc.start(startTime);
            osc.stop(startTime + noteLength);
            
            startTime += noteLength;
        }
        
        // Final chord (C major)
        const finalLength = 0.8;
        for (let i = 0; i < notes.length; i++) {
            const osc = this.audioContext.createOscillator();
            const gain = this.audioContext.createGain();
            
            osc.type = 'triangle';
            osc.frequency.value = notes[i];
            
            gain.gain.setValueAtTime(0, startTime);
            gain.gain.linearRampToValueAtTime(volume * 0.5, startTime + 0.1);
            gain.gain.setValueAtTime(volume * 0.5, startTime + finalLength - 0.2);
            gain.gain.linearRampToValueAtTime(0, startTime + finalLength);
            
            osc.connect(gain);
            gain.connect(this.audioContext.destination);
            
            osc.start(startTime);
            osc.stop(startTime + finalLength);
        }
        
        setTimeout(onComplete, (notes.length * noteLength + finalLength) * 1000 + 200);
    }
}
