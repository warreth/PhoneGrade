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

import { SensorTest } from '../modules/SensorTest.js';

test('SensorTest skip and log warning when DeviceMotionEvent is undefined (unsupported)', async () => {
    // Temporarily remove DeviceMotionEvent
    const originalDeviceMotionEvent = global.DeviceMotionEvent;
    global.DeviceMotionEvent = undefined;
    
    let warningLogged = false;
    let reportedMissingApi = null;
    
    const mockWsClient = {
        baseUrl: 'http://localhost',
        sessionId: 'test_session_fallback',
    };
    
    const originalFetch = global.fetch;
    global.fetch = async (url, options) => {
        if (url.includes('/api/pwa/log-warning')) {
            warningLogged = true;
            const body = JSON.parse(options.body);
            reportedMissingApi = body.missingApi;
            return { ok: true };
        }
        return { ok: true };
    };

    const document = {
        createElement: () => ({
            innerHTML: '',
            querySelector: (sel) => ({
                style: {},
                onclick: null
            })
        })
    };
    const originalDoc = global.document;
    global.document = document;
    
    const container = {
        innerHTML: '',
        querySelector: (sel) => {
            if (sel === '#btn-request-sensors') {
                return { onclick: null };
            }
            return { style: {}, textContent: '' };
        }
    };

    const sensorTest = new SensorTest();
    sensorTest.skip = function(notes) {
        this.status = 'skipped';
        this.notes = notes;
    };
    
    // The innerHTML gets set, which drops our mocked buttons in a real DOM.
    // In our node test environment, we just need to grab the mock.
    const runPromise = sensorTest.run(mockWsClient, container);
    
    // Simulate user clicking the "Grant Motion Sensor Access" button
    const btnRequest = container.querySelector('#btn-request-sensors');
    if (btnRequest && btnRequest.onclick) {
        await btnRequest.onclick();
    }
    
    await runPromise;
    
    assert.equal(sensorTest.status, 'skipped');
    assert.equal(sensorTest.notes, 'DeviceMotionEvent not supported');
    assert.equal(warningLogged, true);
    assert.equal(reportedMissingApi, 'DeviceMotionEvent');
    
    // Restore
    global.DeviceMotionEvent = originalDeviceMotionEvent;
    global.fetch = originalFetch;
    global.document = originalDoc;
});

test('SensorTest skip and log warning when permission is denied', async () => {
    // Mock DeviceMotionEvent with requestPermission returning 'denied'
    const originalDeviceMotionEvent = global.DeviceMotionEvent;
    global.DeviceMotionEvent = {
        requestPermission: async () => 'denied'
    };
    
    let warningLogged = false;
    let reportedMissingApi = null;
    
    const mockWsClient = {
        baseUrl: 'http://localhost',
        sessionId: 'test_session_denied',
    };
    
    const originalFetch = global.fetch;
    global.fetch = async (url, options) => {
        if (url.includes('/api/pwa/log-warning')) {
            warningLogged = true;
            const body = JSON.parse(options.body);
            reportedMissingApi = body.missingApi;
            return { ok: true };
        }
        return { ok: true };
    };

    const document = {
        createElement: () => ({
            innerHTML: '',
            querySelector: (sel) => ({
                style: {},
                onclick: null
            })
        })
    };
    const originalDoc = global.document;
    global.document = document;

    const container = {
        innerHTML: '',
        querySelector: (sel) => {
            if (sel === '#btn-request-sensors') {
                return { onclick: null };
            }
            return { style: {}, textContent: '' };
        }
    };

    const sensorTest = new SensorTest();
    sensorTest.skip = function(notes) {
        this.status = 'skipped';
        this.notes = notes;
    };
    
    const runPromise = sensorTest.run(mockWsClient, container);
    
    const btnRequest = container.querySelector('#btn-request-sensors');
    if (btnRequest && btnRequest.onclick) {
        await btnRequest.onclick();
    }
    
    await runPromise;
    
    assert.equal(sensorTest.status, 'skipped');
    assert.equal(sensorTest.notes, 'Permission denied for DeviceMotionEvent');
    assert.equal(warningLogged, true);
    assert.equal(reportedMissingApi, 'DeviceMotionEvent');
    
    // Restore
    global.DeviceMotionEvent = originalDeviceMotionEvent;
    global.fetch = originalFetch;
    global.document = originalDoc;
});

