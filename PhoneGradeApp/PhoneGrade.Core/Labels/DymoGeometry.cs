namespace PhoneGrade.Core;

/// <summary>
/// Where the objects sit on a shipped DYMO template, in inches, in the units the
/// DesktopLabel dialect measures in.
/// </summary>
/// <remarks>
/// The label itself starts at (0.23, 0.06) on the page and is 3.21 by 0.9967
/// inches; these are page coordinates, as every ObjectLayout in the shipped
/// templates is.
///
/// The variant templates carry these as sentinels rather than as fixed numbers,
/// because how much room the words get depends on how many barcodes the operator
/// chose. Two codes take a second band and push everything down; with fixed
/// numbers the words would either run under the code or leave a hand's width of
/// paper empty above it. Computing them here keeps one template per variant
/// serving one code and two.
/// </remarks>
public static class DymoGeometry
{
    /// <summary>The top of the first barcode band, as the shipped template has it.</summary>
    public const float FirstBandTop = 0.10f;

    /// <summary>How tall the first band is drawn.</summary>
    public const float FirstBandHeight = 0.26f;

    /// <summary>The top of the second barcode band, as the shipped template has it.</summary>
    public const float SecondBandTop = 0.38f;

    /// <summary>How tall the second band is drawn.</summary>
    public const float SecondBandHeight = 0.20f;

    /// <summary>The gap between the last band and the words.</summary>
    public const float BandGap = 0.06f;

    /// <summary>Empty paper kept below the content.</summary>
    public const float BottomMargin = 0.03f;

    /// <summary>The bottom of the label on the page.</summary>
    public const float LabelBottom = 0.06f + 0.9967f;

    /// <summary>The line box one text object is given in the structured arrangement.</summary>
    public const float LineHeight = 0.105f;

    /// <summary>The height of the locks line box.</summary>
    public const float LockHeight = 0.12f;

    /// <summary>How tall the grade block is drawn, in inches.</summary>
    public const float GradeBoxHeight = 0.34f;

    /// <summary>Room kept between the grade block and the locks line under it.</summary>
    public const float GradeLockGap = 0.05f;

    /// <summary>
    /// Where one variant's objects start, and how tall they are, for one, two or no
    /// barcodes.
    /// </summary>
    /// <param name="variant">Which arrangement is being placed.</param>
    /// <param name="barcodes">How many barcode bands sit above the words.</param>
    public static DymoPositions For(LabelVariant variant, int barcodes) => variant switch
    {
        LabelVariant.GradeBlock => GradeBlock(barcodes),
        LabelVariant.Structured => Structured(barcodes),
        _ => Clean(barcodes),
    };

    /// <summary>The top of the words: below the last band, or the top of the label.</summary>
    public static float TextTop(int barcodes) => barcodes switch
    {
        <= 0 => FirstBandTop,
        1 => FirstBandTop + FirstBandHeight + BandGap,
        _ => SecondBandTop + SecondBandHeight + BandGap,
    };

    /// <summary>What the barcodes leave for the words.</summary>
    private static float Available(int barcodes) =>
        Math.Max(0.05f, (LabelBottom - BottomMargin) - TextTop(barcodes));

    /// <summary>
    /// Four lines of type under the barcodes, centred in what is left.
    /// </summary>
    private static DymoPositions Structured(int barcodes)
    {
        float line = LineHeight;
        float locks = LockHeight;
        float content = (3 * line) + locks;
        float available = Available(barcodes);

        // Two codes leave less room than four lines want; shrinking both is better
        // than letting the locks line fall off the bottom of the label.
        if (content > available)
        {
            float scale = available / content;
            line *= scale;
            locks *= scale;
            content = available;
        }

        float top = TextTop(barcodes) + Math.Max(0f, (available - content) / 2f);

        return new DymoPositions(
            Barcode2Y: SecondBandTop,
            T1Y: top,
            T2Y: top + line,
            T3Y: top + (2 * line),
            T4Y: top + (3 * line),
            BoxY: top,
            BoxH: GradeBoxHeight,
            Info1Y: top,
            Info2Y: top,
            Info3Y: top,
            InfoH: line,
            LockY: top + (3 * line),
            LockH: locks,
            LineH: line);
    }

    /// <summary>
    /// The grade block with three lines beside it and the locks below: centred in
    /// what the barcodes leave, shrunk if a second code takes too much of it.
    /// </summary>
    private static DymoPositions GradeBlock(int barcodes)
    {
        float box = GradeBoxHeight;
        float locks = LockHeight;
        float content = box + GradeLockGap + locks;
        float available = Available(barcodes);

        if (content > available)
        {
            float scale = available / content;
            box *= scale;
            locks *= scale;
            content = available;
        }

        float top = TextTop(barcodes) + Math.Max(0f, (available - content) / 2f);
        float info = Math.Max(0.05f, (box - 0.02f) / 3f);

        return new DymoPositions(
            Barcode2Y: SecondBandTop,
            T1Y: top,
            T2Y: top + info,
            T3Y: top + (2 * info),
            T4Y: top + (3 * info),
            BoxY: top + 0.01f,
            BoxH: box,
            Info1Y: top + 0.01f,
            Info2Y: top + 0.01f + info,
            Info3Y: top + 0.01f + (2 * info),
            InfoH: info,
            LockY: top + box + GradeLockGap,
            LockH: locks,
            LineH: info);
    }

    /// <summary>
    /// The cleaned arrangement's lines, which the shipped template places at its own
    /// fixed positions; this exists so every variant answers the same question.
    /// </summary>
    private static DymoPositions Clean(int barcodes)
    {
        float top = TextTop(barcodes);
        return new DymoPositions(
            Barcode2Y: SecondBandTop,
            T1Y: top,
            T2Y: top,
            T3Y: top,
            T4Y: top,
            BoxY: top,
            BoxH: GradeBoxHeight,
            Info1Y: top,
            Info2Y: top,
            Info3Y: top,
            InfoH: LineHeight,
            LockY: top,
            LockH: LockHeight,
            LineH: LineHeight);
    }

    /// <summary>One measurement as a template wants it: a plain number in inches.</summary>
    public static string Inches(float value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The size the locks are set at in the variant templates, in points.</summary>
    public const float LockPoint = 8.5f;

    /// <summary>How wide the black band around the locks is drawn, in inches.</summary>
    /// <remarks>
    /// The band hugs its words, like the app's own drawing and the PDF: a stripe
    /// over the full roll reads as a rule rather than as a warning about this phone.
    /// The width is estimated from the words at the size the templates set them,
    /// with a little over it, because DYMO shrinks type that does not fit its box
    /// and a locks line set smaller than everything else is the one line that must
    /// not whisper.
    /// </remarks>
    public static float LockBandWidthInches(string locks)
    {
        if (locks.Length == 0) return 0f;

        float mm = Math.Clamp(LabelType.EstimateWidthMm(locks, LockPoint) * 1.15f, 12f, 81.5f);
        return mm / 25.4f;
    }

    /// <summary>Where the band starts so that it sits in the middle of the roll.</summary>
    public static float LockBandLeftInches(string locks)
    {
        float band = LockBandWidthInches(locks);
        return PageLeftInches + ((PageWidthInches - band) / 2f);
    }

    /// <summary>The label's left edge on the page.</summary>
    public const float PageLeftInches = 0.23f;

    /// <summary>The label's width on the page.</summary>
    public const float PageWidthInches = 3.21f;
}

/// <summary>
/// Where one variant's objects start and how tall they are, in inches. Every field
/// is always filled; a template uses the ones it has objects for.
/// </summary>
/// <param name="Barcode2Y">The top of the second barcode band.</param>
/// <param name="T1Y">The first line of the structured arrangement.</param>
/// <param name="T2Y">The second line of the structured arrangement.</param>
/// <param name="T3Y">The third line of the structured arrangement.</param>
/// <param name="T4Y">The locks line of the structured arrangement.</param>
/// <param name="BoxY">The top of the grade block.</param>
/// <param name="BoxH">The height of the grade block.</param>
/// <param name="Info1Y">The first line beside the grade block.</param>
/// <param name="Info2Y">The second line beside the grade block.</param>
/// <param name="Info3Y">The third line beside the grade block.</param>
/// <param name="InfoH">The height of one line beside the grade block.</param>
/// <param name="LockY">The top of the locks line.</param>
/// <param name="LockH">The height of the locks line.</param>
/// <param name="LineH">The height of one line of the structured arrangement.</param>
public readonly record struct DymoPositions(
    float Barcode2Y,
    float T1Y,
    float T2Y,
    float T3Y,
    float T4Y,
    float BoxY,
    float BoxH,
    float Info1Y,
    float Info2Y,
    float Info3Y,
    float InfoH,
    float LockY,
    float LockH,
    float LineH);
