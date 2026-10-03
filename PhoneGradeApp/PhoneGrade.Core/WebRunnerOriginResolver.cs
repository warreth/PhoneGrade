using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core;

/// <summary>The address the phone is told to open, and whether it counts as secure.</summary>
public sealed record WebRunnerOrigin(string Address, bool IsSecure, ConnectionWarning? Warning);

/// <summary>
/// Decides which address goes into the QR code.
///
/// A browser hands out camera, microphone, motion, orientation, wake lock and
/// geolocation only on a secure origin, and the routes that can produce one
/// are not equally good, so they are tried in the order that costs the technician
/// least:
///
/// 1. adb reverse, which needs no internet, nothing on the phone, and gives
///    localhost, the one address every browser trusts. Android keeps this.
///    The switch that serves the phone over the USB cable only skips this
///    route when it is off; the public https route below still gets its turn.
/// 2. A public https address in front of the local server, which is the only route
///    an iPhone has. It needs internet on both ends and carries the results past
///    Cloudflare, so it is only opened for a phone that cannot use the cable.
/// 3. The plain network address, which works for everything except the restricted
///    APIs. It is the last resort, and it says so instead of failing quietly:
///    when no route was ever tried the window words that as the secure
///    connection being switched off.
///
/// Each route is asked on its own and its failure is recorded rather than thrown,
/// so one broken route cannot hide the answer from the routes behind it.
///
/// What comes back is fact rather than wording: which routes failed and what a
/// failing one said about itself. The window turns that into a sentence in its
/// own language, which is what keeps an English window from reading Dutch.
/// </summary>
public sealed class WebRunnerOriginResolver
{
    /// <summary>Opens the private route the cable already provides.</summary>
    public delegate Task<bool> UsbRoute(string sessionUdid, int port);

    /// <summary>Opens a public https address in front of the local server.</summary>
    public delegate Task<string?> InternetRoute(int port, Action<ConnectionNotice>? onStatus);

    private readonly UsbRoute _usb;
    private readonly InternetRoute _internet;

    public WebRunnerOriginResolver(UsbRoute usb, InternetRoute internet)
    {
        _usb = usb;
        _internet = internet;
    }

    /// <summary>True when the session has no phone behind it yet.</summary>
    public static bool IsPlaceholderSession(string? sessionUdid) =>
        string.IsNullOrWhiteSpace(sessionUdid)
        || sessionUdid.Equals("DEMO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The address for this session. <paramref name="onStatus"/> is called with
    /// whatever step the routes are on while the answer is still being worked out,
    /// which can take a while on a first run. The step reaches the caller as it is,
    /// because turning it into a sentence is the caller's language and not ours.
    /// </summary>
    public async Task<WebRunnerOrigin> ResolveAsync(
        string sessionUdid,
        int port,
        string lanAddress,
        string loopbackAddress,
        bool secureOriginEnabled,
        bool publicTunnelEnabled,
        Action<ConnectionNotice>? onStatus = null)
    {
        if (string.IsNullOrWhiteSpace(lanAddress)) lanAddress = loopbackAddress;

        var failed = new List<ConnectionRoute>(2);

        // The switch only governs the cable route: off, the phone is never served
        // through adb reverse, and the public route below still gets its turn.
        if (secureOriginEnabled && AdbReverseTunnel.SupportsReverse(sessionUdid))
        {
            onStatus?.Invoke(new ConnectionNotice(ConnectionStep.OpeningUsb));

            bool opened = false;
            try
            {
                opened = await _usb(sessionUdid, port);
            }
            catch (Exception ex)
            {
                // adb missing, the device not authorised, a build without reverse.
                SystemEventLogger.Warning(LogSource.UsbDetector, $"adb reverse could not be opened: {ex.Message}");
            }

            if (opened)
            {
                return new WebRunnerOrigin(loopbackAddress, true, null);
            }

            failed.Add(ConnectionRoute.Usb);
        }

        // A placeholder session is only ever opened by the technician's own browser
        // to look around, and a tunnel for it would publish the app for nothing.
        if (publicTunnelEnabled && !IsPlaceholderSession(sessionUdid))
        {
            onStatus?.Invoke(new ConnectionNotice(ConnectionStep.OpeningInternet));

            // The route narrates what it is doing and, when it cannot, why. Only the
            // second is worth carrying into the warning: the first is the step this
            // method has just reported, and nesting it inside the reason reads as if
            // opening the connection were what went wrong. The step is compared
            // rather than the sentence, so rewriting either one cannot break this.
            ConnectionNotice? reported = null;

            string? internet = null;
            try
            {
                internet = await _internet(port, notice =>
                {
                    if (notice.Step != ConnectionStep.OpeningInternet)
                        reported = notice;
                    onStatus?.Invoke(notice);
                });
            }
            catch (Exception ex)
            {
                SystemEventLogger.Warning(LogSource.UsbDetector, $"The internet tunnel could not be opened: {ex.Message}");
                reported = new ConnectionNotice(ConnectionStep.InternetStartFailed, ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(internet))
            {
                return new WebRunnerOrigin(internet.TrimEnd('/'), true, null);
            }

            failed.Add(ConnectionRoute.Internet);

            if (reported != null)
                return Insecure(lanAddress, failed, reported);
        }

        if (IsPlaceholderSession(sessionUdid))
        {
            return new WebRunnerOrigin(lanAddress, false, null);
        }

        return Insecure(lanAddress, failed, null);
    }

    /// <summary>
    /// The plain network address, with what stopped it from being a secure one.
    ///
    /// <paramref name="reason"/> is whatever the failing route said about itself,
    /// which is the only part an operator cannot work out from the list of routes
    /// that failed. It stays a step here: the window words it, because the window
    /// is the only thing that shows it.
    /// </summary>
    private static WebRunnerOrigin Insecure(
        string lanAddress, List<ConnectionRoute> failed, ConnectionNotice? reason)
        => new(lanAddress, false, new ConnectionWarning(failed.ToArray(), reason));
}
