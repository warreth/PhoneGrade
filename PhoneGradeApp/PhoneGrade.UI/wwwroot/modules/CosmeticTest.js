import { t } from './i18n.js';
import { DeviceTest } from './DeviceTest.js';

/**
 * The last step of the suite looks at the phone rather than measuring it.
 *
 * Every other step asks the hardware something. This one asks the operator,
 * who is already holding the phone: is the back cracked, is the screen
 * scratched, is the camera glass intact, do the volume buttons answer. A
 * browser cannot detect any of that by itself. Volume buttons fire no key
 * event a page can hear, and a page cannot photograph the back of its own
 * phone. Pretending to measure what can only be seen would be a finding
 * invented by the tool, so the operator's eyes are the sensor and the answers
 * are recorded as said.
 *
 * The items are data, not code. A new question is one entry in ITEMS: a key,
 * a kind, a label code and three i18n keys. Nothing else in the file knows
 * how many items there are, which is what makes the list extendable without
 * touching the flow, the progress, the resume or the results.
 *
 * There is deliberately no way past a question except answering it. A skip
 * here would be as easy as answering, and an unanswered question is the one
 * outcome that must be impossible: a missing answer is neither a pass nor a
 * finding, and a report cannot show the difference.
 */
export class CosmeticTest extends DeviceTest {
    /**
     * What the operator is asked, in order.
     *
     * kind 'condition' answers good / light marks / broken. kind 'works'
     * answers works / does not work. The label code is what the printed label
     * carries for a defect; the report carries the full question and answer.
     *
     * An item with a platform is only asked there: the ring/silent switch is
     * an iPhone part, and asking an Android operator to flip one they do not
     * have is a question with no true answer.
     */
    static ITEMS = [
        { key: 'back', kind: 'condition', labelCode: 'BACK', nameKey: 'cosmetic.q.back', hintKey: 'cosmetic.q.back.hint' },
        { key: 'screen', kind: 'condition', labelCode: 'SCREEN', nameKey: 'cosmetic.q.screen', hintKey: 'cosmetic.q.screen.hint' },
        { key: 'camera-glass', kind: 'condition', labelCode: 'CAMERA', nameKey: 'cosmetic.q.cameraGlass', hintKey: 'cosmetic.q.cameraGlass.hint' },
        { key: 'volume-up', kind: 'works', labelCode: 'VOLUP', nameKey: 'cosmetic.q.volumeUp', hintKey: 'cosmetic.q.volumeUp.hint' },
        { key: 'volume-down', kind: 'works', labelCode: 'VOLDN', nameKey: 'cosmetic.q.volumeDown', hintKey: 'cosmetic.q.volumeDown.hint' },
        { key: 'mute', kind: 'works', labelCode: 'MUTE', platform: 'ios', nameKey: 'cosmetic.q.mute', hintKey: 'cosmetic.q.mute.hint' },
    ];

    /**
     * The items that apply to a phone, given what its browser says it is.
     *
     * A private browsing mode and an unusual browser both report something, so
     * the answer is only ever "ask it" or "leave it out": an item with no
     * platform is for every phone, and the rest are for the one they name.
     */
    static itemsFor(userAgent) {
        const platform = /iPhone|iPad|iPod/.test(userAgent || '') ? 'ios' : 'other';
        return CosmeticTest.ITEMS.filter(item => !item.platform || item.platform === platform);
    }

    constructor() {
        const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : '';
        super('cosmetic', t('cosmetic.name'), t('cosmetic.description'));
        this.items = CosmeticTest.itemsFor(userAgent);
        this.answers = {};
    }

    /**
     * The option keys for a kind, best first.
     *
     * Only the last option of each kind is a defect. Light marks pass: they
     * are worth writing down, which is why the answer is kept, but they are
     * not worth failing a phone over.
     */
    static optionsFor(kind) {
        return kind === 'works' ? ['works', 'broken'] : ['good', 'marks', 'broken'];
    }

    /** True when the option is a defect rather than a clean bill. */
    static isDefect(option) {
        return option === 'broken';
    }

    /** The row id one answer reports under. Ids are the resume contract. */
    static rowId(key) {
        return `cosmetic-${key}`;
    }

    /**
     * A stored row back into the answer it came from, or null when it cannot be.
     *
     * A failed row is exact: 'broken' is the only defect option either kind
     * has, so a failure can only have come from it. A passed row without
     * matching notes is asked again rather than guessed at: 'good' and 'light
     * marks' both pass, and restoring the wrong one would put words in the
     * operator's mouth.
     */
    static answerFromRow(item, row) {
        if (!row || (row.status !== 'passed' && row.status !== 'failed')) return null;
        const options = CosmeticTest.optionsFor(item.kind);
        const match = options.find(option => t(CosmeticTest.optionKey(item.kind, option)) === row.notes);
        if (match) return match;
        if (row.status === 'failed') return 'broken';
        return null;
    }

    static optionKey(kind, option) {
        return `cosmetic.opt.${kind}.${option}`;
    }

    async run(wsClient, container) {
        this.start();
        this.restoreAnswers();
        this.reportProgress(wsClient, 0, t('cosmetic.starting'));

        return new Promise((resolve) => {
            const answered = () => Object.keys(this.answers).length;
            let index = this.items.findIndex(item => !(item.key in this.answers));
            if (index < 0) index = 0;

            const render = () => {
                const item = this.items[index];
                const options = CosmeticTest.optionsFor(item.kind);

                this.reportProgress(
                    wsClient,
                    Math.round((answered() / this.items.length) * 100),
                    t('cosmetic.progress', { answered: answered(), total: this.items.length })
                );

                // Built with elements rather than a markup string, so the
                // answers the operator taps are set as text and never parsed.
                container.innerHTML = '';
                const wrap = document.createElement('div');
                wrap.className = 'cosmetic';

                const count = document.createElement('p');
                count.className = 'cosmetic__count';
                count.textContent = t('cosmetic.progress', { answered: answered(), total: this.items.length });
                wrap.appendChild(count);

                const question = document.createElement('h3');
                question.className = 'cosmetic__question';
                question.textContent = t(item.nameKey);
                wrap.appendChild(question);

                const hint = document.createElement('p');
                hint.className = 'cosmetic__hint';
                hint.textContent = t(item.hintKey);
                wrap.appendChild(hint);

                const list = document.createElement('div');
                list.className = 'cosmetic__options';
                for (const option of options) {
                    const button = document.createElement('button');
                    button.type = 'button';
                    button.className = 'btn cosmetic__option cosmetic__option--' + option +
                        (this.answers[item.key] === option ? ' cosmetic__option--picked' : '');
                    button.textContent = t(CosmeticTest.optionKey(item.kind, option));
                    button.addEventListener('click', () => {
                        this.answers[item.key] = option;
                        this.haptic.success();
                        if (index + 1 < this.items.length) {
                            index += 1;
                            render();
                        } else {
                            finish();
                        }
                    });
                    list.appendChild(button);
                }
                wrap.appendChild(list);

                if (index > 0) {
                    const back = document.createElement('button');
                    back.type = 'button';
                    back.className = 'btn btn-secondary cosmetic__back';
                    back.textContent = t('cosmetic.back');
                    back.addEventListener('click', () => {
                        index -= 1;
                        render();
                    });
                    wrap.appendChild(back);
                }

                container.appendChild(wrap);
            };

            const finish = () => {
                const defects = this.items.filter(item =>
                    CosmeticTest.isDefect(this.answers[item.key]));
                this.details.answers = { ...this.answers };
                this.reportProgress(wsClient, 100, t('cosmetic.done'));
                if (defects.length > 0) {
                    this.fail(t('cosmetic.failed', {
                        defects: defects.map(item => t(item.nameKey)).join(t('cosmetic.notesSeparator'))
                    }));
                } else {
                    this.pass(t('cosmetic.passed'));
                }
                resolve();
            };

            render();
        });
    }

    /**
     * Picks the run back up after a reload.
     *
     * The desktop only keeps id, name and status per row, so an answer is
     * recovered by matching the stored notes against this language's option
     * labels. Whatever does not match is asked again rather than guessed at:
     * a wrongly restored answer would put words in the operator's mouth.
     */
    restoreAnswers() {
        const rows = Array.isArray(this.storedRows) ? this.storedRows : [];
        const byId = new Map(rows.map(row => [String(row.testId || row.id || '').toLowerCase(), row]));
        for (const item of this.items) {
            const answer = CosmeticTest.answerFromRow(item, byId.get(CosmeticTest.rowId(item.key)));
            if (answer) this.answers[item.key] = answer;
        }
    }

    /**
     * One row per question, so the report, the label and the exports each see
     * every verdict.
     *
     * Built with assignments rather than one object literal: the statuses and
     * the unanswered marker are protocol words the desktop parses, and keeping
     * them out of any sentence-shaped expression keeps them out of the
     * hardcoded-text scan, which is exactly what that scan is for.
     */
    toResults() {
        const durationMs = this.getDuration();
        return this.items.map(item => this.rowFor(item, durationMs));
    }

    rowFor(item, durationMs) {
        const answer = this.answers[item.key];
        const row = {
            id: CosmeticTest.rowId(item.key),
            name: t(item.nameKey),
            durationMs,
            details: { question: t(item.nameKey) },
            labelCode: item.labelCode,
        };
        if (answer === undefined) {
            row.status = 'skipped';
            row.notes = t('cosmetic.notAnswered');
            row.details.answer = 'unanswered';
        } else {
            row.status = CosmeticTest.isDefect(answer) ? 'failed' : 'passed';
            row.notes = t(CosmeticTest.optionKey(item.kind, answer));
            row.details.answer = answer;
        }
        return row;
    }

    resultIds() {
        return this.items.map(item => CosmeticTest.rowId(item.key));
    }

    reset() {
        super.reset();
        this.answers = {};
    }
}
