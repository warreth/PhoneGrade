namespace PhoneGrade.Core;

/// <summary>
/// The barcode on the label.
///
/// The template encodes the device identifier as Code39. That is the right choice
/// for a thermal label printer and the wrong one to write from scratch, because
/// Code39 has a quirk that matters here: it can only encode a fixed set of
/// characters, and a serial number is free to contain characters outside it. An
/// identifier with an ampersand in it either fails to encode or, worse, encodes as
/// something a scanner reads back as a different device.
///
/// So this reports whether an identifier can be encoded before it is drawn, and
/// the label falls back to printing it as text when it cannot.
/// </summary>
public static class LabelBarcode
{
    /// <summary>
    /// What the identifier needs to become before it can go on a barcode.
    /// </summary>
    /// <param name="Reason">
    /// Why it changed, or null when nothing had to. A reason is worth showing
    /// because a serial that was rewritten is not the serial the phone reported.
    /// </param>
    public static string? Encode(string identifier, out string? reason, ExportWording? wording = null)
    {
        var words = wording ?? ExportWording.English;
        reason = null;
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        // A placeholder is not a device. A barcode reading NOID would scan on
        // every phone the shop has not managed to read yet, which is worse than
        // no barcode at all.
        if (identifier == DevicePlaceholders.Identifier) return null;

        // Already wrapped, and every character is one Code39 can carry: the value
        // goes on the label exactly as the phone reported it.
        if (identifier.StartsWith('*') && identifier.EndsWith('*')
            && Code39.CanEncode(identifier))
            return identifier;

        if (Code39.CanEncode(identifier) && !identifier.Contains('*'))
            return $"*{identifier}*";

        // Every Code39 barcode opens and closes with an asterisk, and an
        // identifier that does not begin or end with one has to have it added or
        // a scanner cannot tell where the data stops.
        string wrapped = $"*{identifier.Trim('*')}*";

        var cleaned = new System.Text.StringBuilder(wrapped.Length);
        bool changed = false;
        foreach (char character in wrapped)
        {
            if (Code39.CanEncode(character.ToString()))
            {
                cleaned.Append(character);
                continue;
            }

            changed = true;
            cleaned.Append('-');
        }

        reason = changed ? words.BarcodeReplaced : words.BarcodeWrapped;
        return cleaned.ToString();
    }

    /// <summary>Whether the identifier can go on a barcode exactly as it stands.</summary>
    public static bool IsEncodable(string identifier) => Encode(identifier, out _) == $"*{identifier}*";
}