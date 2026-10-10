namespace PhoneGrade.Core;

/// <summary>
/// How large the label's words are set, in points.
/// </summary>
/// <remarks>
/// Worked out from the stock, the barcode mode and the lines themselves, so that a
/// 28mm address label and a 59mm shipping label are both filled rather than one
/// being a wall of text and the other three words in a corner.
///
/// This lives here rather than in the PDF writer because two renderers need it: the
/// file that gets printed, and the preview the operator checks it against. Sizing
/// worked out twice is sizing that eventually disagrees, and a preview that sets
/// its text larger than the file does is a preview that cannot be trusted to tell
/// the operator whether the words will fit.
/// </remarks>
public static class LabelType
{
    /// <summary>
    /// The one size the whole text block is set at, taken from its longest line.
    /// </summary>
    /// <remarks>
    /// Set per line the block reads as three pieces of paper rather than one label.
    /// Set once from the longest line it reads as one label, which is also the only
    /// way the lines are guaranteed to fit the height the barcodes have left.
    ///
    /// The whole printable width is allowed, because the advance above is the width
    /// the words actually come out at and a line that lands on the edge is a line
    /// that overflows by a hair somewhere else. A line too long for that wraps, and
    /// wrapping a rare line of capitals is a smaller fault than setting every label
    /// at half the size it could be.
    /// </remarks>
    /// <param name="block">The lines the label carries, in the order it says them.</param>
    /// <param name="layout">The stock and the arrangement on it.</param>
    /// <param name="barcodes">How many barcodes are drawn above the text.</param>
    /// <param name="widthMm">
    /// How much of the paper's width the words may use, when something else on the
    /// label has taken a share of it. The grade block does, and measuring the text
    /// against the full width put it under the block. Null means the printable width.
    /// </param>
    /// <param name="heightLines">
    /// How many line heights the label draws, when that is not one per line of the
    /// block. An arrangement that draws its locks on a line below the block rather
    /// than as one of its lines has a whole extra line to pay for, and sizing the
    /// block as if it were the whole label is how the locks line came off the bottom.
    /// </param>
    public static float BlockSize(
        IReadOnlyList<string> block, LabelLayout layout, int barcodes, float? widthMm = null,
        float? heightLines = null)
    {
        int longest = 0;
        foreach (string line in block) longest = Math.Max(longest, line.Length);

        float across = longest == 0
            ? LargestBodyPoint
            : (widthMm ?? layout.WidthMm) / (longest * AdvanceMmPerPoint);

        return Math.Clamp(Math.Min(across, LargestFor(layout, barcodes, heightLines ?? block.Count)),
            SmallestBodyPoint, LargestBodyPoint);
    }

    /// <summary>
    /// The size one line is set at, held between the size the width can take and
    /// the size the height can take, and never below the smallest readable.
    /// </summary>
    /// <remarks>
    /// The two limits are different limits and a label can hit either. A nine point
    /// line fits across an 81mm address label and does not fit down what is left of
    /// it under a barcode.
    /// </remarks>
    public static float LineSize(
        string text, LabelLayout layout, int barcodes, float largest, float smallest)
    {
        if (text.Length == 0) return smallest;

        float across = layout.WidthMm / (text.Length * AdvanceMmPerPoint);

        return Math.Clamp(Math.Min(across, LargestFor(layout, barcodes, 1)), smallest, largest);
    }

    /// <summary>
    /// The largest type a block of a given shape fits down the height that is left.
    /// </summary>
    /// <remarks>
    /// Counted in line heights rather than in lines, because the locks line is set
    /// larger than the rest and the arrangements differ in whether it is one of the
    /// block's lines or an extra one below it. The caller says how many line heights
    /// the label actually draws. Dividing the available height by the number of
    /// block lines and then setting one of them a third bigger is how the locks line
    /// came to be dropped from the label: the block did not fit, and the layout
    /// engine answered an overfull label by leaving out the last thing on it.
    /// </remarks>
    private static float LargestFor(LabelLayout layout, int barcodes, float lines)
    {
        float available = layout.TextHeightMm(barcodes);
        float perLine = available / Math.Max(1f, lines);

        return Math.Clamp(perLine / LineHeightInMm, SmallestBodyPoint, LargestBodyPoint);
    }

    /// <summary>
    /// Empty paper above the content, so a label with room to spare sits in the
    /// middle of its roll rather than clinging to the top of it.
    /// </summary>
    /// <remarks>
    /// The same figure is also the empty paper below it, which is what makes the
    /// content centred rather than merely pushed down. It is half of what the text
    /// block does not need, so a 28mm address label gets none at all and a 59mm
    /// shipping label gets a quarter of its height above and below.
    ///
    /// Pinned to the top instead, a big roll came out as a barcode against the top
    /// edge, a hand's width of white paper, and then the words, which reads as a
    /// label that ran out of something.
    /// </remarks>
    /// <param name="extraMm">
    /// Room taken by something taller than a line of type, in millimetres. The grade
    /// block is the thing this exists for: it has to be paid for out of the same room
    /// as the words or the locks come off the bottom of the label.
    /// </param>
    /// <param name="lines">
    /// The lines set at the body size, without the locks. Those are a line of their
    /// own and are paid for here as one, because the arrangements that draw them
    /// below the block rather than inside it were reserving a third of a line for
    /// them and putting them under the paper with it.
    /// </param>
    public static float Centring(
        LabelLayout layout, int barcodes, float body, int lines, bool locksSet,
        float extraMm = 0)
    {
        float room = layout.TextHeightMm(barcodes);

        float taken = (lines + (locksSet ? LocksLargerThanBody : 0)) * body * LineHeightInMm
                      + extraMm;

        return Math.Max(0f, (room - taken) / 2f);
    }

    /// <summary>
    /// How much bigger the locks line is set than the rest of the block.
    /// </summary>
    /// <remarks>
    /// They are the one fault that costs a shop the sale and the one an operator
    /// reads last if it is the same size as everything else. A FRP locked phone
    /// that the next owner activates wipes itself, and this line is the last place
    /// that could have said so.
    /// </remarks>
    public const float LocksLargerThanBody = 1.3f;

    /// <summary>
    /// How wide one character is per point of type, in millimetres.
    /// </summary>
    /// <remarks>
    /// Measured off a rendered label rather than assumed, because assuming this
    /// figure is what sets every label in six point type and leaves a 59mm roll
    /// three quarters empty. In Lato SemiBold at nine point a typical label line
    /// runs 0.171mm a character and a line of digits runs 0.204; a row of wide
    /// capitals reaches 0.328 and a row of narrow ones 0.086.
    ///
    /// 0.21 is the figure for text a label actually carries, with a little over it.
    /// The widest thing measured, a line of nothing but wide capitals, is not
    /// something a phone inspection produces, and sizing for it would set every
    /// real label smaller than it needs to be.
    /// </remarks>
    private const float AdvanceMmPerPoint = 0.21f;

    /// <summary>
    /// How tall one line is for one point of type, in millimetres: about a fifth
    /// more than the point itself.
    /// </summary>
    /// <remarks>
    /// Taken off a rendered label rather than from the font's own metrics, which
    /// cannot be asked before the document exists.
    /// </remarks>
    public const float LineHeightInMm = 0.3528f * 1.2f;

    /// <summary>
    /// How wide a line of type comes out, in millimetres, as a sizing estimate.
    /// </summary>
    /// <remarks>
    /// The same figure the block sizing works on, so a band drawn around a line and
    /// the size the line is set at cannot disagree about how wide it is. A renderer
    /// that measures for itself is how a black band ends up narrower than the words
    /// on it, or twice as wide.
    /// </remarks>
    public static float EstimateWidthMm(string text, float point) =>
        text.Length * point * AdvanceMmPerPoint;

    /// <summary>
    /// The biggest the text block is ever set.
    /// </summary>
    public const float LargestBodyPoint = 9f;

    /// <summary>
    /// The smallest. Below this the words stop being words an operator can read at
    /// arm's length, and a label nobody can read is a label that has failed.
    /// </summary>
    public const float SmallestBodyPoint = 4.5f;
}
