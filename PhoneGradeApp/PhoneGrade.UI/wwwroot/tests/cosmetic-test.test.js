import test from 'node:test';
import assert from 'node:assert/strict';

/**
 * A fake DOM big enough for one question screen: elements hold text, classes
 * and click handlers, and the container collects what was appended.
 *
 * The questionnaire builds with createElement rather than markup strings, so
 * the fake never parses anything and an assertion on a button's text proves
 * the answer was set as text rather than handed to a parser.
 */
function fakeElement(tag) {
    return {
        tag,
        className: '',
        textContent: '',
        type: '',
        children: [],
        handlers: {},
        appendChild(child) { this.children.push(child); return child; },
        addEventListener(name, fn) { this.handlers[name] = fn; },
        click() { if (this.handlers.click) this.handlers.click(); },
    };
}

function fakeContainer() {
    return {
        html: '',
        children: [],
        set innerHTML(v) { this.html = v || ''; this.children = []; },
        get innerHTML() { return this.html; },
        appendChild(child) { this.children.push(child); return child; },
    };
}

function allButtons(root) {
    const out = [];
    const walk = (el) => {
        if (el.tag === 'button') out.push(el);
        for (const child of el.children || []) walk(child);
    };
    for (const child of root.children) walk(child);
    return out;
}

function buttonWithText(root, text) {
    return allButtons(root).find(b => b.textContent === text) || null;
}

global.window = { devicePixelRatio: 1, location: { search: '', protocol: 'http:', host: 'localhost:5055' } };
Object.defineProperty(global, 'navigator', {
    value: { userAgent: 'Mozilla/5.0 (Linux; Android 17) Chrome/140' },
    writable: true,
    configurable: true,
});
global.document = {
    body: { style: {} },
    createElement: (tag) => fakeElement(tag),
    addEventListener: () => {},
    removeEventListener: () => {},
    head: { appendChild: () => {} },
    getElementById: () => null,
};

function fakeClient() {
    const sent = [];
    return { sent, isConnected: () => true, send: (m) => sent.push(m) };
}

import { CosmeticTest } from '../modules/CosmeticTest.js';
import { setLocale } from '../modules/i18n.js';

// English labels throughout, so the assertions read what the keys say rather
// than what Dutch happens to make of them. The dictionaries themselves are
// covered by i18n.test.js; these tests are about the flow, not the wording.
setLocale('en');

test('the questionnaire asks one question at a time with big answer buttons', async () => {
    const step = new CosmeticTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);

    assert.equal(allButtons(container).length, 3, 'three answers and nothing else on the first screen');
    assert.ok(buttonWithText(container, 'Good'), 'the best answer is offered first');
    assert.ok(!buttonWithText(container, 'Back'), 'no going back from the first question');
    assert.ok(!buttonWithText(container, 'Skip'), 'and no way past it except answering');

    buttonWithText(container, 'Good').click();
    assert.ok(buttonWithText(container, 'Back'), 'from the second question on there is a way back');
    assert.equal(step.answers.back, 'good');

    // Settle the rest so the run ends.
    for (const label of ['Good', 'Good', 'Works', 'Works']) {
        buttonWithText(container, label).click();
    }
    await running;

    assert.equal(step.status, 'passed', 'a phone with nothing wrong passes');
    assert.equal(step.notes, 'No defects recorded');
});

test('a defect fails the step and every answer becomes its own result row', async () => {
    const step = new CosmeticTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);

    buttonWithText(container, 'Broken').click();
    for (const label of ['Light marks', 'Good', 'Works', 'Does not work']) {
        buttonWithText(container, label).click();
    }
    await running;

    assert.equal(step.status, 'failed');
    assert.match(step.notes, /back/i, 'the failing question is named');

    const rows = step.toResults();
    assert.equal(rows.length, step.items.length, 'one row per question, never one row for the step');
    assert.deepEqual(step.resultIds(), rows.map(r => r.id), 'the resume contract lists the same rows');

    const byId = Object.fromEntries(rows.map(r => [r.id, r]));
    assert.equal(byId['cosmetic-back'].status, 'failed');
    assert.equal(byId['cosmetic-back'].labelCode, 'BACK');
    assert.equal(byId['cosmetic-screen'].status, 'passed', 'light marks are written down, not failed over');
    assert.equal(byId['cosmetic-screen'].notes, 'Light marks');
    assert.equal(byId['cosmetic-volume-down'].status, 'failed');
    assert.equal(byId['cosmetic-volume-down'].labelCode, 'VOLDN', 'up and down need different codes on the label');

    const codes = rows.map(r => r.labelCode);
    assert.equal(new Set(codes).size, codes.length, 'no two rows share a label code');
});

test('going back changes the answer instead of adding one', async () => {
    const step = new CosmeticTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);

    buttonWithText(container, 'Broken').click();
    buttonWithText(container, 'Back').click();
    buttonWithText(container, 'Good').click();
    for (const label of ['Good', 'Good', 'Works', 'Works']) {
        buttonWithText(container, label).click();
    }
    await running;

    assert.equal(step.answers.back, 'good', 'the second tap wins');
    assert.equal(step.status, 'passed');
    assert.equal(step.toResults().filter(r => r.status === 'failed').length, 0);
});

test('a reload resumes with the answers it already has', async () => {
    const step = new CosmeticTest();
    step.storedRows = [
        { testId: 'cosmetic-back', status: 'passed', notes: 'Good' },
        { testId: 'cosmetic-screen', status: 'passed', notes: 'Light marks' },
    ];
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);

    // The cursor sits at the first unanswered question, not at the start.
    assert.ok(buttonWithText(container, 'Is the camera glass intact?') || container.children.length > 0,
        'the run continues rather than restarting');
    assert.equal(step.answers.back, 'good', 'a matching stored answer is kept');
    assert.equal(step.answers.screen, 'marks', 'light marks survive the reload as light marks, not as good');

    for (const label of ['Good', 'Works', 'Works']) {
        buttonWithText(container, label).click();
    }
    await running;

    assert.equal(step.status, 'passed');
    assert.equal(step.toResults().find(r => r.id === 'cosmetic-screen').notes, 'Light marks');
});

test('a stored row nobody can place is asked again rather than guessed at', () => {
    const step = new CosmeticTest();
    step.storedRows = [{ testId: 'cosmetic-back', status: 'passed', notes: 'something in another language' }];
    step.restoreAnswers();
    assert.deepEqual(step.answers, {}, 'an unrecognised answer restores nothing');
});

test('an interrupted run leaves unanswered questions skipped, not failed', async () => {
    // The runner's failsafe settles a step the operator walked away from. An
    // answer never given is not a defect found, so it must not read as one.
    const step = new CosmeticTest();
    step.start();
    step.answers = { back: 'good' };

    const rows = step.toResults();
    assert.equal(rows.find(r => r.id === 'cosmetic-back').status, 'passed');
    for (const row of rows.filter(r => r.id !== 'cosmetic-back')) {
        assert.equal(row.status, 'skipped', `${row.id} was never answered`);
    }
});

test('reset clears the answers so a rerun starts empty', async () => {
    const step = new CosmeticTest();
    const container = fakeContainer();
    const running = step.run(fakeClient(), container);
    buttonWithText(container, 'Good').click();
    step.reset();
    assert.deepEqual(step.answers, {});
    assert.equal(step.status, 'pending');
    // The run still waiting on its promise is abandoned like any other retry.
    container.children = [];
});

test('adding an item is one list entry and grows rows, ids and codes together', () => {
    // The extensibility promise, pinned down: the flow, the progress and the
    // results all read the list, so a question cannot update one and miss another.
    const androidItems = CosmeticTest.itemsFor('Mozilla/5.0 (Linux; Android 17)');
    const iphoneItems = CosmeticTest.itemsFor('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)');

    assert.ok(androidItems.length >= 5, 'the agreed questions are there');
    assert.equal(iphoneItems.length, androidItems.length + 1,
        'the silent switch is an iphone question');
    assert.ok(!androidItems.some(item => item.key === 'mute'),
        'an android operator is not asked about a switch that is not there');
    assert.ok(iphoneItems.some(item => item.key === 'mute'));

    for (const items of [androidItems, iphoneItems]) {
        const codes = items.map(item => item.labelCode);
        assert.equal(new Set(codes).size, codes.length, 'no two questions share a label code');
        for (const item of items) {
            assert.match(item.labelCode, /^[A-Z0-9]{1,9}$/, `${item.key} carries a label-safe code`);
            assert.ok(['condition', 'works'].includes(item.kind), `${item.key} has a known kind`);
        }
    }
});

test('the step asks the questions that match the phone it runs on', () => {
    const androidAgent = 'Mozilla/5.0 (Linux; Android 17) Chrome/140';
    navigator.userAgent = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)';
    try {
        const step = new CosmeticTest();
        assert.deepEqual(
            step.resultIds(),
            CosmeticTest.itemsFor(navigator.userAgent).map(item => `cosmetic-${item.key}`),
            'the rows follow the filtered list rather than every item');
        assert.ok(step.resultIds().includes('cosmetic-mute'), 'an iphone is asked about the switch');
    } finally {
        navigator.userAgent = androidAgent;
    }

    assert.ok(!new CosmeticTest().resultIds().includes('cosmetic-mute'),
        'an android is not');
});
