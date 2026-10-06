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
    /// The single code a shop that works entirely from its labels wants: scanned once
    /// and the whole device arrives typed. It is not offered unless it can be drawn
    /// as bars that a scanner reads, because a code squeezed into the space left is
    /// not a barcode: it comes out as a row of grey the scanner shrugs at, which is
    /// worse than no code because the operator believes it was read.
    ///
    /// In Code39 it cannot be drawn on any roll in <see cref="LabelStock.All"/>: the
    /// identifier alone is 271 units and the two together are 431, and at the narrowest
    /// bar the symbology is specified at that is 85.7mm of an 81.5mm address label. In
    /// Code128 the same value is about 69mm and does fit, which is what
    /// <see cref="LabelCode.Available"/> decides, so the setting is offered when the
    /// symbology can carry it and greyed out with the reason when it cannot.
    /// </remarks>
    Combined,

    /// <summary>Two barcodes: the identifier alone, and the specification alone.</summary>
    Split,
}

/// <summary>
/// Which symbology the barcode is drawn in.
/// </summary>
/// <remarks>
/// Code39 is what every till and every phone shop scanner already reads, and it is
/// what the DYMO template that ships with the app declares, so it stays the
/// default. It is also why one code cannot carry both the identifier and the
/// specification: fifteen units a character, and the two together are 431 of them
/// before the quiet zones, which is 85.7mm of an 81.5mm address label at the
/// narrowest bar the symbology is specified at.
///
/// Code128 carries the same value in about 69mm, because it reads a pair of digits
/// as one value and an identifier is nothing but digits. So a shop whose scanner
/// reads Code128 gets the combined code, and one whose scanner does not keeps the
/// two codes separately, which every scanner reads and which costs a little more
/// paper.
///
/// The choice is asked rather than guessed because a barcode in a symbology the
/// shop's own scanner does not read is worth nothing at all: it prints, it looks
/// like a barcode, and it reads as nothing.
/// </remarks>
public enum LabelCodeSymbology
{
    /// <summary>Code39, which every shop scanner reads.</summary>
    Code39,

    /// <summary>
    /// Code128, which is denser and carries more characters.
    /// </summary>
    /// <remarks>
    /// A scanner has to be told to read it. A till set to Code39 alone reads a
    /// Code128 code as nothing and says so with a beep, which is the failure an
    /// operator will believe rather than investigate.
    /// </remarks>
    Code128,
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
    public static bool Fits(
        LabelStock stock, string value, LabelCodeSymbology symbology = LabelCodeSymbology.Code39)
    {
        if (string.IsNullOrEmpty(value)) return true;

        // The width the code comes out at once rounded to whole dots, which is the
        // width it is actually printed at and therefore the only one a scanner will
        // ever see.
        return LabelBarcode.DrawnNarrowMm(value, symbology, stock.PrintableWidthMm)
            >= LabelBarcode.NarrowestNarrowMm;
    }

    /// <summary>The same, in Code39, which is what the .dymo template declares.</summary>
    public static bool Fits(LabelStock stock, string value) =>
        Fits(stock, value, LabelCodeSymbology.Code39);

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
    /// <param name="symbology">Which symbology the bars are drawn in.</param>
    /// <param name="identifiable">
    /// Whether the identifier can be encoded at all. A phone whose serial could not
    /// be read must not get a barcode of its placeholder.
    /// </param>
    public IReadOnlyList<string> Payloads(
        LabelBarcodeMode mode, LabelCodeSymbology symbology, bool identifiable)
    {
        if (mode == LabelBarcodeMode.None || !identifiable) return [string.Empty];

        string identifier = LabelBarcode.Encode(Identifier, symbology, out _) ?? string.Empty;
        string specification = Scannable(Specification, symbology);

        // On its own code the specification is wrapped like any other value, because
        // on its own code it is a barcode and needs a reader to be able to find its
        // end. On the combined code it is the tail of one and carries no markers of
        // its own: Code39 wrapping it put a second pair of asterisks in the middle of
        // the value where a reader would take them for data.
        //
        // The two halves of the combined code are separated by a space, because a
        // reader has to be able to tell where the identifier stops and the
        // specification starts, and fifteen digits followed by letters with nothing
        // between them is one string rather than two. A space is carried by both
        // symbologies and costs one character.
        string combined = specification.Length > 0
            ? $"{identifier.Trim('*')} {specification}"
            : identifier;

        return mode switch
        {
            LabelBarcodeMode.Identifier => [identifier],
            LabelBarcodeMode.Combined => [combined],
            _ => [identifier, LabelBarcode.Encode(specification, symbology, out _) ?? ""],
        };
    }

    /// <summary>
    /// Whether a mode can put bars on the paper at all on this stock.
    /// </summary>
    /// <remarks>
    /// What decides whether a setting is offered or greyed out. It answers for the
    /// combined code in particular, which is the one mode that cannot be drawn as
    /// Code39 on any of these rolls: offered in Code128, where it fits, and greyed
    /// out in Code39 with the reason the panel shows.
    ///
    /// Answered from the widest payload the mode draws rather than from the count of
    /// barcodes, because that is what decides whether the paper takes them. Two codes
    /// that each fit can still be wrong for a mode that draws one very wide code, and
    /// counting them would say the mode is fine.
    /// </remarks>
    /// <param name="stock">The roll in the printer.</param>
    /// <param name="mode">What the operator is choosing.</param>
    /// <param name="symbology">Which symbology it would be drawn in.</param>
    /// <param name="identifiable">
    /// Whether the identifier can be encoded at all. A phone whose serial could not
    /// be read has nothing to barcode either way, so the mode is not held against it.
    /// </param>
    public bool Available(
        LabelStock stock, LabelBarcodeMode mode, LabelCodeSymbology symbology,
        bool identifiable = true)
    {
        if (mode == LabelBarcodeMode.None) return true;
        if (!identifiable) return true;

        return Payloads(mode, symbology, identifiable)
            .Where(payload => payload.Length > 0)
            .All(payload => Fits(stock, payload, symbology));
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
    /// <param name="symbology">Which symbology the bars are drawn in.</param>
    /// <param name="identifiable">
    /// Whether the identifier can be encoded at all. A phone whose serial could not
    /// be read must not get a barcode of its placeholder.
    /// </param>
    public (IReadOnlyList<string> Barred, IReadOnlyList<string> Spelled) On(
        LabelStock stock, LabelBarcodeMode mode, LabelCodeSymbology symbology, bool identifiable)
    {
        var all = Payloads(mode, symbology, identifiable)
            .Where(payload => payload.Length > 0).ToList();

        return (all.Where(payload => Fits(stock, payload, symbology)).ToList(),
                all.Where(payload => !Fits(stock, payload, symbology)).ToList());
    }

    /// <summary>The same, in Code39, which is what the .dymo template declares.</summary>
    public (IReadOnlyList<string> Barred, IReadOnlyList<string> Spelled) On(
        LabelStock stock, LabelBarcodeMode mode, bool identifiable) =>
        On(stock, mode, LabelCodeSymbology.Code39, identifiable);

    /// <summary>
    /// The specification in the form a barcode can carry.
    /// </summary>
    /// <remarks>
    /// What it can be depends on the symbology, so this is asked of the one the
    /// label is actually drawn in rather than of a fixed alphabet.
    ///
    /// In Code39 that is capitals, spaces and seven symbols, so a model written
    /// "iPhone 13 Pro" comes back as dashes. In Code128 it is every printable ASCII
    /// character, so the same string goes on the code with its lower case letters
    /// intact, which is both shorter to scan and truer to what the phone said.
    ///
    /// Anything outside the set becomes a dash rather than being dropped, so the
    /// length of the code is known before it is drawn and the paper either fits it or
    /// does not. A code whose length was only known after it was drawn is a code the
    /// fit test could not have been run against.
    ///
    /// The display line runs through here rather than the other way round. The words
    /// on a label are for whoever picks the phone up next, and they are spelled the
    /// way the phone spells them. The code is for a scanner.
    /// </remarks>
    private static string Scannable(string specification, LabelCodeSymbology symbology)
    {
        var capitals = new System.Text.StringBuilder(specification.Length);

        foreach (char character in specification.ToUpperInvariant())
            capitals.Append(LabelBarcode.CanCarry(character, symbology) ? character : '-');

        return capitals.ToString().Trim();
    }
}
