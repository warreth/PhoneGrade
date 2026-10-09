namespace PhoneGrade.Core;

/// <summary>
/// How a label is arranged on its paper.
/// </summary>
/// <remarks>
/// The information on a label is the same whichever variant is chosen; what changes
/// is what the eye meets first and how the lines are divided. A shop picks one in the
/// label settings and every renderer follows it, because a preview that drew one
/// arrangement and a file that carried another is the fault the plate exists to stop.
/// </remarks>
public enum LabelVariant
{
    /// <summary>The single specification line, as the label has always been drawn, cleaned of placeholders.</summary>
    Clean,

    /// <summary>Model and storage above colour, grade and battery, with the faults between them and the locks.</summary>
    Structured,

    /// <summary>The grade as a block of its own, with the rest of the values beside it.</summary>
    GradeBlock,
}

/// <summary>The part a line plays on the label, so a renderer knows how to set it.</summary>
public enum LabelLineRole
{
    /// <summary>What the device is.</summary>
    Title,

    /// <summary>Colour, grade, battery, cycles, payment: the values that decide the price.</summary>
    Meta,

    /// <summary>What is wrong with the device.</summary>
    Faults,

    /// <summary>The locks and blocks, the one line a shop cannot miss.</summary>
    Locks,

    /// <summary>A barcode value printed as words because it would not fit the paper.</summary>
    Spelled,
}

/// <summary>One line of a label, with the size it is set at.</summary>
/// <param name="Text">What it says.</param>
/// <param name="Role">What part it plays, for a renderer that styles by role.</param>
/// <param name="PointSize">The size the line is set at, in points, worked out here.</param>
/// <param name="Bold">Whether it is set heavier than the lines around it.</param>
public sealed record LabelLine(string Text, LabelLineRole Role, float PointSize, bool Bold);

/// <summary>
/// The label as one render model: the barcodes, the lines, and the measurements the
/// PDF writer and the on screen preview both read.
/// </summary>
/// <remarks>
/// This is the one place a label is turned from values into a drawing plan. The PDF
/// and the preview used to work their sizing out separately, which is exactly how a
/// preview shows locks that the file leaves off: both rendered, and the two renders
/// disagreed. Everything that can be decided without a drawing surface is decided
/// here: which values are real, how many barcodes there are, the size of every line
/// and how much empty paper sits above the content.
/// </remarks>
public sealed record LabelPlate
{
    /// <summary>Which arrangement this plate draws.</summary>
    public required LabelVariant Variant { get; init; }

    /// <summary>
    /// Which symbology the bars are drawn in. Carried so a renderer draws the same
    /// table the fit test measured against; the payloads are already encoded.
    /// </summary>
    public required LabelCodeSymbology Symbology { get; init; }

    /// <summary>The barcode values, in the order they are drawn.</summary>
    public required IReadOnlyList<string> Barred { get; init; }

    /// <summary>The values that had to be spelled out because no barcode of them fits.</summary>
    public required IReadOnlyList<string> Spelled { get; init; }

    /// <summary>The text lines below the barcode, in the order they are drawn.</summary>
    public required IReadOnlyList<LabelLine> Lines { get; init; }

    /// <summary>The size the body of the label is set at, in points.</summary>
    public required float BodyPoint { get; init; }

    /// <summary>The size the locks line is set at, in points.</summary>
    public required float LocksPoint { get; init; }

    /// <summary>
    /// The grade letter for the grade block variant, or empty when the arrangement
    /// has no block.
    /// </summary>
    public required string Grade { get; init; }

    /// <summary>The size the grade letter is set at, in points. Zero without a block.</summary>
    public required float GradePoint { get; init; }

    /// <summary>Whether the locks line is drawn white on black rather than as ordinary text.</summary>
    public required bool LocksInverted { get; init; }

    /// <summary>Empty paper above the content, so a roll with room to spare is centred.</summary>
    public required float TopOffsetMm { get; init; }

    /// <summary>How many barcodes this plate draws, which decides the height they take.</summary>
    public int BarcodeCount => Barred.Count;

    /// <summary>Whether anything at all separates this label from blank paper.</summary>
    public bool HasContent => Barred.Count > 0 || Lines.Count > 0;

    /// <summary>The spelled values as one line, ready to draw.</summary>
    /// <remarks>
    /// The Code39 markers are taken off: they are how a scanner finds the ends of a
    /// code, not part of the serial number, and a label that spells a value out is a
    /// label for a person to read.
    /// </remarks>
    public string SpelledLine => string.Join("  ", Spelled.Select(value => value.Trim('*')));

    /// <summary>
    /// The separator between the values on one line of the structured and grade block
    /// arrangements.
    /// </summary>
    private const string Dot = "  ·  ";

    /// <summary>
    /// How many times the grade block's letter is as large as a line of type. The
    /// block is the thing the eye lands on, so it is larger than any word can be.
    /// </summary>
    private const float GradeLetterOverBody = 2.6f;

    /// <summary>How much larger the block is than the letter inside it.</summary>
    private const float GradeBoxOverLetter = 1.35f;

    /// <summary>How wide the grade block is drawn, in millimetres.</summary>
    public const float GradeBoxWidthMm = 15f;

    /// <summary>
    /// Turns an inspection into the drawing plan for one variant.
    /// </summary>
    /// <param name="fields">What the label carries, already through the value rules.</param>
    /// <param name="layout">The stock and the arrangement of barcodes and text on it.</param>
    /// <param name="mode">What the barcodes carry.</param>
    /// <param name="symbology">Which symbology the bars are drawn in.</param>
    /// <param name="variant">Which arrangement to build.</param>
    /// <param name="cyclesMinimum">
    /// Below this charge count the cycles are left off the label, because the number
    /// says nothing at that size and the paper is worth more to everything else.
    /// </param>
    public static LabelPlate Build(
        LabelFields fields,
        LabelLayout layout,
        LabelBarcodeMode mode = LabelBarcodeMode.Identifier,
        LabelCodeSymbology symbology = LabelCodeSymbology.Code39,
        LabelVariant variant = LabelVariant.Clean,
        int cyclesMinimum = 0)
    {
        var (barred, spelled) = new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields, symbology))
            .On(layout.Stock, mode, symbology, fields.IsIdentifiable);

        // The Code39 markers come off a spelled value: they are how a scanner finds
        // the ends of a code, not part of the serial number, and the line is on the
        // paper for a person to read.
        string spelledLine = string.Join("  ", spelled.Select(value => value.Trim('*')));

        // The real values, with the placeholders and the empty optional fields left
        // out. A label never says NOPAY or NOCOLOR: a value the phone did not report
        // is not a value, and the panel stops a label being printed at all when one
        // of the four that matter is missing.
        string model = Real(fields.Model, DevicePlaceholders.Model);
        string storage = Real(fields.Storage, DevicePlaceholders.Storage);
        string colour = Real(fields.Color, DevicePlaceholders.Color);
        string grade = Real(fields.Grade, DevicePlaceholders.Grade);
        string battery = Real(fields.Battery, DevicePlaceholders.Battery);
        string pay = Real(fields.PayMethod, DevicePlaceholders.PayMethod);
        string cycles = CycleToken(fields, cyclesMinimum);
        string faults = fields.Content.Faults ? fields.Faults.FaultsOnly : "";
        string locks = LabelLayout.LockLine(fields);

        var planned = variant switch
        {
            LabelVariant.Structured => StructuredLines(model, storage, colour, grade, battery, cycles, pay, faults),
            LabelVariant.GradeBlock => GradeBlockLines(model, storage, colour, battery, pay, cycles, faults),
            _ => CleanLines(model, storage, colour, grade, battery, pay, cycles, faults, locks),
        };

        var block = new List<string>();
        if (spelledLine.Length > 0) block.Add(spelledLine);
        block.AddRange(planned.Where(line => line.Text.Length > 0).Select(line => line.Text));

        // The grade block takes width the words cannot use, so the body size is
        // measured against what is left of the paper. Without it a two line block on
        // an address label sizes to the full width and runs under the box.
        float textWidth = variant == LabelVariant.GradeBlock
            ? Math.Max(8f, layout.WidthMm - GradeBoxWidthMm - 2f)
            : layout.WidthMm;

        float body = LabelType.BlockSize(block, layout, barred.Count, textWidth);
        bool hasLocks = locks.Length > 0;
        float locksPoint = hasLocks
            ? LabelType.LineSize(locks, layout, barred.Count, body * LabelType.LocksLargerThanBody, body)
            : body;

        // The grade block is taller than the lines of type beside it and has to be
        // paid for out of the same room, or the locks come off the bottom of the
        // label: the failure this arrangement would hit first.
        float gradePoint = body * GradeLetterOverBody;
        float boxHeightMm = gradePoint * GradeBoxOverLetter * (25.4f / 72f);
        int besideLines = planned.Count(line => line.Text.Length > 0) + (spelledLine.Length > 0 ? 1 : 0);
        float besideMm = besideLines * body * LabelType.LineHeightInMm;
        float extraMm = variant == LabelVariant.GradeBlock
            ? Math.Max(0f, boxHeightMm - besideMm)
            : 0f;

        float slack = LabelType.Centring(
            layout, barred.Count, body, lines: besideLines, locksSet: hasLocks, extraMm: extraMm);

        var lines = new List<LabelLine>();
        if (spelledLine.Length > 0)
            lines.Add(new LabelLine(spelledLine, LabelLineRole.Spelled, body, Bold: false));

        foreach (PlannedLine line in planned)
        {
            if (line.Text.Length == 0) continue;
            lines.Add(new LabelLine(
                line.Text, line.Role,
                line.Role == LabelLineRole.Locks ? locksPoint : body,
                line.Bold));
        }

        // The locks go on every arrangement, and on their own line on every one of
        // them: the arrangements that keep them out of their planned lines (the grade
        // block, where they sit below the block) still carry them, and a plate that
        // forgot them would print a locked phone as a free one.
        if (hasLocks && !planned.Any(line => line.Role == LabelLineRole.Locks))
            lines.Add(new LabelLine(locks, LabelLineRole.Locks, locksPoint, Bold: true));

        return new LabelPlate
        {
            Variant = variant,
            Symbology = symbology,
            Barred = barred,
            Spelled = spelled,
            Lines = lines,
            BodyPoint = body,
            LocksPoint = locksPoint,
            Grade = variant == LabelVariant.GradeBlock ? grade : "",
            GradePoint = variant == LabelVariant.GradeBlock ? gradePoint : 0f,
            LocksInverted = variant is not LabelVariant.Clean && hasLocks,
            TopOffsetMm = slack,
        };
    }

    /// <summary>A line as planned, before the sizes are known.</summary>
    private readonly record struct PlannedLine(string Text, LabelLineRole Role, bool Bold);

    /// <summary>
    /// The single specification line and the two lines under it, which is the label
    /// as it has always been drawn with the placeholders cleaned off it.
    /// </summary>
    private static List<PlannedLine> CleanLines(
        string model, string storage, string colour, string grade, string battery,
        string pay, string cycles, string faults, string locks)
    {
        string spec = string.Join(" ", new[] { model, storage, colour, grade, battery, pay }
            .Where(part => part.Length > 0));
        string detail = string.Join(" ", new[] { cycles, faults }.Where(part => part.Length > 0));

        return
        [
            new(spec, LabelLineRole.Title, Bold: true),
            new(detail, LabelLineRole.Faults, Bold: false),
            new(locks, LabelLineRole.Locks, Bold: true),
        ];
    }

    /// <summary>Model and storage, then the values, then the faults, then the locks.</summary>
    private static List<PlannedLine> StructuredLines(
        string model, string storage, string colour, string grade, string battery,
        string cycles, string pay, string faults)
    {
        string title = string.Join(" ", new[] { model, storage }.Where(part => part.Length > 0));
        string meta = string.Join(Dot, new[] { colour, grade, battery, cycles, pay }.Where(part => part.Length > 0));

        return
        [
            new(title, LabelLineRole.Title, Bold: true),
            new(meta, LabelLineRole.Meta, Bold: false),
            new(faults, LabelLineRole.Faults, Bold: true),
        ];
    }

    /// <summary>
    /// The values beside the grade block, and the faults under it, without the locks,
    /// which go on their own line below everything.
    /// </summary>
    private static List<PlannedLine> GradeBlockLines(
        string model, string storage, string colour, string battery, string pay,
        string cycles, string faults)
    {
        string title = string.Join(" ", new[] { model, storage }.Where(part => part.Length > 0));
        string meta = string.Join(Dot, new[] { colour, battery, pay }.Where(part => part.Length > 0));
        string codes = string.Join(Dot, new[] { cycles, faults }.Where(part => part.Length > 0));

        return
        [
            new(title, LabelLineRole.Title, Bold: true),
            new(meta, LabelLineRole.Meta, Bold: false),
            new(codes, LabelLineRole.Faults, Bold: false),
        ];
    }

    /// <summary>The size the grade block is drawn at, in millimetres.</summary>
    public float GradeBoxHeightMm => GradePoint * GradeBoxOverLetter * (25.4f / 72f);

    /// <summary>The charge count as a token, or empty when it is not shown.</summary>
    private static string CycleToken(LabelFields fields, int cyclesMinimum)
    {
        if (!fields.Content.BatteryCycles) return "";
        if (fields.BatteryCycles == DevicePlaceholders.BatteryCycles) return "";

        if (!int.TryParse(fields.BatteryCycles, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int count))
            return "";

        if (count < cyclesMinimum) return "";
        return $"{count} CYCLES";
    }

    /// <summary>The value, or nothing when it is the word for "the phone did not say".</summary>
    private static string Real(string value, string placeholder) =>
        value.Length > 0 && value != placeholder ? value : "";
}
