// Detects browser APIs the test suite depends on and reports the ones that are
// absent. Split out of app.js so the scanner can be imported and exercised on
// its own instead of only through a loaded page.
//
// One class with two entry points: scan() collects, reportMissing() sends, and
// scanAndReport() does both. The DOMContentLoaded bootstrap is the only caller,
// so a single page load reports once instead of twice.

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

        const doFetch = fetchImpl || ((...args) => fetch(...args));
        const ua = navigator.userAgent;
        let os = 'Unknown';
        let osVersion = '';

        if (/iPhone|iPad|iPod/.test(ua)) {
            os = 'iOS';
            osVersion = ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || '';
        } else if (/Android/.test(ua)) {
            os = 'Android';
            osVersion = ua.match(/Android (\d+\.\d+)/)?.[1] || '';
        } else if (/Macintosh/.test(ua)) {
            os = 'macOS';
            osVersion = ua.match(/Mac OS X ([\d_]+)/)?.[1]?.replace(/_/g, '.') || '';
        } else if (/Windows/.test(ua)) {
            os = 'Windows';
        }

        for (const missingApi of this.missingApis) {
            try {
                await doFetch(`${apiClient.baseUrl}/api/pwa/log-warning`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        sessionId: apiClient.sessionId,
                        missingApi,
                        userAgent: ua,
                        osVersion: `${os} ${osVersion}`.trim(),
                        // The scan only fires when the property is absent from the
                        // browser, so this is always a real capability gap.
                        reason: 'missing'
                    })
                });
            } catch (e) {
                console.warn('Failed to report missing API:', missingApi, e);
            }
        }
    }

    async scanAndReport() {
        this.scan();
        await this.reportMissing(this.apiClient);
    }
}
