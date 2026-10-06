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
    public static float BlockSize(IReadOnlyList<string> block, LabelLayout layout, int barcodes)
    {
        int longest = 0;
        foreach (string line in block) longest = Math.Max(longest, line.Length);

        float across = longest == 0
            ? LargestBodyPoint
            : layout.WidthMm / (longest * AdvanceMmPerPoint);

        return Math.Clamp(Math.Min(across, LargestFor(layout, barcodes, block.Count)),
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
    /// larger than the rest. Dividing the available height by the number of lines
    /// and then setting one of them a third bigger is how the locks line came to be
    /// dropped from the label: the block did not fit, and the layout engine answered
    /// an overfull label by leaving out the last thing on it.
    /// </remarks>
    private static float LargestFor(LabelLayout layout, int barcodes, int lines)
    {
        float available = layout.TextHeightMm(barcodes);

        // One line of the block is set larger than the others by this much, so it
        // occupies more than one line height and has to be paid for.
        float heights = lines + ((lines > 0 ? lines - 1 : 0) * (LocksLargerThanBody - 1));
        float perLine = available / Math.Max(1f, heights);

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
    public static float Centring(
        LabelLayout layout, int barcodes, float body, int lines, bool locksSet)
    {
        float room = layout.TextHeightMm(barcodes);

        // The locks line is set larger than the rest, so it occupies more than one
        // line height and has to be paid for out of the same room.
        float taken = lines * body + (locksSet ? body * (LocksLargerThanBody - 1) : 0);

        return Math.Max(0f, (room - taken * LineHeightInMm) / 2f);
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

    /// <summary>The biggest the text block is ever set.</summary>
    public const float LargestBodyPoint = 9f;

    /// <summary>
    /// The smallest. Below this the words stop being words an operator can read at
    /// arm's length, and a label nobody can read is a label that has failed.
    /// </summary>
    public const float SmallestBodyPoint = 4.5f;
}
