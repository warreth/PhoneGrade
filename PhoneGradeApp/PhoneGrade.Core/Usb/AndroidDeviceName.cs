using System.Text.RegularExpressions;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Turns the name the operating system reported for a device into something
/// worth showing to an operator.
///
/// Every platform answers with a device, not with a phone: Windows says
/// "USB Composite Device" for the interface while the portable device under it
/// is called "HONOR 600 Lite (LNA-NX1)", and udev hands back "honor LNA-NX1".
/// The factory model code in brackets means nothing to anyone looking for
/// their phone, so that comes out and what is left is the name the phone is
/// sold under, brand included: "Voor uw HONOR 600 Lite:" names a phone, where
/// "Voor uw 600 Lite:" and "Voor uw 13:" name a fragment of one. When nothing
/// is left at all, the caller falls back to the brand and then to the generic
/// "Android device".
/// </summary>
public static class AndroidDeviceName
{
    /// <summary>
    /// A model name from the reported text, or an empty string when the text
    /// only describes a piece of hardware.
    /// </summary>
    /// <param name="reported">Whatever the OS called the device.</param>
    public static string FromReportedText(string? reported)
    {
        if (string.IsNullOrWhiteSpace(reported)) return "";

        string name = reported.Trim();

        // "(LNA-NX1)" and friends are the factory model code, not a name.
        name = Regex.Replace(name, @"\s*\([^)]*\)", " ");

        // udev writes spaces in a model name as underscores.
        name = name.Replace('_', ' ');

        name = Regex.Replace(name, @"\s+", " ").Trim().Trim('-', ',', '/', ':', '|', '.').Trim();

        return IsHardwareDescription(name) ? "" : name;
    }

    /// <summary>
    /// True for a name that describes a piece of hardware rather than a model.
    /// A guide headed "USB Composite Device" tells the operator nothing they
    /// can act on, so the caller is better off falling back to the brand.
    /// </summary>
    private static bool IsHardwareDescription(string name)
    {
        if (name.Length < 2) return true;

        if (Regex.IsMatch(name, @"\bUSB\b", RegexOptions.IgnoreCase)) return true;

        return name.StartsWith("Android", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Composite", StringComparison.OrdinalIgnoreCase);
    }
}
