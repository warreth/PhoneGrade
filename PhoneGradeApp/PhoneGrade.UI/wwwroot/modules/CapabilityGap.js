/**
 * One place where a capability gap becomes a report.
 *
 * The scanner and four steps each carried their own copy of the same request:
 * the same user-agent parsing, the same endpoint, the same JSON body. They had
 * drifted (two parsed Android first, one iOS first), and a change to the report
 * had to be made in five places.
 *
 * A caller says which API could not be used and why; everything else about the
 * report lives here. The reason matters to the desktop: 'missing' is a gap in
 * the device and caps the grade, while 'denied' is the operator's choice and
 * only gets a log line.
 */

/** The platform and its version, out of a user agent. */
export function describePlatform(userAgent) {
    const ua = String(userAgent || '');

    if (/iPhone|iPad|iPod/.test(ua)) {
        return { os: 'iOS', version: ua.match(/OS (\d+_\d+)/)?.[1]?.replace(/_/g, '.') || '' };
    }
    if (/Android/.test(ua)) {
        return { os: 'Android', version: ua.match(/Android (\d+\.\d+)/)?.[1] || '' };
    }
    if (/Macintosh/.test(ua)) {
        return { os: 'macOS', version: ua.match(/Mac OS X ([\d_]+)/)?.[1]?.replace(/_/g, '.') || '' };
    }
    if (/Windows/.test(ua)) {
        return { os: 'Windows', version: '' };
    }

    return { os: 'Unknown', version: '' };
}

/** The platform as one line for the log: "Android 14.0", "Windows", "Unknown". */
export function platformSummary(userAgent) {
    const { os, version } = describePlatform(userAgent);
    return `${os} ${version}`.trim();
}

/**
 * Sends one warning about an API the browser cannot give the suite.
 *
 * A failed report is swallowed: a step must not fail because the desk was
 * busy, and the scan runs again on the next page load.
 *
 * @param {object|null} client object exposing baseUrl and sessionId
 * @param {string} missingApi the API that could not be used
 * @param {string} reason 'missing', 'denied' or 'insecure-origin'
 * @param {object} [options] fetchImpl, for tests that observe the request
 * @returns {Promise<boolean>} whether the desktop acknowledged it
 */
export async function reportCapabilityGap(client, missingApi, reason, options = {}) {
    if (!client || !client.sessionId) return false;

    const doFetch = options.fetchImpl || ((...args) => fetch(...args));
    const ua = navigator.userAgent;

    try {
        await doFetch(`${client.baseUrl}/api/pwa/log-warning`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionId: client.sessionId,
                missingApi,
                userAgent: ua,
                osVersion: platformSummary(ua),
                reason
            })
        });
        return true;
    } catch (e) {
        console.warn(`Could not report ${missingApi}:`, e);
        return false;
    }
}