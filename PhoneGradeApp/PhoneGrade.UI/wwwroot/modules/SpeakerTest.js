import { DeviceTest } from './DeviceTest.js';

export class SpeakerTest extends DeviceTest {
    constructor() {
        super('speaker', 'Luidspreker & Oorstuk', 'Controleer de oorluidspreker en hoofdluidspreker');
        this.audioContext = null;
        this.earpieceWorking = false;
        this.loudspeakerWorking = false;
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, 'Audiotest voorbereiden...');

        container.innerHTML = `
            <div style="position: fixed; inset: 0; background: var(--color-bg-primary); z-index: 10000; overflow-y: auto; padding: 20px; display: flex; flex-direction: column; align-items: center;">
                <div style="max-width: 450px; width: 100%;">
                    <h3 style="color: var(--color-text-primary); margin-bottom: 12px; font-size: 22px; text-align: center; font-weight: bold;">Audio &amp; Luidsprekers</h3>
                    
                    <div style="background: #fef3c7; border: 1px solid #f59e0b; color: #92400e; padding: 12px; border-radius: 8px; margin-bottom: 20px; font-weight: 600; text-align: center; font-size: 13px;">
                        ⚠️ Zet de stille modus schakelaar UIT en zet je mediavolume op 100%!
                    </div>

                    <div id="earpiece-section" style="margin-bottom: 20px; padding: 18px; background: var(--color-bg-secondary); border-radius: 12px; border: 1px solid var(--color-border); box-shadow: var(--shadow-sm); text-align: center;">
                        <p style="margin-bottom: 8px; font-weight: bold; font-size: 15px; color: var(--color-text-primary);">1. Bovenste Oorluidspreker (Earpiece)</p>
                        <p style="margin-bottom: 16px; font-size: 13px; color: var(--color-text-secondary);">Houd het toestel tegen je oor zodra de beltoon start.</p>
                        <button id="play-earpiece-btn" class="btn btn-primary" style="width: 100%; margin-bottom: 14px;">Speel Beltoon</button>
                        <div id="earpiece-feedback" style="display: none; justify-content: center; gap: 10px;">
                            <button id="earpiece-yes" class="btn btn-success" style="flex: 1;">Ik hoorde het goed</button>
                            <button id="earpiece-no" class="btn btn-danger" style="flex: 1;">Niets gehoord</button>
                        </div>
                        <p id="earpiece-status" style="text-align: center; margin-top: 10px; color: var(--color-text-tertiary); font-size: 13px; font-weight: 600;">Wacht op test...</p>
                    </div>
                    
                    <div id="loudspeaker-section" style="padding: 18px; background: var(--color-bg-secondary); border-radius: 12px; border: 1px solid var(--color-border); box-shadow: var(--shadow-sm); opacity: 0.5; pointer-events: none; text-align: center;">
                        <p style="margin-bottom: 8px; font-weight: bold; font-size: 15px; color: var(--color-text-primary);">2. Onderste Hoofdluidspreker (Loudspeaker)</p>
                        <p style="margin-bottom: 16px; font-size: 13px; color: var(--color-text-secondary);">Een heldere beltoon speelt af over de hoofdluidspreker.</p>
                        <button id="play-loud-btn" class="btn btn-primary" style="width: 100%; margin-bottom: 14px;">Speel Beltoon</button>
                        <div id="loud-feedback" style="display: none; justify-content: center; gap: 10px;">
                            <button id="loud-yes" class="btn btn-success" style="flex: 1;">Ik hoorde het goed</button>
                            <button id="loud-no" class="btn btn-danger" style="flex: 1;">Niets gehoord / kraakt</button>
                        </div>
                        <p id="loud-status" style="text-align: center; margin-top: 10px; color: var(--color-text-tertiary); font-size: 13px; font-weight: 600;">Wacht op test...</p>
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

        const initAudio = () => {
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
                    initAudio();
                    playEarpieceBtn.disabled = true;
                    earpieceStatus.textContent = 'Beltoon speelt af (zacht)...';
                    this.playChime(0.12, () => {
                        playEarpieceBtn.style.display = 'none';
                        earpieceFeedback.style.display = 'flex';
                        earpieceStatus.textContent = 'Heb je de beltoon duidelijk gehoord?';
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
                    earpieceStatus.textContent = 'Defect / Geen geluid';
                    earpieceStatus.style.color = 'var(--color-error)';
                    earpieceFeedback.style.display = 'none';
                    loudSection.style.opacity = '1';
                    loudSection.style.pointerEvents = 'auto';
                    res();
                };
            });

            const testLoudspeaker = () => new Promise((res) => {
                playLoudBtn.onclick = () => {
                    initAudio();
                    playLoudBtn.disabled = true;
                    loudStatus.textContent = 'Beltoon speelt af...';
                    this.playChime(1.0, () => {
                        playLoudBtn.style.display = 'none';
                        loudFeedback.style.display = 'flex';
                        loudStatus.textContent = 'Heb je de beltoon luid en helder gehoord?';
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
                    loudStatus.textContent = 'Defect / Geen geluid';
                    loudStatus.style.color = 'var(--color-error)';
                    loudFeedback.style.display = 'none';
                    res();
                };
            });

            this.reportProgress(wsClient, 10, 'Oorluidspreker testen...');
            await testEarpiece();

            this.reportProgress(wsClient, 50, 'Hoofdluidspreker testen...');
            await testLoudspeaker();

            this.reportProgress(wsClient, 100, 'Audiotests voltooid');

            if (this.earpieceWorking && this.loudspeakerWorking) {
                this.pass('Zowel oorluidspreker als hoofdluidspreker werken uitstekend');
            } else if (this.earpieceWorking || this.loudspeakerWorking) {
                const w = this.earpieceWorking ? 'Oorluidspreker' : 'Hoofdluidspreker';
                const f = this.earpieceWorking ? 'Hoofdluidspreker' : 'Oorluidspreker';
                this.fail(w + ' werkt, maar ' + f + ' is defect');
            } else {
                this.fail('Geen van beide luidsprekers werkt');
            }

            this.details.earpiece = this.earpieceWorking;
            this.details.loudspeaker = this.loudspeakerWorking;

            if (this.audioContext) {
                try { this.audioContext.close(); } catch (e) {}
            }
            resolve();
        });
    }

    playChime(volume, onComplete) {
        if (!this.audioContext) return;
        
        // Gentle marimba/chime sequence: C5, E5, G5, B5, C6 (warm bells)
        const notes = [523.25, 659.25, 783.99, 987.77, 1046.50];
        const stepTime = 0.22;
        let t = this.audioContext.currentTime;

        notes.forEach((freq, idx) => {
            const osc = this.audioContext.createOscillator();
            const gain = this.audioContext.createGain();
            
            // Warm sine + soft harmonics for natural acoustic bell tone
            osc.type = 'sine';
            osc.frequency.setValueAtTime(freq, t);

            gain.gain.setValueAtTime(0, t);
            gain.gain.linearRampToValueAtTime(volume * 0.7, t + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.001, t + 0.45);

            osc.connect(gain);
            gain.connect(this.audioContext.destination);

            osc.start(t);
            osc.stop(t + 0.5);

            t += stepTime;
        });

        setTimeout(onComplete, (notes.length * stepTime + 0.5) * 1000);
    }
}
