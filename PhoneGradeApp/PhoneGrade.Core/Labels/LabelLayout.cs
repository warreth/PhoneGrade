namespace PhoneGrade.Core;

/// <summary>
/// How the label is arranged on one piece of stock, in millimetres.
/// </summary>
/// <remarks>
/// One arrangement, read by the .dymo template, the label PDF and the preview. They
/// have to agree: a label whose PDF puts the grade where the .dymo puts the battery
/// is two different labels depending on which button the operator pressed, and the
/// only way that is caught is by having one source of truth for where things go.
///
/// Everything here is measured from the top left of the printable area, in the same
/// direction the DYMO template measures in. The defaults are the shipped template's
/// own geometry, so a label written through the PDF lands where the same label
/// written through the template lands.
/// </remarks>
/// <param name="Stock">The paper this is drawn on.</param>
public sealed record LabelLayout(LabelStock Stock)
{
    /// <summary>A DYMO address label, which is what the shipped template describes.</summary>
    public static LabelLayout Address { get; } = new(LabelStock.Address);

    /// <summary>
    /// The gap between two barcodes, and between the last barcode and the text.
    /// </summary>
    /// <remarks>
    /// Barcodes are placed with absolute offsets measured from the top of the
    /// printable area rather than by stacking what came before, so a second barcode
    /// cannot drift a fraction of a millimetre per label because of the one before
    /// it. That leaves the gaps to be stated rather than accumulated, which is why
    /// this is a number and not something worked out as items are added.
    /// </remarks>
    public const float GapMm = 1f;

    /// <summary>
    /// The height the text is guaranteed however many barcodes there are.
    /// </summary>
    /// <remarks>
    /// Enough for three lines at the smallest size the label is ever set at. A
    /// second barcode on a 28mm address label would otherwise take the faults line
    /// with it, and the faults are the reason to print the label.
    /// </remarks>
    public const float MinimumTextMm = 8f;

    /// <summary>The tallest one barcode is drawn, so a big label is not mostly barcode.</summary>
    public const float LargestBarcodeBandMm = 12f;

    /// <summary>
    /// The shortest, which is a code with room for its caption and nothing to spare.
    /// </summary>
    public const float SmallestBarcodeBandMm = 4f;

    /// <summary>How wide the printable area is.</summary>
    public float WidthMm => Stock.PrintableWidthMm;

    /// <summary>How tall the printable area is.</summary>
    public float HeightMm => Stock.PrintableHeightMm;

    /// <summary>How wide the paper is, which is what the PDF page has to be.</summary>
    public float PaperWidthMm => Stock.WidthMm;

    /// <summary>How tall the paper is.</summary>
    public float PaperHeightMm => Stock.HeightMm;

    /// <summary>
    /// How tall one barcode is drawn, bars and caption together, when there are
    /// this many of them.
    /// </summary>
    /// <remarks>
    /// Divided rather than fixed, because a fixed band that fits one barcode runs
    /// two off the bottom of a 28mm label. The text gets its guaranteed height first
    /// and whatever is left is shared equally, so adding a barcode shortens the
    /// barcodes rather than the words.
    /// </remarks>
    public float BarcodeBandMm(int count)
    {
        if (count <= 0) return 0f;

        float shared = HeightMm - (GapMm * (count + 1)) - MinimumTextMm;

        return Math.Clamp(shared / count, SmallestBarcodeBandMm, LargestBarcodeBandMm);
    }

    /// <summary>Where the barcode at this position starts, from the top of the print.</summary>
    public float BarcodeYmm(int index, int count) => index * (BarcodeBandMm(count) + GapMm);

    /// <summary>Where the text block starts, below however many barcodes there are.</summary>
    public float TextYmm(int count) => count <= 0 ? 0f : (BarcodeBandMm(count) + GapMm) * count;

    /// <summary>How much height is left for the text once the barcodes have taken theirs.</summary>
    public float TextHeightMm(int count) => HeightMm - TextYmm(count);

    /// <summary>Where the text block starts, for the mode the operator chose.</summary>
    public float TextYmm(LabelBarcodeMode mode) => TextYmm(LabelCode.BarcodeCount(mode));

    /// <summary>How much height is left for the text, for the chosen mode.</summary>
    public float TextHeightMm(LabelBarcodeMode mode) => TextHeightMm(LabelCode.BarcodeCount(mode));

    /// <summary>The first line: what the device is and what it is worth.</summary>
    public static string TextLine(LabelFields fields) =>
        $"{fields.Model} {fields.Storage} {fields.Color} {fields.Grade} {fields.Battery} {fields.PayMethod}";

    /// <summary>
    /// The first line again, short enough for a barcode.
    /// </summary>
    /// <remarks>
    /// The display line is forty one characters for a phone with a colour name in
    /// it, and it will not fit on a barcode on any roll in this list: sixteen units
    /// a character is 656 units, and the whole of an address label is 429 of them
    /// at the narrowest bar a scanner reads. Capitalising it does not help, and
    /// pushing the lower case letters through the encoder as dashes is worse than
    /// useless, because a till reads <c>-P--- 13 P--</c> as a real device code.
    ///
    /// So this drops what a scanner has no use for. The storage, the grade and the
    /// battery are what decide the price; the colour and the payment method are
    /// what the person at the counter already said out loud. What is left is
    /// sixteen characters, which is 256 units and fits the stock with room to spare.
    /// </remarks>
    public static string ScannableLine(LabelFields fields)
    {
        string battery = fields.Battery.Split(' ')[0].Replace("%", string.Empty);

        return Scannable(string.Join(" ",
            new[] { fields.Storage, fields.Grade, battery }
                .Where(part => part.Length > 0)));
    }

    /// <summary>
    /// Text in the capitals, spaces and symbols a Code39 barcode can carry.
    /// </summary>
    /// <remarks>
    /// Anything outside that set becomes a dash rather than being dropped, so the
    /// length of the code is known before it is drawn and the paper either fits it
    /// or does not. A dash is at least honest about there having been something
    /// there, which is not what an empty gap says.
    /// </remarks>
    private static string Scannable(string text)
    {
        var capitals = new System.Text.StringBuilder(text.Length);

        foreach (char character in text.ToUpperInvariant())
            capitals.Append(Code39.CanEncode(character.ToString()) ? character : '-');

        return capitals.ToString().Trim();
    }

    /// <summary>
    /// The second line: the charge count and the faults that are not locks.
    ///
    /// Its own line because it is the line a shop reads first. The first line says
    /// what the phone is; this one says what is wrong with it, and it is empty on a
    /// clean phone so its presence is itself the news. The locks are not repeated
    /// here because they get a line of their own, set larger.
    /// </summary>
    public static string DetailLine(LabelFields fields)
    {
        string faults = fields.Content.Faults ? fields.Faults.FaultsOnly : "";
        string cycles = !fields.Content.BatteryCycles
            || fields.BatteryCycles == DevicePlaceholders.BatteryCycles
                ? ""
                : fields.BatteryCycles + " CYCLES";

        return string.Join(" ", new[] { cycles, faults }.Where(part => part.Length > 0));
    }

    /// <summary>The third line: the locks, on their own and never shared.</summary>
    public static string LockLine(LabelFields fields) =>
        fields.Content.Locks ? fields.Faults.LockLine : "";

    /// <summary>How many text lines this label carries.</summary>
    public static int Lines(LabelFields fields) =>
        1 + (DetailLine(fields).Length > 0 ? 1 : 0) + (LockLine(fields).Length > 0 ? 1 : 0);

    /// <summary>
    /// Everything the label says about this device, in the order it says it.
    ///
    /// One list for all three renderers. The text and the barcodes are drawn from
    /// the same list, so a preview cannot show one thing and the file carry another.
    /// </summary>
    public IReadOnlyList<string> LinesOf(LabelFields fields, LabelBarcodeMode mode) =>
        new[] { TextLine(fields), DetailLine(fields), LockLine(fields) }
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>
    /// The .dymo values, keyed by the sentinels the template carries.
    ///
    /// The barcode sentinels take the payloads the operator's mode asks for, and an
    /// empty payload where a barcode is switched off: an empty barcode object in a
    /// DYMO template prints nothing, which is how a two barcode template serves a
    /// one barcode label.
    /// </summary>
    public IReadOnlyDictionary<string, string> DymoValues(
        LabelFields fields, LabelCode code, LabelBarcodeMode mode)
    {
        var payloads = code.Payloads(mode, fields.IsIdentifiable);

        return new Dictionary<string, string>
        {
            ["IDENTIFIER"] = fields.Identifier,
            ["MODEL"] = fields.Model,
            ["PCOLOR"] = fields.Color,
            ["BATTERY"] = fields.Battery,
            ["QUALITY"] = fields.Grade,
            ["PAYM"] = fields.PayMethod,
            ["STORAGE"] = fields.Storage,
            ["MEMORY"] = fields.Memory,
            ["CYCLES"] = fields.BatteryCycles,
            ["FAULTS"] = fields.Faults.Summary,
            ["LOCKS"] = fields.Faults.LockLine,
            ["DETAIL"] = DetailLine(fields),
            ["SPEC"] = TextLine(fields),

            // The two barcodes, named so the template's two objects can each take
            // one and either can arrive empty.
            ["BARCODE1"] = payloads.Count > 0 ? payloads[0] : string.Empty,
            ["BARCODE2"] = payloads.Count > 1 ? payloads[1] : string.Empty,
        };
    }
}
