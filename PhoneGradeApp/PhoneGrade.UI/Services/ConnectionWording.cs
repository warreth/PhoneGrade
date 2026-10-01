using System;
using System.Collections.Generic;
using System.Linq;
using PhoneGrade.Core;

namespace PhoneGrade.UI.Services;

/// <summary>
/// Turns what the connection routes worked out into the sentence the operator
/// reads on the status line.
///
/// Core knows which route was tried, which one failed, and what a failing route
/// said about itself; it does not know the language, and that is deliberate.
/// Every sentence here comes from the dictionaries the rest of the window reads,
/// so a window set to English reads English under the QR code and a sentence
/// written once is written in both languages or not at all.
///
/// The one thing not translated is the connector's own complaint, which is
/// quoted material carried in <c>ConnectionNotice.Detail</c>: it arrives as the
/// connector wrote it and is dropped into the placeholder without being touched.
/// </summary>
public static class ConnectionWording
{
    /// <summary>One step of the connection routes, in the language in use.</summary>
    public static string Say(ConnectionNotice notice) => notice.Step switch
    {
        ConnectionStep.OpeningUsb => LocalizationManager.GetString("Connect_OpeningUsb"),
        ConnectionStep.OpeningInternet => LocalizationManager.GetString("Connect_OpeningInternet"),
        ConnectionStep.InternetStartFailed => string.Format(
            LocalizationManager.GetString("Connect_StartFailed"), notice.Detail),
        ConnectionStep.NoConnector => LocalizationManager.GetString("Connect_NoConnector"),
        ConnectionStep.GaveUp => LocalizationManager.GetString("Connect_GaveUp"),
        ConnectionStep.NoAddress => string.Format(
            LocalizationManager.GetString("Connect_NoAddress"), notice.Detail),

        // Unreachable while the switch above names every step. The test that walks
        // Enum.GetValues keeps it that way, and a step that slipped through has to
        // read as something rather than as nothing at all.
        _ => notice.Detail ?? notice.Step.ToString()
    };

    /// <summary>
    /// Why the address in the QR code is not a secure one, spelled out in full:
    /// which routes could not open one, what a failing route said if it said
    /// anything, and what the operator loses because of it.
    /// </summary>
    public static string Warn(ConnectionWarning warning)
    {
        string reason = warning.Failed.Count == 0
            ? LocalizationManager.GetString("Connect_SecureOff")
            : string.Format(
                warning.Failed.Count > 1
                    ? LocalizationManager.GetString("Connect_FailedMany")
                    : LocalizationManager.GetString("Connect_FailedOne"),
                Names(warning.Failed));

        // A detail is its own sentence and is quoted rather than folded into the
        // reason: nesting "opening the connection" inside "opening the connection
        // failed" reads as if that were what went wrong.
        string told = warning.Reason is null
            ? ""
            : $" {Say(warning.Reason).TrimEnd('.')}.";

        return $"{reason}.{told} {LocalizationManager.GetString("Connect_RestrictedApis")}";
    }

    /// <summary>The failed routes, named and joined the way the language joins things.</summary>
    private static string Names(IReadOnlyList<ConnectionRoute> failed)
    {
        string joiner = LocalizationManager.GetString("Connect_Joiner");
        return string.Join($" {joiner} ", failed.Select(Name));
    }

    private static string Name(ConnectionRoute route) => route switch
    {
        ConnectionRoute.Usb => LocalizationManager.GetString("Connect_RouteUsb"),
        ConnectionRoute.Internet => LocalizationManager.GetString("Connect_RouteInternet"),
        _ => route.ToString()
    };
}
