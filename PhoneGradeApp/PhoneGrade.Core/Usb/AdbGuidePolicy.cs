namespace PhoneGrade.Core.Usb;

/// <summary>What the USB debugging how-to should do with an answer.</summary>
public enum AdbGuideDecision
{
    /// <summary>Put the how-to on screen.</summary>
    Show,

    /// <summary>Take it off screen.</summary>
    Hide,

    /// <summary>Leave it as it is, because the answer was not about this phone.</summary>
    Keep
}

/// <summary>
/// Whether the USB debugging how-to belongs on screen for a given answer from
/// the device probe.
///
/// Three things write that one flag: the USB event stream, the probe, and the
/// operator's own two buttons. Handed to each of them they disagree, and the
/// disagreement is visible on screen - "Opnieuw zoeken" used to close the
/// how-to before asking, and an answer of "nothing found" (what adb prints
/// while a phone is still enumerating) left it closed with the phone still on
/// the cable still showing the prompt.
///
/// So the rule lives here, once: two answers close it, one opens it, and
/// everything else - a missing tool, a stopped daemon, an iPhone waiting for
/// trust, a list with nothing in it - is not a claim about this phone and
/// leaves the how-to where the operator put it.
/// </summary>
public static class AdbGuidePolicy
{
    public static AdbGuideDecision Decide(DeviceService.ConnectionState state, bool dismissed)
    {
        // The prompt is on screen. Answering it is what the how-to asked for,
        // so the answer that it worked ends it; a phone whose how-to was
        // closed does not get it back over whatever the operator is reading.
        if (state == DeviceService.ConnectionState.Unauthorized)
            return dismissed ? AdbGuideDecision.Hide : AdbGuideDecision.Show;

        // adb can talk to a phone, so there is nothing left to explain.
        if (state == DeviceService.ConnectionState.Connected)
            return AdbGuideDecision.Hide;

        return AdbGuideDecision.Keep;
    }
}
