import test from 'node:test';
import assert from 'node:assert/strict';
import { baseUrlFrom } from '../modules/serverUrl.js';

// The three address shapes QrCodeService hands out. A real URL object is used
// rather than a plain literal so the port is what a browser would actually
// report for that address, which is the whole point of the function.
test('a tunnel address keeps no port', () => {
    const page = new URL('https://quiet-marble-otter.trycloudflare.com/?sessionId=38091FDJG00EMF');

    const base = baseUrlFrom(page);

    assert.equal(base, 'https://quiet-marble-otter.trycloudflare.com');
    assert.ok(!base.includes(':5056'), 'the tunnel is only served on 443, so a port must not appear');
    assert.equal(`${base}/api/pwa/handshake`, 'https://quiet-marble-otter.trycloudflare.com/api/pwa/handshake');
});

test('a LAN address keeps its port', () => {
    const page = new URL('http://192.168.0.216:5056/?sessionId=38091FDJG00EMF');

    assert.equal(baseUrlFrom(page), 'http://192.168.0.216:5056');
});

test('the adb reverse address keeps its port', () => {
    const page = new URL('http://localhost:5055/?sessionId=38091FDJG00EMF');

    assert.equal(baseUrlFrom(page), 'http://localhost:5055');
});

test('the default port of the scheme is not restated', () => {
    const page = new URL('http://192.168.0.216:80/?sessionId=X');

    assert.equal(baseUrlFrom(page), 'http://192.168.0.216');
});

test('an address without an origin is rebuilt from its parts', () => {
    const opaque = { protocol: 'https:', hostname: 'quiet-marble-otter.trycloudflare.com', port: '' };

    assert.equal(baseUrlFrom(opaque), 'https://quiet-marble-otter.trycloudflare.com');
    assert.equal(baseUrlFrom({ protocol: 'http:', hostname: '192.168.0.216', port: '5056' }), 'http://192.168.0.216:5056');
});

test('nothing at all still yields a callable address', () => {
    assert.equal(baseUrlFrom(undefined), 'http://127.0.0.1');
    assert.equal(baseUrlFrom({}), 'http://127.0.0.1');
    assert.equal(baseUrlFrom({ origin: 'null' }), 'http://127.0.0.1');
});
