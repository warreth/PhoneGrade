import { t } from './i18n.js';
import { DeviceTest } from './DeviceTest.js';
import {
    CAPABILITY,
    classifyMediaError,
    explainMediaError,
    hasMediaDevices
} from './MediaCapability.js';

/**
 * The microphone, checked by playing back what it recorded.
 *
 * The step opens the microphone once and records three seconds of it, then
 * hands the clip straight back. The level bar alone said that something moved
 * the diaphragm. It never let the operator hear whether what came out was their
 * voice or a buzz, and that is the answer they are actually grading: a phone
 * whose microphone is wired to nothing and a phone with a scratchy one both move
 * a bar, so the bar cannot be the whole verdict.
 *
 * The level is still measured while the recording runs, and a clip nobody can be
 * heard in fails on that measurement before the question is ever asked. The yes
 * button must never be the only thing standing between a dead microphone and a
 * pass, which is the thing the meter was introduced to stop: an operator who
 * never listened to the recording is how one used to get through.
 *
 * A refusal is still shown as a refusal, with a retry. The recorder is kept for
 * the one case it is actually for: a browser that does not expose the microphone
 * API at all, where there is no measurement to make and a human listening back
 * is the only check available. A browser that can open the microphone but has no
 * MediaRecorder gets the meter, which is the same measurement without the clip.
 *
 * The stream and the audio context are released in dispose(), which the runner
 * calls on every exit. Without it a skipped microphone step left the input open,
 * and the phone kept showing its microphone indicator for the rest of the run.
 */
export class MicrophoneTest extends DeviceTest {
    /** How long the clip runs. Long enough to say a word, short enough to hear. */
    static RECORD_MS = 3000;

    constructor() {
        super('microphone', t('microphone.title'), t('microphone.stepDescription'));
        this._stream = null;
        this._audioCtx = null;
        this._recorder = null;
    }

    /**
     * The clip takes three seconds to make and then the operator's ear, and the
     * recorder fallback takes a recording made in another app first. 90 s covers
     * neither.
     */
    getFailsafeMs() {
        return 150000;
    }

    dispose() {
        this.stopCapture();
    }

    stopCapture() {
        if (this._recorder) {
            // A recorder left running holds the input open exactly as the stream
            // does, and leaving it open is the thing dispose() exists to prevent.
            try {
                if (this._recorder.state !== 'inactive') this._recorder.stop();
            } catch (e) { /* the track went first */ }
            this._recorder = null;
        }
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

        // First, enumerate audio input devices to allow microphone selection
        let audioInputs = [];
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            audioInputs = devices.filter(d => d.kind === 'audioinput' && d.deviceId);
        } catch (err) {
            // enumeration failed, continue without selection
            console.warn('Could not enumerate audio devices:', err);
        }

        // If multiple microphones found, let the operator choose
        if (audioInputs.length > 1) {
            const selectedDeviceId = await this.showMicSelection(wsClient, container, audioInputs);
            if (!selectedDeviceId) {
                return this.fail(t('microphone.noSelection'));
            }
            // Store the selected deviceId for use in getUserMedia
            this._selectedDeviceId = selectedDeviceId;
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
                const constraints = this._selectedDeviceId
                    ? { audio: { deviceId: { exact: this._selectedDeviceId } } }
                    : { audio: true };
                stream = await navigator.mediaDevices.getUserMedia(constraints);
            } catch (err) {
                lastResult = classifyMediaError(err);

                if (lastResult.kind === CAPABILITY.DENIED || lastResult.kind === CAPABILITY.MISSING) {
                    await this.reportCapabilityGap(wsClient, 'navigator.mediaDevices.getUserMedia', lastResult.kind);
                }
            }

            if (stream) {
                this._stream = stream;
                try {
                    return await this.runPlaybackTest(wsClient, container, stream);
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
     * Shows a microphone selection UI when multiple audio inputs are available.
     * Returns the selected deviceId or null if cancelled.
     */
    async showMicSelection(wsClient, container, audioInputs) {
        return new Promise((resolve) => {
            const optionsHtml = audioInputs.map((device, index) => {
                const label = device.label || `${t(index === 0 ? 'microphone.bottomMic' : 'microphone.topMic')} (${index + 1})`;
                return `<option value="${device.deviceId}">${label}</option>`;
            }).join('');

            container.innerHTML = `
                <div class="step-screen">
                    <div class="step-column">
                        <h3 class="step-title">${t('microphone.title')}</h3>
                        <div class="step-card">
                            <p class="step-lead">${t('microphone.micSelectionHint')}</p>
                            <div class="step-stack">
                                <select id="mic-select" class="form-select mic-select">
                                    <option value="" disabled selected>${t('microphone.selectMic')}</option>
                                    ${optionsHtml}
                                </select>
                                <button id="mic-select-confirm" class="btn btn-primary step-block">${t('microphone.testSelected')}</button>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            this.reportProgress(wsClient, 0, t('microphone.progressTesting'));

            const select = container.querySelector('#mic-select');
            const confirmBtn = container.querySelector('#mic-select-confirm');

            select.addEventListener('change', () => {
                confirmBtn.disabled = !select.value;
            });

            confirmBtn.addEventListener('click', () => {
                if (select.value) {
                    resolve(select.value);
                }
            });

            // No cancel button - user must select a microphone
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

    /**
     * Measures the microphone while it runs, and paints the bar as it does.
     *
     * Both paths this step can take need the same two things: a level the
     * operator can watch, and a peak a verdict is made on. The number comes off
     * the byte spectrum the analyser fills, so it is a figure the phone measured
     * rather than one the step made up. The analysis context is kept on the step
     * so dispose() releases it along with the stream.
     *
     * Returns null when the browser has no AudioContext, which is the one case
     * where there is nothing to measure with.
     */
    startMeter(stream, vuBar, onLevel) {
        const AudioCtor = window.AudioContext || window.webkitAudioContext;
        if (!AudioCtor) return null;

        this._audioCtx = new AudioCtor();
        const analyser = this._audioCtx.createAnalyser();
        const source = this._audioCtx.createMediaStreamSource(stream);
        source.connect(analyser);
        analyser.fftSize = 256;

        const dataArray = new Uint8Array(analyser.frequencyBinCount);
        let peak = 0;
        let stopped = false;

        const measure = () => {
            if (stopped) return;

            analyser.getByteFrequencyData(dataArray);
            let sum = 0;
            for (let i = 0; i < dataArray.length; i++) sum += dataArray[i];
            const pct = Math.min(100, Math.round(((sum / dataArray.length) / 128) * 100));

            if (vuBar) vuBar.style.width = pct + '%';
            if (pct > peak) peak = pct;
            onLevel(pct, peak);

            requestAnimationFrame(measure);
        };

        requestAnimationFrame(measure);

        return {
            peak: () => peak,
            // The loop parks itself rather than being cancelled, so a step that has
            // settled stops measuring even where there is no handle to cancel the
            // frame with.
            stop: () => { stopped = true; }
        };
    }

    /**
     * The path for a browser with no MediaRecorder: the level is the whole
     * answer, so the step ends on the measurement and nothing is handed back.
     */
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

        return new Promise((resolve) => {
            let settled = false;
            let verdictTimer = null;
            let meter = null;

            const finish = (passed, notes) => {
                if (settled) return;
                settled = true;
                if (verdictTimer) clearTimeout(verdictTimer);
                if (meter) meter.stop();
                this.stopCapture();
                this.details.checkMethod = 'live-meter';
                this.details.peakLevel = meter ? meter.peak() : 0;
                // The runner settles a step from under a pending timer just as it
                // settles one that is still waiting on the operator, and a verdict
                // written after that would replace the one it gave.
                if (this.status === 'running') {
                    if (passed) this.pass(notes);
                    else this.fail(notes);
                }
                resolve();
            };

            meter = this.startMeter(stream, vuBar, (pct, peak) => {
                if (peak <= 20) return;
                if (statusText) {
                    statusText.textContent = t('microphone.soundDetected', { peak });
                    statusText.style.color = 'var(--color-success-text)';
                }
                if (verdictTimer) return;
                // Sound has answered. One more second of it, so a syllable that
                // happened to pass does not settle the step on its own.
                verdictTimer = setTimeout(() => {
                    this.reportProgress(wsClient, 100, t('microphone.progressWorking'));
                    finish(true, t('microphone.registersSound', { peak: meter.peak() }));
                }, 1000);
            });

            if (!meter) {
                this.stopCapture();
                this.fail(t('microphone.cannotMeasureSignal'));
                resolve();
                return;
            }

            setTimeout(() => {
                if (meter.peak() <= 20) {
                    this.reportProgress(wsClient, 100, t('microphone.progressNoSignal'));
                    finish(false, t('microphone.noSignalMeasured'));
                }
            }, 10000);
        });
    }

    /**
     * The main path: record three seconds of the open stream, then hand the clip
     * back to be heard.
     *
     * Reached with the microphone already open, so the permission prompt this
     * step is really about has been answered. A browser that cannot record is
     * sent to the meter rather than failed, because the measurement is still
     * there even when the clip is not.
     */
    async runPlaybackTest(wsClient, container, stream) {
        if (typeof MediaRecorder === 'undefined') {
            return this.runLiveMeterTest(wsClient, container, stream);
        }

        let recorder;
        try {
            recorder = new MediaRecorder(stream);
        } catch (err) {
            // The constructor is there and this stream is not recordable. The
            // level still is, so the step does not fail for a browser's opinion
            // about codecs.
            return this.runLiveMeterTest(wsClient, container, stream);
        }

        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('microphone.title')}</h3>
                    <div class="step-card">
                        <p class="step-lead">${t('microphone.recordLead')}</p>
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

        return new Promise((resolve) => {
            const chunks = [];
            let settled = false;
            let meter = null;

            const finish = (passed, notes, progress) => {
                if (settled) return;
                settled = true;
                if (meter) meter.stop();
                this.stopCapture();
                this.details.checkMethod = 'record-and-replay';
                this.details.peakLevel = meter ? meter.peak() : 0;
                if (this.status === 'running') {
                    this.reportProgress(wsClient, 100, progress);
                    if (passed) this.pass(notes);
                    else this.fail(notes);
                }
                resolve();
            };

            meter = this.startMeter(stream, vuBar, (pct, peak) => {
                if (peak <= 20 || !statusText) return;
                statusText.textContent = t('microphone.soundDetected', { peak });
                statusText.style.color = 'var(--color-success-text)';
            });

            if (!meter) {
                this.stopCapture();
                this.fail(t('microphone.cannotMeasureSignal'));
                resolve();
                return;
            }

            this._recorder = recorder;

            recorder.ondataavailable = (event) => {
                if (event.data && event.data.size > 0) chunks.push(event.data);
            };

            recorder.onstop = () => {
                // The runner ends the step from under a recording just as it ends
                // one from under a timer. Anything after that is the old step
                // writing on a screen it no longer owns.
                if (settled || this.status !== 'running') return;

                const peak = meter.peak();

                if (peak <= 20) {
                    // The measurement comes before the question. A clip nobody can
                    // be heard in fails here, so the yes button is never the only
                    // thing between a dead microphone and a pass.
                    finish(false, t('microphone.noSignalMeasured'), t('microphone.progressNoSignal'));
                    return;
                }

                if (chunks.length === 0) {
                    // The level moved and nothing was written down, which is a
                    // recording that failed rather than a microphone that did.
                    finish(false, t('microphone.playbackFailNotes'), t('microphone.progressNotAudible'));
                    return;
                }

                const clip = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' });
                this.showPlayback(container, URL.createObjectURL(clip), peak, finish);
            };

            recorder.start();
            setTimeout(() => {
                if (recorder.state !== 'inactive') recorder.stop();
            }, MicrophoneTest.RECORD_MS);
        });
    }

    /**
     * Plays the clip back and waits for the operator's answer.
     *
     * The question is the one the recorder fallback has always asked, because it
     * is the same judgement about the same thing: what this microphone put out.
     * Only a clip with something in it gets this far, so nobody is ever asked to * listen to silence.
     */
    showPlayback(container, clipUrl, peak, settle) {
        container.innerHTML = `
            <div class="step-screen">
                <div class="step-column">
                    <h3 class="step-title">${t('microphone.title')}</h3>
                    <div class="step-card">
                        <p class="step-lead">${t('microphone.soundDetected', { peak })}</p>
                        <audio id="mic-playback" controls class="mic-preview"></audio>
                        <p class="step-question">${t('microphone.hearYourselfQuestion')}</p>
                        <div class="step-actions">
                            <button id="mic-yes" class="btn btn-success">${t('microphone.yesClear')}</button>
                            <button id="mic-no" class="btn btn-danger">${t('microphone.noNoise')}</button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        const clip = container.querySelector('#mic-playback');
        const yes = container.querySelector('#mic-yes');
        const no = container.querySelector('#mic-no');
        clip.src = clipUrl;

        yes.onclick = () => settle(true, t('microphone.playbackPassNotes', { peak }), t('microphone.progressListenedBack'));
        no.onclick = () => settle(false, t('microphone.playbackFailNotes'), t('microphone.progressNotAudible'));
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
