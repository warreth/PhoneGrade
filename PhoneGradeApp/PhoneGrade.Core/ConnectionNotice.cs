using System.Collections.Generic;

namespace PhoneGrade.Core;

/// <summary>
/// What the connection routes have to tell, named instead of worded.
///
/// Core works out which route was tried, which one failed, and what the
/// connector said about itself. It stops at that, because the sentence around
/// those facts is wording and the wording lives in the desktop dictionaries
/// where every other sentence the window shows already lives. Handing over a
/// step and its detail rather than a finished sentence is what keeps a window
/// set to English from reading Dutch, which is what it did before.
///
/// The steps are also what the routes recognise each other by. The resolver
/// used to compare the sentence the tunnel opened with against the one it had
/// just said itself, which only held as long as nobody rewrote either one;
/// comparing the step has no such dependency.
/// </summary>
public enum ConnectionStep
{
    /// <summary>adb reverse is about to be opened.</summary>
    OpeningUsb,

    /// <summary>The public https route is about to be opened.</summary>
    OpeningInternet,

    /// <summary>Opening the public route threw before it could be tried.</summary>
    InternetStartFailed,

    /// <summary>There is no connector to run, so the public route cannot open.</summary>
    NoConnector,

    /// <summary>The connector stopped too often and is no longer being attempted.</summary>
    GaveUp,

    /// <summary>The connector ran but printed no address in time.</summary>
    NoAddress
}

/// <summary>
/// One step, with the detail that only the failing side could provide.
/// </summary>
/// <param name="Step">What happened, in the vocabulary above.</param>
/// <param name="Detail">
/// Quoted material such as an exception message or the connector's own output.
/// It is handed over as it arrived, because it is not ours to translate.
/// </param>
public sealed record ConnectionNotice(ConnectionStep Step, string? Detail = null);

/// <summary>A route that was asked for a secure address and could not open one.</summary>
public enum ConnectionRoute
{
    /// <summary>adb reverse, which only works for a handset over the cable.</summary>
    Usb,

    /// <summary>The public https address in front of the local server.</summary>
    Internet
}

/// <summary>
/// Why the address that ended up in the QR code is not a secure one.
///
/// An empty <c>Failed</c> list means no route was asked at all: the cable
/// route was switched off or does not apply, and so was the public tunnel.
/// That still reads as a warning rather than as silence: the camera and
/// motion steps are about to be unavailable and the operator is owed the
/// reason before scanning.
/// </summary>
/// <param name="Failed">Every route that was tried and could not open an address.</param>
/// <param name="Reason">
/// What a failing route said about itself, if any of them said anything worth
/// quoting. Null when the failure speaks for itself.
/// </param>
public sealed record ConnectionWarning(
    IReadOnlyList<ConnectionRoute> Failed,
    ConnectionNotice? Reason);
