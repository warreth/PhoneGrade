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

    /// <summary>
    /// What the identifier needs to become before it can go on a barcode.
    /// </summary>
    /// <remarks>
    /// The wrapping is Code39's: every Code39 barcode opens and closes with an
    /// asterisk, and a value that does not begin and end with one has no end a reader
    /// can find. Code128 ends itself, so wrapping it in asterisks would put two
    /// characters on the label that are not part of the identifier.
    ///
    /// What else changes is how much has to be replaced. Code39 carries digits,
    /// capitals and seven symbols, so a model called "iPhone 13 Pro" comes back as
    /// dashes. Code128 carries every printable ASCII character, so the same string
    /// goes on the code exactly as the phone reported it.
    /// </remarks>
    /// <param name="identifier">The device identifier as the phone reported it.</param>
    /// <param name="symbology">Which symbology it is going on.</param>
    /// <param name="reason">
    /// Why it changed, or null when nothing had to. A reason is worth showing
    /// because a serial that was rewritten is not the serial the phone reported.
    /// </param>
    /// <param name="wording">How to say the above in the operator's language.</param>
    public static string? Encode(
        string identifier, LabelCodeSymbology symbology, out string? reason,
        ExportWording? wording = null)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        // A placeholder is not a device. A barcode reading NOID would scan on every
        // phone the shop has not managed to read yet, which is worse than no barcode
        // at all.
        if (identifier == DevicePlaceholders.Identifier) return null;

        string value = symbology == LabelCodeSymbology.Code39
            ? identifier.Trim('*')
            : identifier;

        // Already in a form that carries without changing, which is every character
        // of it.
        if (Carries(value, symbology))
            return symbology == LabelCodeSymbology.Code39 ? $"*{value}*" : value;

        var cleaned = new System.Text.StringBuilder(value.Length);
        bool changed = false;

        foreach (char character in value)
        {
            if (CanCarry(character, symbology))
            {
                cleaned.Append(character);
                continue;
            }

            changed = true;
            cleaned.Append('-');
        }

        reason = changed
            ? (wording ?? ExportWording.English).BarcodeReplaced
            : (wording ?? ExportWording.English).BarcodeWrapped;

        return symbology == LabelCodeSymbology.Code39
            ? $"*{cleaned}*"
            : cleaned.ToString();
    }

    /// <summary>The identifier as the Code39 writer has always had it.</summary>
    public static string? Encode(
        string identifier, out string? reason, ExportWording? wording = null) =>
        Encode(identifier, LabelCodeSymbology.Code39, out reason, wording);

    /// <summary>Whether the identifier can go on a barcode exactly as it stands.</summary>
    public static bool IsEncodable(string identifier) => Encode(identifier, out _) == $"*{identifier}*";

    /// <summary>Whether a character this symbology can carry as it stands.</summary>
    public static bool CanCarry(char character, LabelCodeSymbology symbology) => symbology switch
    {
        LabelCodeSymbology.Code128 => Code128.CanEncode(character.ToString()),
        _ => Code39.CanEncode(character.ToString()),
    };

    /// <summary>Whether every character of a value can be carried as it stands.</summary>
    public static bool Carries(string value, LabelCodeSymbology symbology) =>
        value.All(character => CanCarry(character, symbology));

    /// <summary>
    /// The bars and spaces of a value in a given symbology, each with its width.
    /// </summary>
    /// <remarks>
    /// One place that knows both alphabets, so the width the paper is measured
    /// against, the width the preview draws and the width the label prints are
    /// worked out from the same list of elements. A renderer handed the wrong one
    /// draws a code the fit test never looked at.
    /// </remarks>
    public static IEnumerable<BarcodeElement> ElementsOf(
        string value, LabelCodeSymbology symbology) => symbology == LabelCodeSymbology.Code128
            ? Code128.Elements(value)
            : Code39.Elements(value);

    /// <summary>How many units a value is drawn as in a given symbology.</summary>
    public static int UnitsOf(string value, LabelCodeSymbology symbology)
    {
        int units = 0;
        foreach (BarcodeElement element in ElementsOf(value, symbology)) units += element.Units;

        return units;
    }

    /// <summary>
    /// How wide one narrow element has to be for this value to fill that width,
    /// including the quiet zones a scanner needs on each side.
    /// </summary>
    public static float NarrowMm(
        string value, LabelCodeSymbology symbology, float availableMm)
    {
        float units = UnitsOf(value, symbology) + (2 * QuietZoneUnits);

        return units > 0 ? availableMm / units : availableMm;
    }

    /// <summary>How wide the drawn code comes out, at a given bar width.</summary>
    public static float WidthMm(
        string value, LabelCodeSymbology symbology, float narrowMm) =>
        (UnitsOf(value, symbology) + (2 * QuietZoneUnits)) * narrowMm;

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
    public static float DrawnNarrowMm(
        string value, LabelCodeSymbology symbology, float availableMm) =>
        PngWriter.MmOf(PngWriter.DotsFor(NarrowMm(value, symbology, availableMm)));
}
