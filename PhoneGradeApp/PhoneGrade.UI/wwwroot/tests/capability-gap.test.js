import test from 'node:test';
import assert from 'node:assert/strict';

import { describePlatform, platformSummary, reportCapabilityGap } from '../modules/CapabilityGap.js';

test('the platform and its version come out of the user agent', () => {
    assert.deepEqual(
        describePlatform('Mozilla/5.0 (Linux; Android 14.0; Pixel 8) AppleWebKit/537.36'),
        { os: 'Android', version: '14.0' });
    assert.deepEqual(
        describePlatform('Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X)'),
        { os: 'iOS', version: '17.5' });
    assert.deepEqual(
        describePlatform('Mozilla/5.0 (Macintosh; Intel Mac OS X 13_6)'),
        { os: 'macOS', version: '13.6' });
    assert.deepEqual(
        describePlatform('Mozilla/5.0 (Windows NT 10.0; Win64; x64)'),
        { os: 'Windows', version: '' });
    assert.deepEqual(describePlatform(''), { os: 'Unknown', version: '' });
    assert.deepEqual(describePlatform(null), { os: 'Unknown', version: '' });
});

test('the summary is the platform as one line', () => {
    assert.equal(platformSummary('Linux; Android 14.0; Pixel 8'), 'Android 14.0');
    assert.equal(platformSummary('Windows NT 10.0'), 'Windows');
    assert.equal(platformSummary(''), 'Unknown');
});

test('a gap is reported to the desktop with the API and the reason', async () => {
    const calls = [];
    const fetchImpl = async (url, options) => {
        calls.push({ url, options });
        return { ok: true };
    };

    const sent = await reportCapabilityGap(
        { baseUrl: 'http://localhost:5056', sessionId: 'session-1' },
        'navigator.geolocation',
        'missing',
        { fetchImpl });

    assert.equal(sent, true);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, 'http://localhost:5056/api/pwa/log-warning');
    assert.equal(calls[0].options.method, 'POST');

    const body = JSON.parse(calls[0].options.body);
    assert.equal(body.sessionId, 'session-1');
    assert.equal(body.missingApi, 'navigator.geolocation');
    assert.equal(body.reason, 'missing');
    assert.equal(typeof body.userAgent, 'string');
    assert.equal(typeof body.osVersion, 'string');
});

test('a denial keeps its reason, so it is not counted as a hardware gap', async () => {
    let body = null;
    await reportCapabilityGap(
        { baseUrl: 'http://localhost:5056', sessionId: 's' },
        'navigator.mediaDevices.getUserMedia',
        'denied',
        { fetchImpl: async (_url, options) => { body = JSON.parse(options.body); } });

    assert.equal(body.reason, 'denied');
});

test('no session means no request at all', async () => {
    let called = false;
    const fetchImpl = async () => { called = true; };

    assert.equal(await reportCapabilityGap({ baseUrl: 'x', sessionId: '' }, 'a', 'missing', { fetchImpl }), false);
    assert.equal(await reportCapabilityGap(null, 'a', 'missing', { fetchImpl }), false);
    assert.equal(called, false);
});

test('a failed report is swallowed rather than thrown', async () => {
    const sent = await reportCapabilityGap(
        { baseUrl: 'http://localhost:5056', sessionId: 's' },
        'navigator.vibrate',
        'missing',
        { fetchImpl: async () => { throw new Error('offline'); } });

    assert.equal(sent, false);
});