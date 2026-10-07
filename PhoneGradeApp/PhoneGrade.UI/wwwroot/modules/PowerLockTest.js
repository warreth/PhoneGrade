import { DeviceTest } from './DeviceTest.js';
import { t } from './i18n.js';

/**
 * How long the operator gets to turn the screen off and back on.
 *
 * Long enough to press power, look at the dark screen and unlock again, short
 * enough that a suite is not held up by a step nobody finishes. It is a wait,
 * not a judgement: nothing fails when it runs out, the question is simply put
 * to the operator instead.
 */
export const POWER_WAIT_MS = 60000;

/** The two rows this step reports, named once so the report and the resume agree. */
export const POWER_ROW = 'powerlock-power';
export const BIOMETRIC_ROW = 'powerlock-biometric';

/**
 * Did the page see the screen go off and come back?
 *
 * A browser cannot see the power button, and a screenshot key combination is
 * something no page can observe at all. What it can see is the screen going
 * away and coming back, because a page that is put away reports itself as
 * hidden and reports itself visible again when it is returned to. That cycle
 * needs a lock and an unlock in between, so it is the power button's own effect
 * rather than a guess about it.
 */
export class VisibilityCycle {
    constructor() {
        this.hiddenAt = null;
        this.returned = false;
    }

    /** The page went away. The first one counts; a second means nothing new. */
    hidden(now = Date.now()) {
        if (this.hiddenAt !== null) return false;
        this.hiddenAt = now;
        return true;
    }

    /** The page came back, which only means something after a hidden. */
    visible(now = Date.now()) {
        if (this.hiddenAt === null || this.returned) return false;
        this.returned = true;
        this.hiddenMs = now - this.hiddenAt;
        return true;
    }

    get detected() {
        return this.hiddenAt !== null && this.returned;
    }
}

/** The row status for what the power stage settled on. */
export function powerStatus(answer) {
    return answer === 'no' ? 'failed' : 'passed';
}

/**
 * The row status for what the operator said about the unlock.
 *
 * "A code was asked" is not a defect: a phone with nothing enrolled asks for
 * its code, and a shop does not enroll a fingerprint on a device it is about to
 * sell. Only the explicit "biometrics did not work" is a finding.
 */
export function biometricStatus(answer) {
    if (answer === 'worked') return 'passed';
    if (answer === 'failed') return 'failed';
    return 'skipped';
}

/**
 * Whether the browser has a platform authenticator to exercise.
 *
 * true: ask the unlock question, the hardware is there.
 * false: the browser says there is none, so the step is unknown rather than failed.
 * null: this browser cannot be asked at all, or the page is on an address the
 * browser does not trust, and neither is a fault of the phone.
 */
export function platformAuthenticatorAvailable() {
    const holder = typeof window !== 'undefined' ? window : null;
    const api = holder && holder.PublicKeyCredential;
    if (!api || typeof api.isUserVerifyingPlatformAuthenticatorAvailable !== 'function') {
        return Promise.resolve(null);
    }
    try {
        return api.isUserVerifyingPlatformAuthenticatorAvailable()
            .then((value) => Boolean(value))
            .catch(() => null);
    } catch (e) {
        return Promise.resolve(null);
    }
}

/**
 * The power button and the unlock, as the twelfth step the site counts.
 *
 * The suite measures what a browser can measure; this step is the one pair of
 * things that only the operator can judge, reached through the one signal the
 * page does get. Power is confirmed by the screen leaving and returning, with
 * the operator's answer as the fallback, and the unlock question follows the
 * unlock the cycle just forced.
 */
export class PowerLockTest extends DeviceTest {
    constructor() {
        super('powerlock', t('powerlock.stepName'), t('powerlock.stepDescription'));
        this.resetAnswers();
    }

    resetAnswers() {
        this.powerAnswer = null;
        this.biometricAnswer = null;
        this.cycleDetected = false;
        this.biometricAvailable = null;
    }

    /**
     * The power wait plus the time the two questions need after it. The wait is
     * on the clock; the questions are not, because an operator who is answering
     * is not the reason a step overruns.
     */
    getFailsafeMs() {
        return POWER_WAIT_MS + 90000;
    }

    reset() {
        super.reset();
        this.resetAnswers();
    }

    async run(wsClient, container) {
        this.start();
        this.resetAnswers();
        this.reportProgress(wsClient, 0, t('powerlock.waiting'));

        const secure = typeof window === 'undefined' || window.isSecureContext !== false;
        this.biometricAvailable = secure ? await platformAuthenticatorAvailable() : null;

        const card = this.drawStage(container, 'powerlock.title', 'powerlock.instructions');
        this.powerAnswer = await this.waitForPower(card);
        this.reportProgress(wsClient, 50, t('powerlock.powerSettled'));

        if (this.biometricAvailable === true) {
            this.reportProgress(wsClient, 70, t('powerlock.askingUnlock'));
            const unlockCard = this.drawStage(
                container, 'powerlock.biometric.title', 'powerlock.biometric.hint');
            this.biometricAnswer = await this.chooseUnlock(unlockCard);
        }

        this.reportProgress(wsClient, 100, t('powerlock.done'));
        this.drawOutcome(container);
        this.applyStatus();
    }

    /**
     * Waits for the cycle, and asks when it does not come.
     *
     * While the screen is away the page is frozen by the browser in most cases,
     * so nothing here has to keep running: the visible event is what wakes the
     * step up, and the timer only times the wait, not the phone.
     */
    waitForPower(card) {
        return new Promise((resolve) => {
            const cycle = new VisibilityCycle();
            let settled = false;
            let timer = null;

            const cleanup = () => {
                settled = true;
                if (timer) clearTimeout(timer);
                timer = null;
                document.removeEventListener('visibilitychange', onVisibility);
                window.removeEventListener('pagehide', onHidden);
                window.removeEventListener('freeze', onHidden);
            };

            const finish = (answer) => {
                if (settled) return;
                cleanup();
                resolve(answer);
            };

            const onHidden = () => {
                cycle.hidden();
            };

            const onVisibility = () => {
                if (document.visibilityState !== 'visible') {
                    cycle.hidden();
                    return;
                }
                if (cycle.visible()) {
                    this.cycleDetected = true;
                    this.appendNote(card, t('powerlock.detectedPower'));
                    finish('yes');
                }
            };

            timer = setTimeout(() => {
                this.appendQuestion(card, t('powerlock.power.question'), [
                    { label: t('powerlock.power.yes'), onPick: () => finish('yes') },
                    { label: t('powerlock.power.no'), onPick: () => finish('no') }
                ]);
            }, POWER_WAIT_MS);

            document.addEventListener('visibilitychange', onVisibility);
            window.addEventListener('pagehide', onHidden);
            window.addEventListener('freeze', onHidden);
        });
    }

    /** How the phone came back from the lock, as only the operator saw it. */
    chooseUnlock(card) {
        return new Promise((resolve) => {
            this.appendOptions(card, [
                { label: t('powerlock.biometric.worked'), onPick: () => resolve('worked') },
                { label: t('powerlock.biometric.code'), onPick: () => resolve('code') },
                { label: t('powerlock.biometric.failed'), onPick: () => resolve('failed') }
            ]);
        });
    }

    /** The overall verdict reads off the two rows rather than beside them. */
    applyStatus() {
        const rows = this.toResults();
        const failed = rows.filter((row) => row.status === 'failed');

        if (failed.length > 0) {
            this.fail(failed.map((row) => row.notes).join(t('powerlock.separator')));
            return;
        }

        this.pass(rows.map((row) => row.notes).join(t('powerlock.separator')));
    }

    toResults() {
        const durationMs = this.getDuration();
        return [this.powerRow(durationMs), this.biometricRow(durationMs)];
    }

    powerRow(durationMs) {
        const row = {
            id: POWER_ROW,
            name: t('powerlock.power.name'),
            durationMs,
            details: { detected: this.cycleDetected },
            labelCode: 'PWR'
        };

        if (this.powerAnswer === 'yes') {
            row.status = powerStatus('yes');
            row.notes = this.cycleDetected
                ? t('powerlock.power.passDetected')
                : t('powerlock.power.passManual');
        } else if (this.powerAnswer === 'no') {
            row.status = powerStatus('no');
            row.notes = t('powerlock.power.fail');
        } else {
            row.status = 'skipped';
            row.notes = t('powerlock.notAnswered');
        }

        return row;
    }

    biometricRow(durationMs) {
        const row = {
            id: BIOMETRIC_ROW,
            name: t('powerlock.biometric.name'),
            durationMs,
            details: { available: this.biometricAvailable },
            labelCode: 'BIOM'
        };

        const answer = this.biometricAnswer;
        row.status = biometricStatus(answer);

        if (answer === 'worked') {
            row.notes = t('powerlock.biometric.pass');
        } else if (answer === 'failed') {
            row.notes = t('powerlock.biometric.fail');
        } else if (answer === 'code') {
            row.notes = t('powerlock.biometric.skipCode');
        } else if (this.powerAnswer === null) {
            row.notes = t('powerlock.notAnswered');
        } else if (this.biometricAvailable === false) {
            row.notes = t('powerlock.biometric.skipUnavailable');
        } else if (this.biometricAvailable === null) {
            row.notes = typeof window !== 'undefined' && window.isSecureContext === false
                ? t('powerlock.biometric.skipInsecure')
                : t('powerlock.biometric.skipNoApi');
        } else {
            row.notes = t('powerlock.notAnswered');
        }

        return row;
    }

    resultIds() {
        return [POWER_ROW, BIOMETRIC_ROW];
    }

    // ------------------------------------------------------------------
    // Screen building. Elements rather than markup strings, so every answer
    // and every sentence is set as text and never parsed.
    // ------------------------------------------------------------------

    drawStage(container, titleKey, bodyKey) {
        container.innerHTML = '';

        const screen = document.createElement('div');
        screen.className = 'step-screen';

        const column = document.createElement('div');
        column.className = 'step-column';

        const title = document.createElement('h3');
        title.className = 'step-title';
        title.textContent = t(titleKey);
        column.appendChild(title);

        const card = document.createElement('div');
        card.className = 'step-card';

        const body = document.createElement('p');
        body.className = 'step-lead';
        body.textContent = t(bodyKey);
        card.appendChild(body);

        column.appendChild(card);
        screen.appendChild(column);
        container.appendChild(screen);

        return card;
    }

    appendNote(card, sentence) {
        const note = document.createElement('p');
        note.className = 'step-note';
        note.textContent = sentence;
        card.appendChild(note);
        return note;
    }

    appendQuestion(card, question, options) {
        this.appendNote(card, question);
        return this.appendOptions(card, options);
    }

    appendOptions(card, options) {
        const row = document.createElement('div');
        row.className = 'powerlock-options';

        for (const option of options) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn powerlock-option';
            button.textContent = option.label;
            button.addEventListener('click', () => option.onPick());
            row.appendChild(button);
        }

        card.appendChild(row);
        return row;
    }

    appendFixes(card, steps) {
        const list = document.createElement('ol');
        list.className = 'fix-steps';

        for (const step of steps) {
            const item = document.createElement('li');
            // The fix sentences carry their own <b> around a setting name, the
            // same shape the location step uses, so they are written as markup
            // rather than shown with the tags in them.
            item.innerHTML = step;
            list.appendChild(item);
        }

        card.appendChild(list);
        return list;
    }

    /** The two verdicts, on the phone, before the results screen takes over. */
    drawOutcome(container) {
        const card = this.drawStage(container, 'powerlock.title', 'powerlock.done');

        for (const row of this.toResults()) {
            const line = document.createElement('p');
            line.className = 'step-note';
            line.textContent = row.notes;
            card.appendChild(line);
        }

        /* The one verdict the operator can act on: an unknown biometric on an
         * untrusted address is fixed in the settings on the pc, so the route is
         * shown where the verdict is read rather than on a screen that is
         * replaced a moment later. */
        if (this.biometricAvailable === null
            && typeof window !== 'undefined' && window.isSecureContext === false) {
            this.appendFixes(card, [
                t('powerlock.insecureFixUsb'),
                t('powerlock.insecureFixSecure')
            ]);
        }

        return card;
    }
}
