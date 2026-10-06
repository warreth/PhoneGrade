namespace PhoneGrade.Core;

/// <summary>
/// Which of the three things a label can say are on it.
/// </summary>
/// <remarks>
/// All three are on by default, and a shop turns them off for real reasons: a
/// counter that only grades the battery as a percentage does not need the charge
/// count, and a till that cannot read Code39 needs no barcode at all.
////
/// Passed as one value to every renderer rather than as three switches, because a
/// renderer that was told about two of the three would show a label the file does
/// not contain, and the preview is the only place an operator can catch that
/// before the roll is used up.
/// </remarks>
/// <param name="BatteryCycles">Whether the charge count is printed.</param>
/// <param name="Faults">Whether the faults are printed.</param>
/// <param name="Locks">Whether the locks are printed.</param>
public sealed record LabelContent(
    bool BatteryCycles = true,
    bool Faults = true,
    bool Locks = true)
{
    /// <summary>Everything on, which is what the label says unless a shop says otherwise.</summary>
    public static LabelContent Everything { get; } = new();
}

/// <summary>
/// One DYMO label stock: the paper, and the part of it that can be printed on.
/// </summary>
/// <remarks>
/// The stock sizes are the real ones, named by the DYMO part number rather than by
/// millimetres, because a shop orders by part number and two rolls both called
/// "address" are 28mm and 36mm and nothing about the name tells them apart.
///
/// The printable area is not guessed. DYMO templates carry the label's own geometry
/// and the shipped template describes 81.5 x 25.3mm of paper inside a 28 x 89mm
/// label, which is the address label every DYMO LabelWriter takes. Those margins
/// are what the printer can reach, and drawing outside them is drawing on the
/// backing carrier, which is the one part of the roll an operator sees and the
/// customer does not.
/// </remarks>
/// <param name="PartNumber">The DYMO part number, which is how the roll is ordered.</param>
/// <param name="Name">As the picker shows it.</param>
/// <param name="WidthMm">Label width across the stock.</param>
/// <param name="HeightMm">Label height along the stock.</param>
/// <param name="SideMarginMm">Paper the printer cannot reach, on each side.</param>
/// <param name="TopMarginMm">Paper the printer cannot reach, at the top.</param>
/// <param name="BottomMarginMm">Paper the printer cannot reach, at the bottom.</param>
public sealed record LabelStock(
    string PartNumber,
    string Name,
    float WidthMm,
    float HeightMm,
    float SideMarginMm = 3.75f,
    float TopMarginMm = 1.35f,
    float BottomMarginMm = 1.35f)
{
    /// <summary>How wide the printable area is, which is what the template describes.</summary>
    public float PrintableWidthMm => WidthMm - (2 * SideMarginMm);

    /// <summary>How tall the printable area is.</summary>
    public float PrintableHeightMm => HeightMm - TopMarginMm - BottomMarginMm;

    /// <summary>As the picker shows it: the part number and the size.</summary>
    public string Label => $"{Name} ({PartNumber})";

    public override string ToString() => Label;

    /// <summary>
    /// The stocks the panel offers, most used first.
    ///
    /// The address label leads because it is the roll in almost every phone shop:
    /// it is the default DYMO address stock for the LabelWriter 450 and every
    /// machine since, and it is the one the shipped template was drawn for.
    /// </summary>
    public static IReadOnlyList<LabelStock> All { get; } =
    [
        new("1982991", "Address 89 x 28 mm", 89f, 28f),
        new("1983172", "Large address 89 x 36 mm", 89f, 36f),
        new("30334", "Multi-purpose 57 x 32 mm", 57f, 32f, 3f),
        new("30336", "Small multi-purpose 54 x 25 mm", 54f, 25f, 3f, 1.2f, 1.2f),
        new("30256", "Large shipping 102 x 59 mm", 102f, 59f, 4f, 2f, 2f),
        new("1933081", "Durable shelving 89 x 25 mm", 89f, 25f),
    ];

    /// <summary>The stock the shipped template was drawn for, and the default.</summary>
    public static LabelStock Address { get; } = All[0];

    /// <summary>
    /// The stock with this part number, or the address label.
    ///
    /// The fallback matters: a settings file naming a part number that has since
    /// been withdrawn should leave the operator with a label that prints rather
    /// than with an export that fails.
    /// </summary>
    public static LabelStock FromPartNumber(string? partNumber) =>
        All.FirstOrDefault(stock => string.Equals(stock.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
        ?? Address;
}

/// <summary>
/// How the barcode carries the device, which is a choice rather than a fact.
/// </summary>
/// <remarks>
/// The identifier on its own is what a till reads to find out which phone this is.
/// The specification as well is what makes a label into something a shop can work
/// from without a computer: scanned once, the whole device arrives typed. Which of
/// those a shop wants depends on what it does with the label afterwards, so it is
/// asked rather than assumed.
/// </remarks>
public enum LabelBarcodeMode
{
    /// <summary>No barcode at all, for a shop whose scanner cannot read one.</summary>
    None,

    /// <summary>One barcode, the identifier. The label reads as a price tag.</summary>
    Identifier,

    /// <summary>
    /// One barcode carrying the identifier and the specification together.
    /// </summary>
    /// <remarks>
    /// Kept because it was asked for, and honest about the fact that Code39 cannot
    /// put it on any of these rolls. The identifier alone is 271 units and its two
    /// quiet zones are twenty more, and the finest bar a 12 dots per millimetre
    /// raster can draw that is still wide enough to be read is three dots, so the
    /// identifier by itself is 72.8mm of an 81.5mm address label. Adding the
    /// specification makes it 468 units, which is 117mm of paper, and the widest
    /// stock here has 94mm of printable width.
    ///
    /// So on every roll in <see cref="LabelStock.All"/> the combined value is
    /// printed as words instead, and the settings page says so rather than letting
    /// an operator find out from a label. Carrying both in one readable code needs a
    /// denser symbology than Code39, which is a different piece of work.
    /// </remarks>
    Combined,

    /// <summary>Two barcodes: the identifier alone, and the specification alone.</summary>
    Split,
}

/// <summary>
/// What the barcode or barcodes carry, for one device.
/// </summary>
/// <remarks>
/// Built once and handed to whichever renderer is drawing, so the .dymo file, the
/// label PDF and the preview cannot encode different things. A label whose preview
/// scans to one thing and whose file scans to another is worse than no barcode.
/// </remarks>
public sealed record LabelCode(string Identifier, string Specification)
{
    /// <summary>
    /// How many barcodes a mode draws, whether or not there is anything to put in
    /// them.
    /// </summary>
    /// <remarks>
    /// The count rather than the payloads, because it is the count that decides how
    /// much of the paper the codes take. A mode that promised two codes and drew
    /// one would put its words where the second code should have been.
    /// </remarks>
    public static int BarcodeCount(LabelBarcodeMode mode) => mode switch
    {
        LabelBarcodeMode.None => 0,
        LabelBarcodeMode.Split => 2,
        _ => 1,
    };

    /// <summary>
    /// Whether this value can be drawn as bars on that stock and still be read.
    /// </summary>
    /// <remarks>
    /// This is the question the export was silently getting wrong. A Code39
    /// identifier is sixteen units a character, so a fifteen digit IMEI with its
    /// two asterisks is 271 units. At the narrowest bar a 300 dpi head holds, 0.19
    /// millimetres, that is 51.5mm of paper, and the two small multi purpose
    /// stocks have 48mm and 51mm of printable width. A code squeezed into the
    /// space left is not a barcode at all: it comes out as a row of grey the
    /// scanner shrugs at, which is worse than no code because the operator
    /// believes it was read.
    ///
    /// So where the code does not fit, nothing is printed and the value goes on the
    /// label as text instead. The decision lives here rather than in a renderer so
    /// the preview, the PDF and the .dymo file cannot disagree about it.
    /// </remarks>
    public static bool Fits(LabelStock stock, string value)
    {
        if (string.IsNullOrEmpty(value)) return true;

        // The width the code comes out at once rounded to whole dots, which is the
        // width it is actually printed at and therefore the only one a scanner will
        // ever see.
        return LabelBarcode.DrawnNarrowMm(value, stock.PrintableWidthMm)
            >= LabelBarcode.NarrowestNarrowMm;
    }

    /// <summary>
    /// The payloads for one mode, as the barcodes actually carry them.
    /// </summary>
    /// <remarks>
    /// A mode that prints two barcodes returns two payloads; the rest return one.
    /// A payload that would not encode is left empty rather than filled with
    /// something a scanner would misread, and an empty barcode object prints
    /// nothing, which is the point.
    ///
    /// The second value is the specification, not the display line. Those are
    /// different things, and running one through the other is what put
    /// <c>*-P--- 13 P-- 256GB G------ C 78% -X- B--*</c> on a label: Code39 carries
    /// capitals, so every lower case letter became a dash, and forty one characters
    /// of it is not a barcode on any roll in this list. What goes on the second
    /// code is the short capitalised form, built once here so all three renderers
    /// encode the same thing.
    /// </remarks>
    /// <param name="mode">What the operator chose.</param>
    /// <param name="identifiable">
    /// Whether the identifier can be encoded at all. A phone whose serial could not
    /// be read must not get a barcode of its placeholder.
    /// </param>
    public IReadOnlyList<string> Payloads(LabelBarcodeMode mode, bool identifiable)
    {
        if (mode == LabelBarcodeMode.None || !identifiable) return [string.Empty];

        string identifier = LabelBarcode.Encode(Identifier, out _) ?? string.Empty;
        string specification = LabelBarcode.Encode(Scannable(Specification), out _) ?? string.Empty;

        return mode switch
        {
            LabelBarcodeMode.Identifier => [identifier],
            LabelBarcodeMode.Combined => [identifier + specification],
            _ => [identifier, specification],
        };
    }

    /// <summary>
    /// The payloads for a mode, as the two lists the label is actually built from:
    /// the ones that go on as bars, and the ones that go on as words.
    /// </summary>
    /// <remarks>
    /// Split here rather than in each renderer because the split changes where
    /// everything else sits. A label with no barcode has its words where the barcode
    /// would have started, and a renderer that decided for itself which list a
    /// payload was in would put the words in the wrong place on exactly the two
    /// stocks where a fifteen digit identifier does not fit.
    /// </remarks>
    /// <param name="mode">What the operator chose.</param>
    /// <param name="identifiable">
    /// Whether the identifier can be encoded at all. A phone whose serial could not
    /// be read must not get a barcode of its placeholder.
    /// </param>
    public (IReadOnlyList<string> Barred, IReadOnlyList<string> Spelled) On(
        LabelStock stock, LabelBarcodeMode mode, bool identifiable)
    {
        var all = Payloads(mode, identifiable).Where(payload => payload.Length > 0).ToList();

        return (all.Where(payload => Fits(stock, payload)).ToList(),
                all.Where(payload => !Fits(stock, payload)).ToList());
    }

    /// <summary>
    /// The specification in the form a barcode can carry.
    /// </summary>
    /// <remarks>
    /// Capitals, spaces and the few symbols Code39 has. Anything outside that set
    /// becomes a dash rather than being dropped, so the length of the code is known
    /// before it is drawn and the paper either fits it or does not.
    ///
    /// The display line runs through here rather than the other way round. The words
    /// on a label are for whoever picks the phone up next, and they are spelled the
    /// way the phone spells them. The code is for a scanner.
    /// </remarks>
    private static string Scannable(string specification)
    {
        var capitals = new System.Text.StringBuilder(specification.Length);

        foreach (char character in specification.ToUpperInvariant())
            capitals.Append(Code39.CanEncode(character.ToString()) ? character : '-');

        return capitals.ToString().Trim();
    }
}
