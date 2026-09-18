import test from 'node:test';
import assert from 'node:assert/strict';

// Set up mock DOM environment for Node testing
global.window = {
    devicePixelRatio: 2,
    location: { search: '?sessionId=TEST_SUITE_123', protocol: 'http:', host: 'localhost:5055' }
};

global.document = {
    body: {
        style: {
            overflow: '',
            touchAction: '',
            position: '',
            width: '',
            height: ''
        }
    },
    addEventListener: (event, handler) => {},
    removeEventListener: (event, handler) => {},
    head: {
        appendChild: () => {}
    },
    getElementById: (id) => null
};

Object.defineProperty(global, 'navigator', {
    value: {
        vibrate: (pattern) => {
            global._lastVibration = pattern;
            return true;
        },
        userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)'
    },
    writable: true,
    configurable: true
});

import { ViewportLocker } from '../modules/ViewportLocker.js';
import { HapticFeedback } from '../modules/HapticFeedback.js';
import { DeviceTest } from '../modules/DeviceTest.js';

test('ViewportLocker locks and unlocks document body styles properly', () => {
    const locker = new ViewportLocker();
    
    assert.equal(locker.isLocked, false);
    
    locker.lock();
    assert.equal(locker.isLocked, true);
    assert.equal(document.body.style.overflow, 'hidden');
    assert.equal(document.body.style.touchAction, 'none');
    assert.equal(document.body.style.position, 'fixed');
    
    locker.unlock();
    assert.equal(locker.isLocked, false);
    assert.equal(document.body.style.position, '');
});

test('HapticFeedback triggers vibration patterns when supported', () => {
    const haptic = new HapticFeedback();
    assert.equal(haptic.isSupported, true);
    
    haptic.tap();
    assert.equal(global._lastVibration, 10);
    
    haptic.success();
    assert.deepEqual(global._lastVibration, [50, 50, 50]);
    
    haptic.error();
    assert.equal(global._lastVibration, 200);
    
    haptic.progress();
    assert.equal(global._lastVibration, 20);
});

test('DeviceTest lifecycle records start, duration, pass, and fail notes', () => {
    class DummyTest extends DeviceTest {
        constructor() {
            super('dummy', 'Dummy Test', 'Description of dummy test');
        }
    }
    
    const testInst = new DummyTest();
    assert.equal(testInst.id, 'dummy');
    assert.equal(testInst.status, 'pending');
    
    testInst.start();
    assert.equal(testInst.status, 'running');
    assert.ok(testInst.startTime > 0);
    
    testInst.pass('All checks passed');
    assert.equal(testInst.status, 'passed');
    assert.equal(testInst.notes, 'All checks passed');
    assert.ok(testInst.endTime >= testInst.startTime);
    assert.ok(testInst.getDuration() >= 0);
    
    const json = testInst.toJSON();
    assert.equal(json.id, 'dummy');
    assert.equal(json.status, 'passed');
    assert.equal(json.notes, 'All checks passed');
});

test('DeviceTest fail marks status and stores failure reason', () => {
    class FailingTest extends DeviceTest {
        constructor() {
            super('fail_test', 'Failing Test', 'Will fail');
        }
    }
    
    const testInst = new FailingTest();
    testInst.start();
    testInst.fail('Hardware unresponsive');
    
    assert.equal(testInst.status, 'failed');
    assert.equal(testInst.notes, 'Hardware unresponsive');
});
