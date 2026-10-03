using System;
using System.Collections.Generic;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Picks the name Windows has for a phone out of the PnP rows that share its
/// vendor ID.
///
/// One phone is a row per interface it exposes, and the composite interface is
/// called "USB Composite Device" on every phone ever made. The portable device
/// node underneath it is the one the OS fills in from the phone itself, so it
/// wins outright. Between the rest, the name that reads as a phone wins, and
/// when nothing but hardware is named the answer is an empty string: that is
/// what tells the caller to fall back to the brand rather than put a generic
/// name in front of an operator, and to read the tree again in a moment in
/// case the portable node has landed since.
/// </summary>
public static class PnpNamePicker
{
    public static string Pick(IEnumerable<(string Name, string Id)> nodes)
    {
        string portable = "";
        string best = "";
        int bestScore = -1;

        foreach ((string name, string id) in nodes)
        {
            if (name.Length == 0) continue;

            if (id.StartsWith("SWD\\WPDBUSENUM", StringComparison.OrdinalIgnoreCase))
            {
                portable = name;
                continue;
            }

            int score = AndroidDeviceName.FromReportedText(name).Length;
            if (score > bestScore)
            {
                bestScore = score;
                best = name;
            }
        }

        return portable.Length > 0 ? portable : bestScore > 0 ? best : "";
    }
}
