/**
 * Works out which address the page should call the desktop on.
 *
 * The page is opened over three kinds of origin and the port is what tells them
 * apart: a LAN address carries one, http://localhost through the adb reverse
 * tunnel carries one, and a quick tunnel address deliberately carries none
 * because Cloudflare only ever answers on 443.
 *
 * Origin already holds the right host plus the port when it is not the default
 * one, so it is taken as it is. Appending a port here is what sent every request
 * from a tunnel address to a port Cloudflare never serves, which made the
 * handshake fail while the page itself loaded fine.
 */
export function baseUrlFrom(loc) {
    if (loc && typeof loc.origin === 'string' && loc.origin && loc.origin !== 'null') {
        return loc.origin;
    }

    // Only reached where the browser does not report an origin, which is an
    // opaque one such as a sandboxed frame. The port is kept because there is no
    // origin to read it from.
    const protocol = loc && loc.protocol ? loc.protocol : 'http:';
    const hostname = loc && loc.hostname ? loc.hostname : '127.0.0.1';
    const port = loc && loc.port ? `:${loc.port}` : '';
    return `${protocol}//${hostname}${port}`;
}
