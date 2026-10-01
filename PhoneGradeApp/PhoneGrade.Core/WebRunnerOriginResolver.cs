using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core;

/// <summary>The address the phone is told to open, and whether it counts as secure.</summary>
public sealed record WebRunnerOrigin(string Address, bool IsSecure, string? Warning);

/// <summary>
/// Decides which address goes into the QR code.
///
/// A browser hands out camera, microphone, motion, orientation, wake lock and
/// geolocation only on a secure origin, and the three routes that can produce one
/// are not equally good, so they are tried in the order that costs the technician
/// least:
///
/// 1. Nothing, when the secure origin was switched off on purpose.
/// 2. adb reverse, which needs no internet, nothing on the phone, and gives
///    localhost, the one address every browser trusts. Android keeps this.
/// 3. A public https address in front of the local server, which is the only route
///    an iPhone has. It needs internet on both ends and carries the results past
///    Cloudflare, so it is only opened for a phone that cannot use the cable.
/// 4. The plain network address, which works for everything except the restricted
///    APIs, and says so instead of failing quietly.
///
/// Each route is asked on its own and its failure is recorded rather than thrown,
/// so one broken route cannot hide the answer from the routes behind it.
/// </summary>
public sealed class WebRunnerOriginResolver
{
    /// <summary>Opens the private route the cable already provides.</summary>
    public delegate Task<bool> UsbRoute(string sessionUdid, int port);

    /// <summary>Opens a public https address in front of the local server.</summary>
    public delegate Task<string?> InternetRoute(int port, Action<string>? onStatus);

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
    /// whatever the operator should be reading while the answer is still being
    /// worked out, which can take a while on a first run.
    /// </summary>
    public async Task<WebRunnerOrigin> ResolveAsync(
        string sessionUdid,
        int port,
        string lanAddress,
        string loopbackAddress,
        bool secureOriginEnabled,
        bool publicTunnelEnabled,
        Action<string>? onStatus = null)
    {
        if (string.IsNullOrWhiteSpace(lanAddress)) lanAddress = loopbackAddress;

        if (!secureOriginEnabled)
        {
            return new WebRunnerOrigin(lanAddress, false, null);
        }

        var failed = new List<string>(2);

        if (AdbReverseTunnel.SupportsReverse(sessionUdid))
        {
            onStatus?.Invoke(ConnectionText.Get("openingUsb"));

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

            failed.Add(ConnectionText.Get("routeUsb"));
        }

        // A placeholder session is only ever opened by the technician's own browser
        // to look around, and a tunnel for it would publish the app for nothing.
        if (publicTunnelEnabled && !IsPlaceholderSession(sessionUdid))
        {
            // Taken once, because the route narrates in the same language and the
            // comparison below has to recognise its own opening sentence.
            string opening = ConnectionText.Get("openingInternet");
            onStatus?.Invoke(opening);

            // The route narrates what it is doing and, when it cannot, why. Only the
            // second is worth carrying into the warning: the first is the sentence
            // this method has just said, and nesting it inside the reason reads as
            // if opening the connection were what went wrong.
            string? reported = null;

            string? internet = null;
            try
            {
                internet = await _internet(port, message =>
                {
                    if (!string.Equals(message, opening, StringComparison.Ordinal))
                        reported = message;
                    onStatus?.Invoke(message);
                });
            }
            catch (Exception ex)
            {
                SystemEventLogger.Warning(LogSource.UsbDetector, $"The internet tunnel could not be opened: {ex.Message}");
                reported = ConnectionText.Format("internetStartFailed", ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(internet))
            {
                return new WebRunnerOrigin(internet.TrimEnd('/'), true, null);
            }

            failed.Add(ConnectionText.Get("routeInternet"));

            if (!string.IsNullOrWhiteSpace(reported))
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
    /// <paramref name="detail"/> is whatever the failing route said about itself,
    /// which is the only part an operator cannot work out from the sentence.
    /// </summary>
    private static WebRunnerOrigin Insecure(string lanAddress, List<string> failed, string? detail)
    {
        string reason = failed.Count == 0
            ? ConnectionText.Get("secureOff")
            : ConnectionText.Format(
                failed.Count > 1 ? "routeFailedMany" : "routeFailedOne",
                string.Join(ConnectionText.Get("routeSeparator"), failed));

        string sentence = string.IsNullOrWhiteSpace(detail)
            ? ""
            : $" {detail.TrimEnd('.')}.";

        return new WebRunnerOrigin(lanAddress, false,
            $"{reason}.{sentence} {ConnectionText.Get("restrictedApis")}");
    }
}
