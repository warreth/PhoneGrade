// Detects browser APIs the test suite depends on and reports the ones that are
// absent. Split out of app.js so the scanner can be imported and exercised on
// its own instead of only through a loaded page.
//
// One class with two entry points: scan() collects, reportMissing() sends, and
// scanAndReport() does both. The DOMContentLoaded bootstrap is the only caller,
// so a single page load reports once instead of twice.

import { reportCapabilityGap } from './CapabilityGap.js';

export class CapabilityScanner {
    /**
     * @param {object|null} apiClient object exposing baseUrl and sessionId
     * @param {Function} fetchImpl injected so tests can observe the request
     */
    constructor(apiClient = null, fetchImpl = null) {
        this.apiClient = apiClient;
        this.fetchImpl = fetchImpl;
        this.requiredApis = {
            'DeviceMotionEvent': () => typeof DeviceMotionEvent !== 'undefined',
            'DeviceOrientationEvent': () => typeof DeviceOrientationEvent !== 'undefined',
            'navigator.geolocation': () => 'geolocation' in navigator,
            'navigator.mediaDevices.getUserMedia': () => !!(navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function'),
            'navigator.vibrate': () => typeof navigator.vibrate === 'function',
            'navigator.wakeLock': () => 'wakeLock' in navigator
        };
        this.missingApis = [];
    }

    scan() {
        const results = {};
        this.missingApis = [];

        for (const [apiName, checkFn] of Object.entries(this.requiredApis)) {
            const present = !!checkFn();
            results[apiName] = present;
            if (!present) this.missingApis.push(apiName);
        }

        return {
            allPresent: this.missingApis.length === 0,
            missing: [...this.missingApis],
            results
        };
    }

    /**
     * Sends one request per missing API. The report carries a reason so the
     * desktop can tell a capability gap from a refused permission prompt; only
     * a gap is allowed to cost the device a grade.
     */
    async reportMissing(apiClient = this.apiClient, fetchImpl = this.fetchImpl) {
        if (!apiClient || this.missingApis.length === 0) return;

        for (const missingApi of this.missingApis) {
            // The scan only fires when the property is absent from the browser,
            // so this is always a real capability gap.
            await reportCapabilityGap(apiClient, missingApi, 'missing', { fetchImpl });
        }
    }

    async scanAndReport() {
        this.scan();
        await this.reportMissing(this.apiClient);
    }
}
