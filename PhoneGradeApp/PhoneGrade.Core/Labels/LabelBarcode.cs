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

    /// <summary>
    /// How wide one narrow element has to be for this value to fill that width,
    /// including the quiet zones a scanner needs on each side.
    /// </summary>
    /// <remarks>
    /// The quiet zones are counted because they are part of the code. Ten narrow
    /// elements of clear paper on each side is what lets a scanner find the first
    /// bar and the last one; a code drawn to the exact width of the paper and
    /// clipped by the label's own edge is a code that reads back wrong on half the
    /// devices in a shop and is not obviously wrong on the printer.
    /// </remarks>
    public static float NarrowMm(string value, float availableMm)
    {
        float units = UnitsOf(value) + (2 * QuietZoneUnits);

        return units > 0 ? availableMm / units : availableMm;
    }

    /// <summary>
    /// How wide the drawn code comes out, in millimetres, at a given bar width.
    /// </summary>
    public static float WidthMm(string value, float narrowMm) => (UnitsOf(value) + (2 * QuietZoneUnits)) * narrowMm;

    /// <summary>
    /// How many units a value is drawn as.
    /// </summary>
    /// <remarks>
    /// Counted from the elements themselves rather than worked out from the length
    /// of the text, so the width the paper is measured against and the width the
    /// barcode is actually drawn at cannot drift apart. Fifteen characters is
    /// 271 units: fifteen a character, six narrow elements and three wide ones out
    /// of every nine, plus the narrow gap that separates one character from the
    /// next.
    /// </remarks>
    public static int UnitsOf(string value)
    {
        int units = 0;
        foreach (Code39Element element in Code39.Elements(value)) units += element.Units;

        return units;
    }

    /// <summary>
    /// The width one narrow element actually comes out at, once it has been rounded
    /// to whole dots.
    /// </summary>
    /// <remarks>
    /// This is the number that decides whether a code can be read, not the measured
    /// width it was asked for. A barcode measured at exactly the floor rounds down
    /// to below it, and a barcode rounded up is wider than the paper it was measured
    /// against. Both are decided here so the fit test and the drawing cannot
    /// disagree about which one they are looking at.
    /// </remarks>
    public static float DrawnNarrowMm(string value, float availableMm) =>
        PngWriter.MmOf(PngWriter.DotsFor(NarrowMm(value, availableMm)));

    /// <summary>
    /// The narrowest element a Code39 is read at.
    /// </summary>
    /// <remarks>
    /// 0.19 millimetres is about seven and a half thousandths of an inch, which is
    /// the floor the symbology is specified at and the narrowest a 300 dpi head can
    /// print without the bar breaking up into a grey smudge.
    ///
    /// The width a code is actually drawn at is a whole number of dots, so it never
    /// lands on this number but on the next one above it. At twelve dots a
    /// millimetre that is 0.25mm, and a code measured at 0.20mm is therefore drawn
    /// at 0.1667mm and is not readable. <see cref="DrawnNarrowMm"/> is what the fit
    /// test asks about, so that a code is never accepted at a width it cannot be
    /// printed at.
    /// </remarks>
    public const float NarrowestNarrowMm = 0.19f;

    /// <summary>Clear paper a scanner needs on each side of a code, in narrow elements.</summary>
    public const int QuietZoneUnits = 10;
}