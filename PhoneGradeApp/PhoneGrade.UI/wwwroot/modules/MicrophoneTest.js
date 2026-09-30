import { t } from './i18n.js';
import { DeviceTest } from './DeviceTest.js';
import {
    CAPABILITY,
    classifyMediaError,
    explainMediaError,
    hasMediaDevices
} from './MediaCapability.js';

/**
 * The microphone, checked by watching the level move while the operator talks.
 *
 * The old step ran getUserMedia inside a try, and any failure at all fell
 * straight through to a recording made with the phone's own voice recorder. On a
 * phone where the operator had simply refused the microphone prompt, that
 * replaced a measurement with a different test: the recorder app records with
 * the same hardware the prompt was protecting, so the two are not
 * interchangeable, and a phone with a dead microphone could still be passed by
 * pressing "yes, clear" on a recording that never had any sound in it.
 *
 * A refusal is now shown as a refusal, with a retry. The recorder is kept for the
 * one case it is actually for: a browser that does not expose the microphone API
 * at all, where there is no measurement to make and a human listening back is
 * the only check available.
 *
 * The stream and the audio context are released in dispose(), which the runner
 * calls on every exit. Without it a skipped microphone step left the input open,
 * and the phone kept showing its microphone indicator for the rest of the run.
 */
export class MicrophoneTest extends DeviceTest {
    constructor() {
        super('microphone', t('microphone.title'), t('microphone.stepDescription'));
        this._stream = null;
        this._audioCtx = null;
    }

    /**
     * The live meter needs ten seconds of quiet before it gives up, and the
     * recorder fallback needs the operator to record and listen back. 90 s covers
     * the first and not the second.
     */
    getFailsafeMs() {
        return 150000;
    }

    dispose() {
        this.stopCapture();
    }

    stopCapture() {
        if (this._stream) {
            this._stream.getTracks().forEach(track => track.stop());
            this._stream = null;
        }
        if (this._audioCtx) {
            try { this._audioCtx.close(); } catch (e) { /* already closed */ }
            this._audioCtx = null;
        }
    }

    async run(wsClient, container) {
        this.start();
        this.reportProgress(wsClient, 0, t('microphone.progressTesting'));

        if (!hasMediaDevices()) {
            await this.reportCapabilityGap(wsClient, 'navigator.mediaDevices.getUserMedia', 'missing');
            return this.runRecorderFallback(wsClient, container, CAPABILITY.MISSING);
        }

        let lastResult = null;

        // Retrying is the operator's choice, so the loop only ends when they
        // decide, when a measurement comes back, or when the runner cuts in.
        // Nothing here falls through to the recorder on its own.
        //
        // Only the permission call is inside the try. The measurement is not: a
        // fault in the meter is a fault in this step, and letting it land back
        // here turned a broken level meter into a second "the browser does not
        // support this" question, which is both untrue and unanswerable.
        while (true) {
            let stream = null;
            try {
                stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            } catch (err) {
                lastResult = classifyMediaError(err);

                if (lastResult.kind === CAPABILITY.DENIED || lastResult.kind === CAPABILITY.MISSING) {
                    await this.reportCapabilityGap(wsClient, 'navigator.mediaDevices.getUserMedia', lastResult.kind);
                }
            }

            if (stream) {
                this._stream = stream;
                try {
                    return await this.runLiveMeterTest(wsClient, container, stream);
                } catch (err) {
                    this.stopCapture();
                    const text = t('microphone.levelMeterFailed', { error: err && err.message ? err.message : String(err) });
                    return this.fail(text);
                }
            }

            const choice = await this.showMicTrouble(wsClient, container, lastResult);

            if (choice === 'retry') continue;

            if (choice === 'recorder') {
                return this.runRecorderFallback(wsClient, container, lastResult.kind);
            }

            return this.fail(explainMediaError(lastResult, t('microphone.noun')));
        }
    }

    /** Explains why the microphone would not open, and offers the real choices. */
    showMicTrouble(wsClient, container, result) {
        const fixable = result.fixable;
        const canFallBack = result.kind === CAPABILITY.MISSING || result.kind === CAPABILITY.INSECURE;

        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('microphone.title')}</h3>
                    <div class="step-card">
                        <p class="step-note">${explainMediaError(result, t('microphone.noun'))}</p>
                        <div class="step-stack">
                            ${fixable ? `<button id="mic-retry" class="btn btn-primary step-block">${t('microphone.retryButton')}</button>` : ''}
                            ${canFallBack ? `<button id="mic-recorder" class="btn btn-secondary step-block">${t('microphone.recorderButton')}</button>` : ''}
                            <button id="mic-reject" class="btn btn-danger step-block">${t('microphone.defectiveButton')}</button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        this.reportProgress(wsClient, 0, t('microphone.accessNeededProgress'));

        return new Promise((resolve) => {
            const retry = container.querySelector('#mic-retry');
            const recorder = container.querySelector('#mic-recorder');
            const reject = container.querySelector('#mic-reject');

            if (retry) retry.onclick = () => resolve('retry');
            if (recorder) recorder.onclick = () => resolve('recorder');
            reject.onclick = () => resolve('reject');
        });
    }

    /**
     * The last resort for a browser with no microphone API: record with the
     * phone's own app and listen back.
     *
     * Reached only from the card above, so it is always the operator's choice and
     * never a silent substitution. The note says which check produced the verdict.
     */
    async runRecorderFallback(wsClient, container, kind) {
        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('microphone.title')}</h3>
                    <div class="step-card">
                        <p class="step-lead">
                            ${t('microphone.recorderLead')}
                        </p>
                        <label class="btn btn-primary step-block" for="audio-file-input">
                            ${t('microphone.speakIntoLabel')}
                            <input type="file" id="audio-file-input" accept="audio/*" capture hidden>
                        </label>
                        <p id="audio-status" class="step-hint">${t('microphone.waitingForRecording')}</p>
                        <div id="audio-feedback" class="step-stack" hidden>
                            <audio id="audio-preview" controls class="mic-preview"></audio>
                            <p class="step-question">${t('microphone.hearYourselfQuestion')}</p>
                            <div class="step-actions">
                                <button id="mic-yes" class="btn btn-success">${t('microphone.yesClear')}</button>
                                <button id="mic-no" class="btn btn-danger">${t('microphone.noNoise')}</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;

        const audioInput = container.querySelector('#audio-file-input');
        const audioFeedback = container.querySelector('#audio-feedback');
        const audioPreview = container.querySelector('#audio-preview');
        const audioStatus = container.querySelector('#audio-status');
        const btnYes = container.querySelector('#mic-yes');
        const btnNo = container.querySelector('#mic-no');

        audioInput.addEventListener('change', (e) => {
            if (e.target.files && e.target.files.length > 0) {
                audioPreview.src = URL.createObjectURL(e.target.files[0]);
                audioStatus.hidden = true;
                audioFeedback.hidden = false;
            }
        });

        return new Promise((resolve) => {
            const settle = (passed, notes) => {
                this.details.checkMethod = 'recorder';
                this.details.capabilityGap = kind || null;
                this.details.passed = passed;
                this.reportProgress(wsClient, 100, passed ? t('microphone.progressListenedBack') : t('microphone.progressNotAudible'));
                if (passed) this.pass(notes);
                else this.fail(notes);
                resolve();
            };

            btnYes.onclick = () => settle(true, t('microphone.recorderPassNotes'));
            btnNo.onclick = () => settle(false, t('microphone.recorderFailNotes'));
        });
    }

    async runLiveMeterTest(wsClient, container, stream) {
        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('microphone.title')}</h3>
                    <div class="step-card">
                        <p class="step-lead">${t('microphone.meterLead')}</p>
                        <div class="mic-meter">
                            <div id="mic-vu-bar" class="mic-meter-fill"></div>
                        </div>
                        <p id="live-mic-status" class="sensor-status">${t('microphone.listeningStatus')}</p>
                    </div>
                </div>
            </div>
        `;

        const vuBar = container.querySelector('#mic-vu-bar');
        const statusText = container.querySelector('#live-mic-status');

        const AudioCtor = window.AudioContext || window.webkitAudioContext;
        if (!AudioCtor) {
            this.stopCapture();
            return this.fail(t('microphone.cannotMeasureSignal'));
        }

        this._audioCtx = new AudioCtor();
        const analyser = this._audioCtx.createAnalyser();
        const source = this._audioCtx.createMediaStreamSource(stream);
        source.connect(analyser);
        analyser.fftSize = 256;

        const dataArray = new Uint8Array(analyser.frequencyBinCount);
        let peakLevel = 0;
        let settled = false;

        return new Promise((resolve) => {
            const finish = (passed, notes) => {
                if (settled) return;
                settled = true;
                this.stopCapture();
                this.details.checkMethod = 'live-meter';
                this.details.peakLevel = peakLevel;
                if (passed) this.pass(notes);
                else this.fail(notes);
                resolve();
            };

            const checkAudio = () => {
                if (settled) return;

                analyser.getByteFrequencyData(dataArray);
                let sum = 0;
                for (let i = 0; i < dataArray.length; i++) sum += dataArray[i];
                const avg = sum / dataArray.length;
                const pct = Math.min(100, Math.round((avg / 128) * 100));

                if (vuBar) vuBar.style.width = pct + '%';
                if (pct > peakLevel) peakLevel = pct;

                if (peakLevel > 20) {
                    if (statusText) {
                        statusText.textContent = t('microphone.soundDetected', { peak: peakLevel });
                        statusText.style.color = 'var(--color-success-text)';
                    }
                    setTimeout(() => {
                        this.reportProgress(wsClient, 100, t('microphone.progressWorking'));
                        finish(true, t('microphone.registersSound', { peak: peakLevel }));
                    }, 1000);
                    return;
                }

                requestAnimationFrame(checkAudio);
            };

            requestAnimationFrame(checkAudio);

            setTimeout(() => {
                if (peakLevel <= 20) {
                    this.reportProgress(wsClient, 100, t('microphone.progressNoSignal'));
                    finish(false, t('microphone.noSignalMeasured'));
                }
            }, 10000);
        });
    }

    async reportCapabilityGap(wsClient, missingApi, reason) {
        if (!wsClient || !wsClient.sessionId) return;
        try {
            const ua = navigator.userAgent;
            let osVersion = 'Unknown';
            if (/Android/.test(ua)) osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || 'Android Unknown';
            else if (/iPhone|iPad|iPod/.test(ua)) osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || 'iOS Unknown';

            await fetch(`${wsClient.baseUrl}/api/pwa/log-warning`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    sessionId: wsClient.sessionId,
                    missingApi,
                    userAgent: ua,
                    osVersion,
                    reason
                })
            });
        } catch (e) {
            console.warn('Could not report the unusable microphone API:', e);
        }
    }
}
