import test from 'node:test';
import assert from 'node:assert/strict';
import { runCommand } from '../modules/CommandDispatch.js';

function fakeRunner() {
    const calls = [];
    return {
        calls,
        startSuite: () => calls.push(['startSuite']),
        startTest: (id) => calls.push(['startTest', id]),
        stopSuite: () => calls.push(['stopSuite'])
    };
}

test('auto start is what the desktop asks for when the address goes out', () => {
    const runner = fakeRunner();

    const ran = runCommand({ type: 'auto_start_suite', sessionId: 'DEVICE_1' }, runner);

    assert.equal(ran, 'auto_start_suite');
    assert.deepEqual(runner.calls, [['startSuite']]);
});

test('a single test is started with the id that was asked for', () => {
    const runner = fakeRunner();

    const ran = runCommand({ type: 'test_start', testId: 'camera' }, runner);

    assert.equal(ran, 'test_start');
    assert.deepEqual(runner.calls, [['startTest', 'camera']]);
});

test('stopping the suite is a command like any other', () => {
    const runner = fakeRunner();

    assert.equal(runCommand({ type: 'stop_suite' }, runner), 'stop_suite');
    assert.deepEqual(runner.calls, [['stopSuite']]);
});

test('nothing is done when there is no command', () => {
    const runner = fakeRunner();

    assert.equal(runCommand(undefined, runner), null);
    assert.equal(runCommand(null, runner), null);
    assert.equal(runCommand({}, runner), null);
    assert.equal(runCommand({ type: '' }, runner), null);
    assert.deepEqual(runner.calls, []);
});

test('an unknown command is not mistaken for one that ran', () => {
    const runner = fakeRunner();

    assert.equal(runCommand({ type: 'launch_missiles' }, runner), null);
    assert.deepEqual(runner.calls, []);
});

test('a command arriving before the suite exists is held, not run blind', () => {
    assert.equal(runCommand({ type: 'auto_start_suite' }, null), null);
    assert.equal(runCommand({ type: 'auto_start_suite' }, undefined), null);
    assert.equal(runCommand({ type: 'test_start' }, null), null);
    assert.equal(runCommand({ type: 'test_start', testId: null }, fakeRunner()), null);
});
