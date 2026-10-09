import test, { beforeEach } from 'node:test';
import assert from 'node:assert/strict';

import {
    DENIED,
    BUSY,
    abandon,
    fakeClient,
    fakeContainer,
    fireWindow,
    reportedGaps,
    resetPage,
    setMotionApis,
    tick,
    useGetUserMedia,
    windowListenerCount,
    windowRemovalCount
} from './helpers/fake-dom.js';

/**
 * The three steps that ended themselves.
 *
 * Camera, motion and microphone all asked the browser for something that can be
 * refused, and all three answered a refusal the same way: skip. On a real Pixel
 * that meant a phone with a working camera, a working microphone and working
 * motion sensors came off the bench with gaps on the label, purely because a
 * prompt had not been answered yet. The fourth of these steps, the location
 * one, is in location-step.test.js.
 *
 * The rule these tests pin down is one rule: nothing in these steps may reach a
 * verdict on the operator's behalf. A refusal has to leave the step waiting,
 * with something the operator can press. A retry has to be possible. A verdict
 * still taken without asking has to rest on something the phone measured, a
 * genuinely absent API or a microphone nobody spoke into, rather than on a mood.
 *
 * Measured on a Pixel 8 Pro over adb reverse, which is what made this a real
 * problem and not a theory: on a secure origin the four APIs are all present and
 * the camera hands over a real 480x640 frame, and on a plain http LAN address
 * none of them are. Everything below happens in the first world.
 */

beforeEach(resetPage);

// ------------------------------------------------------------------ motion

const { SensorTest } = await import('../modules/SensorTest.js');

test('motion: a browser with no motion API is asked a question, not skipped', async () => {
    setMotionApis(false);
    const sensor = new SensorTest();
    const container = fakeContainer();

    const run = sensor.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-sensors').press();
    await tick();

    // This is the bug. A phone with a perfectly good orientation sensor was
    // recorded as a phone that could not be tested, and the operator never got to
    // look at it. Measured on the real phone: with the page in front of somebody,
    // the motion events do arrive, so the sensors were there all along.
    assert.equal(sensor.status, 'running');
    assert.equal(container.nodes.get('sensor-fallback-area').hidden, false);
    assert.match(container.nodes.get('sensor-fallback-reason').textContent, /DeviceMotionEvent/);

    // And the answer is the operator's, taken through a real verdict.
    container.nodes.get('sensor-manual-yes').press();
    await run;

    assert.equal(sensor.status, 'passed');
    assert.equal(sensor.details.rotationConfirmed, true);
    assert.equal(sensor.details.manualCheckUsed, true);

    // The label has to say the sensors were not read, or the next person to read
    // the report will assume they were.
    assert.match(sensor.notes, /niet rechtstreeks uitgelezen/);
});

test('motion: saying no to the rotation check fails the step, it does not skip it', async () => {
    setMotionApis(false);
    const sensor = new SensorTest();
    const container = fakeContainer();

    const run = sensor.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-sensors').press();
    await tick();
    container.nodes.get('sensor-manual-no').press();
    await run;

    // A phone whose screen does not follow the rotation has a real fault. It used
    // to be recorded as a gap, which is a much softer and quite different thing.
    assert.equal(sensor.status, 'failed');
    assert.match(sensor.notes, /roteert niet/);
});

test('motion: the report says which half of the sensor step was not measured', async () => {
    setMotionApis(false);
    const sensor = new SensorTest();
    const container = fakeContainer();

    sensor.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-sensors').press();
    await tick();

    // The gap is named so the desktop can tell "the browser could not do it" from
    // "the hardware could not do it", and neither sensor is claimed to have been
    // read.
    assert.equal(sensor.details.capabilityGap, 'DeviceMotionEvent');
    assert.equal(sensor.details.accelerometer, undefined);
    assert.equal(sensor.details.gyroscope, undefined);
    assert.match(reportedGaps().join(' '), /missing/);

    container.nodes.get('sensor-manual-yes').press();
    await tick();
    setMotionApis(false);
});

test('motion: real sensor data decides the step, and the manual card stays away', async () => {
    setMotionApis(true);
    const sensor = new SensorTest();
    const container = fakeContainer();

    const run = sensor.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-sensors').press();
    await tick();

    assert.equal(container.nodes.get('sensor-data-area').hidden, false);
    assert.equal(container.nodes.get('sensor-fallback-area').hidden, true);

    fireWindow('devicemotion', { accelerationIncludingGravity: { x: 0, y: 0, z: 9.8 } });
    fireWindow('deviceorientation', { beta: 40, gamma: 0 });
    await run;

    assert.equal(sensor.status, 'passed');
    assert.equal(sensor.details.accelerometer, true);
    assert.equal(sensor.details.gyroscope, true);
    assert.equal(sensor.details.manualCheckUsed, false);
    assert.match(sensor.notes, /reageren op beweging/);
    setMotionApis(false);
});

test('motion: a sensor that wakes up late still produces the real verdict', async () => {
    setMotionApis(true);
    const sensor = new SensorTest();
    const container = fakeContainer();

    const run = sensor.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-sensors').press();
    await tick();

    // Nothing for the listen window, so the manual check is offered. The listeners
    // are deliberately left on: the old step tore them down, and a sensor that
    // needed a second longer to wake was never going to be seen.
    await tick(SensorTest.LISTEN_WINDOW_MS + 50);
    assert.equal(container.nodes.get('sensor-fallback-area').hidden, false);

    fireWindow('devicemotion', { accelerationIncludingGravity: { x: 3, y: 0, z: 9 } });
    fireWindow('deviceorientation', { beta: 0, gamma: 30 });
    await run;

    assert.equal(sensor.status, 'passed');
    assert.equal(sensor.details.manualCheckUsed, false);
    setMotionApis(false);
});

test('motion: the listeners come off when the step ends, however it ended', async () => {
    setMotionApis(true);
    const sensor = new SensorTest();
    const container = fakeContainer();

    const run = sensor.run(fakeClient(), container);
    await tick();
    container.nodes.get('btn-request-sensors').press();
    await tick();
    assert.equal(windowListenerCount('devicemotion'), 1);

    const before = windowRemovalCount();
    container.nodes.get('sensor-manual-yes').press();
    await run;

    assert.equal(windowRemovalCount(), before + 2, 'both the motion and the orientation listener are off');
    assert.equal(windowListenerCount('devicemotion'), 0);
    setMotionApis(false);
});

test('motion: the step asks for more than the single-measurement default', () => {
    // A prompt, four seconds of listening, and a person physically turning a
    // phone does not fit in the 90 s the runner allows for one measurement.
    assert.ok(new SensorTest().getFailsafeMs() > 90000);
});

// ------------------------------------------------------------------ camera

const { CameraTest } = await import('../modules/CameraTest.js');

/**
 * A camera stream the fake video element can be pointed at.
 *
 * Every one of them carries the settings a browser reads back off a track,
 * because that is where a step finds out which camera it is holding and which
 * device to ask for next. A stream without them could only ever be one of the
 * two cameras that can be asked for by facing.
 */
function fakeStream({ torch = false, id = 'back-camera', facing = 'environment' } = {}) {
    const track = {
        stopped: false,
        stop() { this.stopped = true; },
        applyConstraints: async () => { if (!torch) throw { name: 'NotSupportedError' }; },
        getSettings: () => ({ deviceId: id, facingMode: facing })
    };
    return { getTracks: () => [track], getVideoTracks: () => [track], track };
}

/**
 * The lens list a browser hands out, which is only complete once the camera has
 * been granted once. Has to be called after useGetUserMedia, which replaces the
 * whole of mediaDevices the way a page load would.
 */
function useLensList(devices) {
    global.navigator.mediaDevices.enumerateDevices = async () => devices;
}

/**
 * A step on a short timer, so a test does not sit out three seconds a lens.
 *
 * The value actually shipped has a test of its own; what matters in the tests
 * below is that a photo is taken because the time ran out and not because
 * somebody pressed something, and forty milliseconds proves that as well as
 * three seconds does.
 */
function fastCamera() {
    const camera = new CameraTest();
    camera.photoDelayMs = 40;
    camera.shotHoldMs = 0;
    camera.frameWaitMs = 0;
    camera.lensSettleMs = 0;
    return camera;
}

test('camera: a refused prompt shows the reason and keeps the step open', async () => {
    useGetUserMedia([DENIED]);
    const camera = fastCamera();
    const container = fakeContainer();

    camera.run(fakeClient(), container);
    await tick();

    // The step cannot find a lens without the camera being allowed, so it is
    // still open on a question rather than over. The operator is asked, and the
    // countdown that would have photographed a phone nobody can see never began.
    assert.equal(camera.status, 'running');
    assert.equal(container.nodes.get('camera-error-area').hidden, false);
    assert.match(container.nodes.get('camera-error-msg').textContent, /geweigerd/);
    assert.ok(container.nodes.get('btn-camera-retry'));
    assert.ok(container.nodes.get('btn-camera-reject'));
    assert.equal(container.nodes.get('cam-timer').hidden, true);
    assert.equal(container.nodes.get('photo-review').hidden, true);

    abandon(camera);
});

test('camera: retry asks again, and a camera that works after that still passes', async () => {
    const calls = useGetUserMedia([DENIED, fakeStream(), fakeStream(), fakeStream()]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick();
    assert.equal(calls.length, 1);
    assert.equal(container.nodes.get('camera-error-area').hidden, false);

    // The operator goes away, grants the camera, comes back.
    container.nodes.get('btn-camera-retry').press();
    await tick(250);

    // The question is off the screen, and everything since has been
    // photographed without a hand on a shutter: the step did not go back to
    // waiting for somebody to take the photos one at a time.
    assert.equal(container.nodes.get('camera-error-area').hidden, true);
    assert.ok(container.nodes.get('photo-0'));
    assert.ok(container.nodes.get('photo-1'));

    container.nodes.get('btn-use-photo-0').press();
    container.nodes.get('btn-use-photo-1').press();
    await run;

    assert.equal(camera.status, 'passed');
    assert.equal(calls.length, 4);
    assert.match(camera.details.rearCamera.note, /goedgekeurd/);
    assert.match(camera.details.frontCamera.note, /goedgekeurd/);
});

test('camera: the timer photographs every lens, and only then is anything judged', async () => {
    const calls = useGetUserMedia([fakeStream(), fakeStream(), fakeStream()]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick();

    // The live view is up and the round timer is running over it. Nothing has
    // been pressed: the photo is taken when the timer runs out, which is what
    // makes the photographing part the step's and not the operator's.
    assert.equal(container.nodes.get('live-video').hidden, false);
    assert.equal(container.nodes.get('cam-timer').hidden, false);
    assert.equal(container.nodes.get('photo-review').hidden, true);
    assert.equal(calls.length, 2);

    await tick(60);

    // The first lens has been photographed and the second is being counted
    // down, still with nothing asked of anybody.
    assert.equal(calls.length, 3);
    assert.equal(container.nodes.get('photo-review').hidden, true);

    await tick(60);

    // Now every lens has been photographed and the list comes up with all of
    // them at once, so the verdicts are made side by side rather than one at a
    // time with the next camera already running.
    assert.equal(container.nodes.get('photo-review').hidden, false);
    assert.equal(container.nodes.get('video-container').hidden, true);
    assert.ok(container.nodes.get('photo-0'));
    assert.ok(container.nodes.get('photo-1'));
    assert.notEqual(container.nodes.get('photo-0').src, container.nodes.get('photo-1').src);
    assert.match(container.nodes.get('cam-step-title').textContent, /Beoordeel de foto/);
    assert.match(container.nodes.get('cam-instructions').textContent, /elke foto/);
    assert.equal(camera.status, 'running');

    // Two buttons per photo, and the answer to one is not the answer to the other.
    container.nodes.get('btn-use-photo-0').press();
    assert.equal(camera.status, 'running');
    assert.equal(container.nodes.get('btn-use-photo-1').disabled, false);

    container.nodes.get('btn-use-photo-1').press();
    await run;

    assert.equal(camera.status, 'passed');
    assert.equal(calls.length, 3);
    assert.match(camera.details.rearCamera.note, /goedgekeurd/);
    assert.match(camera.details.frontCamera.note, /goedgekeurd/);
    assert.equal(camera.backWorking, true);
    assert.equal(camera.frontWorking, true);
});

test('camera: a lens that will not open does not cost the others their photos', async () => {
    useGetUserMedia([fakeStream(), fakeStream(), BUSY]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick(250);

    // The front camera is held by something else. It is on the list with what
    // the browser said where its photo should be, and the lens before it still
    // has its photo, so a broken lens cannot read on the report as a phone with
    // no camera at all.
    assert.ok(container.nodes.get('photo-0'));
    assert.ok(!container.nodes.get('photo-1'));
    assert.match(container.nodes.get('photo-missing-1').textContent, /busy/);
    assert.equal(container.nodes.get('btn-use-photo-1'), undefined);
    assert.equal(container.nodes.get('btn-retake-1').disabled, false);
    assert.equal(camera.status, 'running');

    // And it can be taken again from there, which is the whole choice there is
    // for a camera that would not open the first time.
    container.nodes.get('btn-use-photo-0').press();
    await run;

    // The report names the lens that would not open, so "the front camera is
    // broken" cannot be read as "the camera is broken".
    assert.equal(camera.status, 'failed');
    assert.match(camera.notes, /voor: busy/);
    assert.doesNotMatch(camera.notes, /achter/, 'a camera that worked is not a fault');

    // And the accepted verdict is still on the record, so the desktop can show a
    // working rear camera next to a broken front one.
    assert.equal(camera.details.rearCamera.working, true);
    assert.equal(camera.backWorking, true);
    assert.equal(camera.frontWorking, false);
});

test('camera: a photo the operator turns down carries the reason they gave', async () => {
    useGetUserMedia([fakeStream(), fakeStream(), fakeStream()]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick(250);

    // The reason is asked per photo, under the photo it is about, and asking
    // about one says nothing about any of the others.
    container.nodes.get('btn-camera-defect-1').press();
    assert.equal(container.nodes.get('reject-controls-1').hidden, false);
    assert.equal(container.nodes.get('reject-controls-0').hidden, true);

    container.nodes.get('btn-reject-blurry-1').press();

    // The answer is on the record and the row closes behind it, so a second tap
    // cannot change a verdict that has already been written.
    assert.match(container.nodes.get('photo-verdict-1').textContent, /Onscherpe foto/);
    assert.equal(container.nodes.get('btn-use-photo-1').disabled, true);
    assert.equal(container.nodes.get('btn-camera-defect-1').disabled, true);
    assert.equal(container.nodes.get('btn-retake-1').disabled, true);
    assert.equal(container.nodes.get('reject-controls-1').hidden, true);
    assert.equal(camera.status, 'running');

    container.nodes.get('btn-use-photo-0').press();
    await run;

    assert.equal(camera.status, 'failed');
    assert.match(camera.notes, /voor: Onscherpe foto/);
    assert.doesNotMatch(camera.notes, /achter: /);

    // Both rows carry an answer, and each is marked with the answer rather than
    // with the fact that one was given: a green bar on the photo that was turned
    // down reads as "good" from across the desk.
    assert.equal(container.nodes.get('photo-row-1').classes.has('photo-row-rejected'), true);
    assert.equal(container.nodes.get('photo-row-1').classes.has('photo-row-approved'), false);
    assert.equal(container.nodes.get('photo-row-0').classes.has('photo-row-approved'), true);
    assert.equal(container.nodes.get('photo-row-0').classes.has('photo-row-rejected'), false);
});

test('camera: a phone with no camera API is a gap, and still a question', async () => {
    delete global.navigator.mediaDevices;
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick();

    assert.equal(camera.status, 'running');
    assert.match(container.nodes.get('camera-error-msg').textContent, /geen achtercamera/);

    // The operator calls it broken rather than trying again. There is no list to
    // read without a grant, so the step falls back to the two cameras it can
    // name without one, and both come back with what the browser said. It does
    // not decide for the operator that the phone has no camera.
    container.nodes.get('btn-camera-reject').press();
    await tick(60);
    await run;

    assert.equal(camera.status, 'failed');
    assert.equal(camera.details.rearCamera.note.includes('missing'), true);
    assert.equal(camera.details.frontCamera.note.includes('missing'), true);
    assert.match(reportedGaps().join(' '), /missing/);
});

test('camera: a lens that never gives a frame is not photographed', async () => {
    useGetUserMedia([fakeStream(), fakeStream(), fakeStream()]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick();

    // The video element has not produced a frame when the timer runs out, so
    // the first lens gets no photo: a black rectangle with a technician's
    // approval on it is exactly what this guard is for. On the real phone the
    // frame arrives at 480x640, so the guard is about the first moments after
    // opening, not about a camera that cannot do it.
    container.nodes.get('live-video').videoWidth = 0;
    await tick(60);
    container.nodes.get('live-video').videoWidth = 640;
    await tick(60);

    assert.ok(!container.nodes.get('photo-0'));
    assert.match(container.nodes.get('photo-missing-0').textContent, /geen beeld/);
    assert.ok(container.nodes.get('photo-1'));

    container.nodes.get('btn-use-photo-1').press();
    await run;

    assert.equal(camera.status, 'failed');
    assert.match(camera.notes, /achter: Deze lens gaf geen beeld/);
    assert.doesNotMatch(camera.notes, /voor: /, 'a lens that was photographed is not a fault');
});

test('camera: a camera with no torch is noted and judged without one', async () => {
    useGetUserMedia([fakeStream({ torch: false }), fakeStream(), fakeStream()]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick();

    // Most cameras have no torch. The overlay says so over the lens that was
    // asked, which is the rear one, and the old step used to put the generic
    // "inspect the photo carefully" text in that place, which read as if
    // something had gone wrong.
    assert.equal(container.nodes.get('torch-overlay').hidden, false);
    assert.match(container.nodes.get('torch-overlay').textContent, /geen flits/);
    assert.equal(camera.torchActive, false);

    await tick(250);
    container.nodes.get('btn-use-photo-0').press();
    container.nodes.get('btn-use-photo-1').press();
    await run;

    assert.equal(camera.status, 'passed');
    assert.equal(camera.details.torchAvailable, false);
});

test('camera: every lens the browser names is photographed, not just two', async () => {
    const calls = useGetUserMedia([
        fakeStream({ id: 'back-main' }),
        fakeStream({ id: 'back-main' }),
        fakeStream({ id: 'back-ultra' }),
        fakeStream({ id: 'front', facing: 'user' })
    ]);
    useLensList([
        { kind: 'audioinput', deviceId: 'mic-1', label: 'Internal microphone' },
        { kind: 'videoinput', deviceId: 'back-main', label: 'Back Camera' },
        { kind: 'videoinput', deviceId: 'back-ultra', label: 'Back Ultra Wide Camera' },
        { kind: 'videoinput', deviceId: 'front', label: 'Front Camera' },
        // A device the browser will not name yet. It is not a lens, and opening
        // it would be opening a camera nobody has described.
        { kind: 'videoinput', deviceId: '', label: 'Unnamed' }
    ]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick(250);

    // The phone reports three cameras and three cameras are on the list. The
    // ultrawide is not the rear camera and does not get filed under its name,
    // which is what used to happen to every lens that was not one of the two
    // the step knew the words for.
    assert.equal(container.nodes.get('photo-review').hidden, false);
    assert.ok(container.nodes.get('photo-0'));
    assert.ok(container.nodes.get('photo-1'));
    assert.ok(container.nodes.get('photo-2'));
    assert.match(container.nodes.get('photo-row-1').textContent, /Back Ultra Wide Camera/);

    // Each one is opened by its own device id. The first camera is opened by
    // facing, because facing is the only thing that can be asked for before the
    // browser will name anything at all.
    assert.deepEqual(calls[0], { video: { facingMode: 'environment' } });
    assert.deepEqual(calls[1], { video: { deviceId: { exact: 'back-main' } } });
    assert.deepEqual(calls[2], { video: { deviceId: { exact: 'back-ultra' } } });
    assert.deepEqual(calls[3], { video: { deviceId: { exact: 'front' } } });

    container.nodes.get('btn-use-photo-0').press();
    container.nodes.get('btn-use-photo-1').press();
    container.nodes.get('btn-use-photo-2').press();
    await run;

    assert.equal(camera.status, 'passed');
    assert.equal(camera.details.rearCamera.working, true);
    assert.equal(camera.details.camera2.working, true);
    assert.equal(camera.details.frontCamera.working, true);
    assert.equal(camera.backWorking, true);
    assert.equal(camera.frontWorking, true);
});

test('camera: a photo can be taken again from the list', async () => {
    const calls = useGetUserMedia([fakeStream(), fakeStream(), fakeStream(), fakeStream()]);
    const camera = fastCamera();
    const container = fakeContainer();

    const run = camera.run(fakeClient(), container);
    await tick(250);
    const first = container.nodes.get('photo-0').src;

    // The camera comes back up under the same card rather than somewhere else,
    // and the list is out of the way while it is being used.
    container.nodes.get('btn-retake-0').press();
    await tick();
    assert.equal(container.nodes.get('video-container').hidden, false);
    assert.equal(container.nodes.get('photo-review').hidden, true);

    await tick(120);

    // A different photo on the same row, from a lens opened again, and nothing
    // else on the list has been touched by it.
    assert.equal(container.nodes.get('photo-review').hidden, false);
    assert.notEqual(container.nodes.get('photo-0').src, first);
    assert.ok(container.nodes.get('photo-1'));
    assert.equal(calls.length, 4);

    container.nodes.get('btn-use-photo-0').press();
    container.nodes.get('btn-use-photo-1').press();
    await run;

    assert.equal(camera.status, 'passed');
});

test('camera: the timer a technician waits for is three seconds', () => {
    // Every other test in here runs the step on a short timer so it does not sit
    // out three seconds a lens, which means the value actually shipped has to be
    // pinned somewhere or it could be changed to anything without a failure.
    assert.equal(CameraTest.PHOTO_DELAY_MS, 3000);
    assert.equal(new CameraTest().photoDelayMs, CameraTest.PHOTO_DELAY_MS);
});

test('camera: a lens gets time to produce its first frame, and the release is waited out', () => {
    // The countdown starts on a frame rather than on the stream handle, and the
    // previous lens is let go before the next one opens: both are what keeps a
    // just-opened camera from being photographed black.
    const camera = new CameraTest();
    assert.equal(camera.frameWaitMs, CameraTest.FRAME_WAIT_MS);
    assert.equal(camera.lensSettleMs, CameraTest.LENS_SETTLE_MS);
    assert.ok(CameraTest.FRAME_WAIT_MS >= 3000);
    assert.ok(CameraTest.LENS_SETTLE_MS > 0);
});

test('camera: several lenses need more than the single-measurement failsafe', () => {
    assert.ok(new CameraTest().getFailsafeMs() > 90000);
});

test('camera: the stream is released when the step is abandoned', async () => {
    const stream = fakeStream();
    const calls = useGetUserMedia([stream, stream]);
    const camera = fastCamera();
    const container = fakeContainer();

    camera.run(fakeClient(), container);
    await tick();

    // The list has been read with the rear camera open, and the rear camera is
    // open again for its own photograph.
    assert.equal(camera._stream, stream);
    assert.equal(calls.length, 2);

    abandon(camera);

    assert.equal(stream.track.stopped, true);
    assert.equal(camera._stream, null);

    // The countdown running over it goes with it, so a step that has been left
    // does not go on to open the next lens on a phone nobody is testing. The
    // camera light going on again after the operator has walked away would be
    // the most visible thing this app could do wrong.
    await tick(200);
    assert.equal(calls.length, 2);
    assert.equal(camera._stream, null);
});

// -------------------------------------------------------------- microphone

const { MicrophoneTest } = await import('../modules/MicrophoneTest.js');

/** A media stream whose track can be watched being stopped. */
function fakeAudioStream() {
    const stream = {
        stopped: false,
        getTracks: () => [{
            stop() { stream.stopped = true; }
        }]
    };
    return stream;
}

/**
 * An AudioContext whose analyser reports whatever level the test asks for.
 *
 * The level is read back out of the byte spectrum the step fills, so the number
 * in the notes is a number the step measured rather than one the test handed it.
 */
function useFakeMeter() {
    const frames = [];
    const state = { level: 0, closed: false };

    global.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
    global.window.AudioContext = function FakeAudioContext() {
        this.createAnalyser = () => ({
            fftSize: 0,
            frequencyBinCount: 32,
            getByteFrequencyData: (arr) => arr.fill(state.level)
        });
        this.createMediaStreamSource = () => ({ connect: () => {} });
        this.close = async () => { state.closed = true; };
    };

    return {
        state,
        /** Runs the next pending meter poll, as a new animation frame would. */
        poll() { const fn = frames.shift(); if (fn) fn(); },
        setLevel(v) { state.level = v; }
    };
}

/**
 * A MediaRecorder that writes a clip of its own the moment the step stops it.
 *
 * The page hands the clip to <audio> through an object URL, which node has no
 * blobs of its own to make, so the URL is stood in for as well.
 */
function useFakeRecorder() {
    global.MediaRecorder = class FakeMediaRecorder {
        constructor(stream) {
            this.stream = stream;
            this.mimeType = 'audio/webm';
            this.state = 'inactive';
            this.ondataavailable = null;
            this.onstop = null;
        }

        start() { this.state = 'recording'; }

        stop() {
            if (this.state === 'inactive') return;
            this.state = 'inactive';
            if (this.ondataavailable) {
                this.ondataavailable({ data: new Blob([new Uint8Array(4096)], { type: this.mimeType }) });
            }
            if (this.onstop) this.onstop();
        }
    };

    if (typeof URL.createObjectURL !== 'function') {
        URL.createObjectURL = () => 'blob:fake-clip';
    }
}

test('microphone: a refusal is a refusal, not a quiet swap for the recorder', async () => {
    useGetUserMedia([DENIED, DENIED]);
    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();

    // The old step fell out of the try and opened the voice recorder. A phone
    // whose microphone was dead could be passed by pressing "yes, clear" on a
    // recording that never had any sound in it.
    assert.equal(mic.status, 'running');
    assert.match(container.innerHTML, /geweigerd/);
    assert.ok(container.nodes.get('mic-retry'), 'a refusal can be undone, so retry is offered');
    assert.ok(container.nodes.get('mic-reject'));

    // The recorder is not offered for a refusal, because it is not the same test.
    // It is kept for a browser with no microphone API at all.
    assert.equal(container.querySelector('#mic-recorder'), null);
    assert.equal(container.querySelector('#audio-file-input'), null);

    // And the refusal was not reported as a missing API, so the grade is not
    // capped for something the operator had simply not answered yet.
    assert.equal(reportedGaps().includes('missing'), false);
    assert.match(reportedGaps().join(' '), /denied/);

    container.nodes.get('mic-reject').press();
    await run;
    assert.equal(mic.status, 'failed');
});

test('microphone: a silent recording fails the step without asking a question', async () => {
    const stream = fakeAudioStream();
    useGetUserMedia([DENIED, stream]);
    useFakeMeter();
    useFakeRecorder();

    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();
    container.nodes.get('mic-retry').press();
    await tick(5);

    assert.equal(mic.status, 'running', 'notes: ' + mic.notes);
    assert.ok(container.querySelector('#mic-vu-bar'), 'the trouble card has been replaced by the recording screen');

    // Nobody speaks for the whole three seconds. The level never moves, and the
    // step says so on that measurement rather than on an answer nobody gave: the
    // question is the one a dead microphone would be passed on. The phone in
    // front of me measured 0 with nobody speaking into it, which is the same
    // answer.
    await tick(MicrophoneTest.RECORD_MS + 200);
    await run;

    assert.equal(mic.status, 'failed');
    assert.match(mic.notes, /Geen audiosignaal/);
    assert.equal(mic.details.checkMethod, 'record-and-replay');
    assert.equal(container.querySelector('#mic-yes'), null, 'silence is never offered the question');
    assert.equal(stream.stopped, true, 'the input is released when the step ends');

    delete global.requestAnimationFrame;
    delete global.MediaRecorder;
});

test('microphone: the clip comes back and the operator hears it', async () => {
    const stream = fakeAudioStream();
    useGetUserMedia([stream]);
    const meter = useFakeMeter();
    useFakeRecorder();

    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();

    // The operator talks, and the level the analyser reports is a real number off
    // the byte spectrum: 90 of 255 is 70% of the bar.
    meter.setLevel(90);
    meter.poll();
    assert.match(container.nodes.get('live-mic-status').textContent, /70%/);

    // Three seconds of it, and then the clip is handed back rather than the bar
    // being taken for an answer.
    await tick(MicrophoneTest.RECORD_MS + 200);

    assert.ok(container.querySelector('#mic-playback'), 'the clip is on screen and playable');
    assert.match(container.nodes.get('mic-playback').src, /^blob:/);
    assert.match(container.nodes.get('mic-yes').textContent, /helder geluid/);

    container.nodes.get('mic-yes').press();
    await run;

    assert.equal(mic.status, 'passed');
    assert.match(mic.notes, /piek 70%/);
    assert.equal(mic.details.peakLevel, 70);
    assert.equal(mic.details.checkMethod, 'record-and-replay');
    assert.equal(stream.stopped, true, 'the input is released when the step ends');

    delete global.requestAnimationFrame;
    delete global.MediaRecorder;
});

test('microphone: more than one input asks which microphone to test', async () => {
    const stream = fakeAudioStream();
    const calls = useGetUserMedia([stream]);
    global.navigator.mediaDevices.enumerateDevices = async () => [
        { kind: 'audioinput', deviceId: 'mic-bottom', label: 'Bottom microphone' },
        { kind: 'audioinput', deviceId: 'mic-top', label: 'Top microphone' },
        { kind: 'videoinput', deviceId: 'camera', label: 'Camera' }
    ];
    const meter = useFakeMeter();
    useFakeRecorder();

    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();

    // Several microphones is a choice the operator makes, and this screen used
    // to crash: it reported its progress through a client that was never passed
    // to it, so every phone with more than one input failed the step with a
    // ReferenceError before a note was even recorded.
    assert.ok(container.nodes.get('mic-select'), 'the selection screen is up');
    assert.equal(calls.length, 0, 'nothing is recorded before a microphone is chosen');

    const select = container.nodes.get('mic-select');
    select.value = 'mic-top';
    select.fire('change');
    container.nodes.get('mic-select-confirm').press();
    await tick();

    // The chosen input is the one the browser is asked for, and the recording
    // proceeds from there.
    assert.deepEqual(calls[0], { audio: { deviceId: { exact: 'mic-top' } } });
    assert.ok(container.querySelector('#mic-vu-bar'));

    meter.setLevel(90);
    meter.poll();
    await tick(MicrophoneTest.RECORD_MS + 200);
    container.nodes.get('mic-yes').press();
    await run;

    assert.equal(mic.status, 'passed');

    delete global.requestAnimationFrame;
    delete global.MediaRecorder;
});

test('microphone: a clip the operator cannot hear fails the step', async () => {
    const stream = fakeAudioStream();
    useGetUserMedia([stream]);
    const meter = useFakeMeter();
    useFakeRecorder();

    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();

    // The level moved, so the question is asked. What came out of the speaker is
    // the operator's to judge, and their no is a failure of this step rather than
    // a note for somebody else to read later.
    meter.setLevel(90);
    meter.poll();
    await tick(MicrophoneTest.RECORD_MS + 200);

    container.nodes.get('mic-no').press();
    await run;

    assert.equal(mic.status, 'failed');
    assert.match(mic.notes, /niet hoorbaar/);
    assert.equal(mic.details.checkMethod, 'record-and-replay');
    assert.equal(stream.stopped, true, 'the input is released when the step ends');

    delete global.requestAnimationFrame;
    delete global.MediaRecorder;
});

test('microphone: a browser that cannot record still gets measured on the meter', async () => {
    // There is no clip to hand back, but the level is still a measurement of this
    // microphone, so the step takes it rather than failing a phone for a browser.
    delete global.MediaRecorder;
    const stream = fakeAudioStream();
    useGetUserMedia([stream]);
    const meter = useFakeMeter();

    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();

    assert.ok(container.querySelector('#mic-vu-bar'));

    meter.setLevel(90);
    meter.poll();

    await tick(1200);
    await run;

    assert.equal(mic.status, 'passed');
    assert.match(mic.notes, /piek 70%/);
    assert.equal(mic.details.checkMethod, 'live-meter');

    delete global.requestAnimationFrame;
});

test('microphone: a browser with no microphone API falls back to the recorder on purpose', async () => {
    delete global.navigator.mediaDevices;
    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();

    // Here there is no measurement to make, so the recording is the only check
    // available, and the report says which check produced the verdict.
    assert.ok(container.querySelector('#audio-file-input'));
    assert.ok(container.querySelector('#mic-yes'));
    assert.match(container.innerHTML, /niet rechtstreeks uitlezen/);

    container.nodes.get('mic-yes').press();
    await run;

    assert.equal(mic.status, 'passed');
    assert.equal(mic.details.checkMethod, 'recorder');
});

test('microphone: a recording the operator cannot hear is a failure, with the method on it', async () => {
    delete global.navigator.mediaDevices;
    const mic = new MicrophoneTest();
    const container = fakeContainer();

    const run = mic.run(fakeClient(), container);
    await tick();
    container.nodes.get('mic-no').press();
    await run;

    assert.equal(mic.status, 'failed');
    assert.equal(mic.details.checkMethod, 'recorder');
    assert.match(mic.notes, /niet hoorbaar/);
});

test('microphone: dispose releases the input and the audio context', () => {
    // A skipped microphone step used to leave the input open, so the phone kept
    // showing its microphone indicator for the rest of the run.
    const mic = new MicrophoneTest();
    const stream = fakeAudioStream();
    let closed = false;

    mic._stream = stream;
    mic._audioCtx = { close: async () => { closed = true; } };

    mic.dispose();

    assert.equal(stream.stopped, true);
    assert.equal(closed, true);
    assert.equal(mic._stream, null);
    assert.equal(mic._audioCtx, null);
});

test('microphone: dispose on an already closed context does not throw', () => {
    const mic = new MicrophoneTest();
    mic._audioCtx = { close: () => { throw new Error('already closed'); } };

    assert.doesNotThrow(() => mic.dispose());
    assert.equal(mic._audioCtx, null);
});

test('microphone: the live meter and the recorder both need more than the default', () => {
    assert.ok(new MicrophoneTest().getFailsafeMs() > 90000);
});
