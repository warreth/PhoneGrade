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
import { CapabilityScanner } from '../modules/CapabilityScanner.js';
import { CameraTest } from '../modules/CameraTest.js';

// Stands in for a live MediaStream: tracks record when they are stopped.
function fakeStream() {
    const tracks = [
        { stopped: false, stop() { this.stopped = true; } },
        { stopped: false, stop() { this.stopped = true; } }
    ];
    return { tracks, getTracks: () => tracks };
}

test('CameraTest.dispose stops the live stream so an abandoned step frees the camera', () => {
    const cam = new CameraTest();
    const stream = fakeStream();
    cam._stream = stream;

    // The runner calls this when a step ends by skip or by the 90s failsafe,
    // so the camera must be released even though run() never reached its own
    // cleanup.
    cam.dispose();

    assert.equal(cam._stream, null, 'the reference must be dropped, not just stopped');
    assert.ok(stream.tracks.every(t => t.stopped), 'every track must be stopped');
});

test('CameraTest.dispose is safe to call twice and with no stream', () => {
    const cam = new CameraTest();

    assert.doesNotThrow(() => cam.dispose());

    cam._stream = fakeStream();
    cam.dispose();
    // The runner can call it again after the test already cleaned up.
    assert.doesNotThrow(() => cam.dispose());
});

test('DeviceTest.dispose is a no-op so tests without resources are unaffected', () => {
    const t = new DeviceTest('x', 'X', 'd');
    assert.doesNotThrow(() => t.dispose());
});

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

test('CapabilityScanner reports the APIs Node actually lacks', () => {
    const scanner = new CapabilityScanner();
    const result = scanner.scan();

    // Node has no geolocation or camera, so those must be reported...
    assert.equal(result.results['navigator.geolocation'], false);
    assert.equal(result.results['navigator.mediaDevices.getUserMedia'], false);
    assert.equal(result.results['navigator.wakeLock'], false);
    // ...while the vibrate stub the test harness installs must not be.
    assert.equal(result.results['navigator.vibrate'], true);

    assert.equal(result.allPresent, false);

    // Every entry in missing must be one the scanner found false, and every
    // false entry must appear there - the two views cannot drift apart.
    const falses = Object.entries(result.results).filter(([, ok]) => !ok).map(([name]) => name);
    assert.deepEqual([...result.missing].sort(), falses.sort());
    for (const name of result.missing) {
        assert.ok(scanner.requiredApis[name], `${name} must be a known API`);
    }
});

test('CapabilityScanner sends one report per missing API and marks them as gaps', async () => {
    const sent = [];
    const scanner = new CapabilityScanner();
    scanner.scan();

    await scanner.reportMissing({
        baseUrl: 'http://localhost:5056',
        sessionId: 'TEST_SUITE_123'
    }, (url, options) => {
        sent.push({ url, body: JSON.parse(options.body) });
        return Promise.resolve({ ok: true });
    });

    assert.ok(sent.length > 0, 'at least one report must be sent');
    for (const { url, body } of sent) {
        assert.equal(url, 'http://localhost:5056/api/pwa/log-warning');
        assert.equal(body.sessionId, 'TEST_SUITE_123');
        // A scan only fires when the property is absent, so it is always a real
        // capability gap and never a refused permission prompt.
        assert.equal(body.reason, 'missing');
        assert.ok(body.missingApi.length > 0);
    }
});

test('CapabilityScanner does nothing when every API is present', async () => {
    const scanner = new CapabilityScanner();
    // Stand in for a browser that exposes everything we ask about.
    scanner.requiredApis = {
        'navigator.vibrate': () => true,
        'navigator.geolocation': () => true
    };
    const result = scanner.scan();

    assert.equal(result.allPresent, true);
    assert.deepEqual(result.missing, []);

    const sent = [];
    await scanner.reportMissing(
        { baseUrl: 'http://localhost:5056', sessionId: 'TEST_SUITE_123' },
        (url, options) => { sent.push(url); return Promise.resolve({ ok: true }); }
    );
    assert.equal(sent.length, 0, 'a clean device must not be reported at all');
});

test('CapabilityScanner swallows a failed report instead of breaking the suite', async () => {
    const scanner = new CapabilityScanner();
    scanner.scan();

    // The failure path logs by design; keep it out of the test output.
    const origWarn = console.warn;
    console.warn = () => {};
    try {
        await assert.doesNotReject(() => scanner.reportMissing(
            { baseUrl: 'http://localhost:5056', sessionId: 'TEST_SUITE_123' },
            () => Promise.reject(new Error('connection refused'))
        ));
    } finally {
        console.warn = origWarn;
    }
});

