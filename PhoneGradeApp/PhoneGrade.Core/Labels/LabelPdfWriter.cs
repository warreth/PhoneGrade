using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PhoneGrade.Core;

/// <summary>
/// Draws the label as a PDF, the same label the .dymo file describes.
///
/// This is the path that works on every platform without anything installed: the
/// PDF goes to the operating system's print dialog, or to a named CUPS queue when
/// there is one. A DYMO printer on Linux is reachable this way through its CUPS
/// driver, which is the only route on that platform.
///
/// The layout is <see cref="LabelLayout"/>, the same numbers the .dymo template
/// uses, so the two agree about what goes on the label and where.
/// </summary>
public static class LabelPdfWriter
{
    /// <summary>Writes one label, the size the layout says.</summary>
    public static void Write(string path, LabelFields fields, LabelLayout? layout = null)
    {
        // Done here rather than left to the caller. Setting the licence up is one
        // line but it is the one line that has to happen before the document is
        // drawn, and a writer that throws without it is a writer every caller has
        // to remember to prepare first.
        ReportFonts.Ensure();

        LabelLayout size = layout ?? LabelLayout.Address;
                string? barcode = LabelBarcode.Encode(fields.Identifier, out _);

        Document.Create(document =>
        {
            document.Page(page =>
            {
                // The page is the label. Anything outside this rectangle is not
                // printed by a label printer, so the layout is measured in the
                // stock's own millimetres rather than in a scaled-down A4.
                page.Size(new PageSize(size.WidthMm, size.HeightMm));
                page.Margin(0);
                page.PageColor(Colors.White);

                string? family = ReportFonts.Resolve();

                page.Content().Column(column =>
                {
                    column.Spacing(0);

                    if (barcode is not null)
                    {
                        // No fixed height on this item, deliberately. Pinning the
                        // band to a height and then putting the caption inside it
                        // hands the layout engine two requirements that cannot both
                        // be met, and QuestPDF answers that by quietly dropping
                        // everything after it: the label comes out with bars on it
                        // and no text at all, which reads as a blank label rather
                        // than as a fault. Let the band take the height its bars and
                        // its caption actually need.
                        column.Item()
                            .PaddingTop(size.BarcodeYmm)
                            .Element(container => Barcode(container, barcode, size, family));
                    }

                    // The rest of the paper: the specification, what is wrong, and
                    // the locks on their own line. No height is set on any of them,
                    // because an item that believes it owns a fixed slice of the
                    // paper is what fought with the barcode band in the first
                    // place: two items that both think they own it is a layout the
                    // engine cannot always satisfy, and it answers by dropping the
                    // text rather than by complaining.
                    // Every line of the text block is set at one size, worked out
                    // from the longest of them. Sized line by line the block reads
                    // as three different pieces of paper; one size makes it read as
                    // one label.
                    string spec = LabelLayout.TextLine(fields);
                    string detail = LabelLayout.DetailLine(fields);
                    string locks = LabelLayout.LockLine(fields);

                    float body = BlockSize(spec, detail, size);
                    float lockSize = FittedSize(locks, size, body * 1.35f, body);

                    column.Item()
                        .PaddingTop(Math.Max(1f, size.TextYmm - size.BarcodeYmm - size.BarcodeHeightMm))
                        .AlignCenter().AlignMiddle()
                        .Element(container => TextLine(container, spec, size, body, family));

                    if (detail.Length > 0)
                        column.Item().AlignCenter().AlignMiddle()
                            .Element(container => Detail(container, detail, size, body, family));

                    if (locks.Length > 0)
                        column.Item().AlignCenter().AlignMiddle()
                            .Element(container => Detail(container, locks, size, lockSize, family));
                });
            });
        }).GeneratePdf(path);
    }

    /// <summary>
 /// The barcode drawn as bars.
    ///
    /// Code39 is drawn here rather than handed to a barcode library because it is
    /// nine elements of fixed widths per character, and a dependency that draws it
    /// would be a hundred times the size of the twenty lines that do. The widths
    /// come from the standard's own table: nine patterns of wide and narrow, three
    /// of them wide, and a narrow gap between one character and the next.
    ///
    /// The bars are given their widths in absolute units rather than as shares of
    /// the row. A fifteen character identifier is a hundred and fifty elements, and
    /// each one given a relative width comes out narrower than the layout engine's
    /// own minimum, which makes the whole page fail to draw.
    /// </summary>
    private static void Barcode(IContainer container, string value, LabelLayout size, string? family)
    {
        // Narrow is one unit and wide is three, which is the ratio the standard is
        // read at. Everything is scaled afterwards so the whole barcode fits the
        // label rather than a fixed number of units spilling off it.
        var elements = new List<bool>();
        foreach (string pattern in Code39.Encode(value))
        {
            for (int i = 0; i < pattern.Length; i++) elements.Add(i % 2 == 0);

            // The gap between characters. Without it one character is read as part
            // of the next and the whole identifier comes back wrong.
            elements.Add(false);
        }
        if (elements.Count > 0) elements.RemoveAt(elements.Count - 1);

        float narrow = NarrowBarWidth(elements, size);
        float barHeight = size.BarcodeHeightMm - 3f;

        container.Column(column =>
        {
            // The physical size is set rather than left to fill the column. Left to
            // fill, the image is stretched across the whole band and a barcode comes
            // out as a black rectangle, which scans as nothing and looks like a
            // printer fault rather than a drawing one.
            column.Item().Height(barHeight).AlignCenter()
                .Element(box => box
                    .Width(BarWidthMm(elements, narrow))
                    .Height(barHeight)
                    .Image(Bars(elements, narrow, barHeight)));

            // The value under the bars. A barcode nobody can read back is worse
            // than no barcode, because the operator believes it was scanned.
            column.Item().AlignCenter().Text(value)
                .FontFamily(family ?? "Helvetica").FontSize(6).FontColor(Colors.Black);
        });
    }

    /// <summary>How wide the drawn code is on the paper, in millimetres.</summary>
    private static float BarWidthMm(IReadOnlyList<bool> elements, float narrowMm)
    {
        float units = 0;
        foreach (bool isBar in elements) units += isBar ? 3 : 1;
        return units * narrowMm;
    }

    /// <summary>
    /// The bars as one picture.
    ///
    /// Painted rather than laid out, and that is the whole point. Laid out as a
    /// hundred and sixty-nine separate boxes, the label's words sat on a knife
    /// edge: making the code a third of a millimetre narrower was enough for the
    /// layout engine to decide the caption under it did not fit, and it then
    /// printed the barcode and nothing else, with every word still in the file.
    /// An image has one width and takes it, so there is nothing left to negotiate.
    ///
    /// Four dots to the millimetre, which is what a 300 dpi head can hold. The
    /// bitmap is handed over with its physical size, so the printer scales it to
    /// the label rather than deciding for itself how wide each bar is.
    /// </summary>
    private static byte[] Bars(IReadOnlyList<bool> elements, float narrowMm, float heightMm)
    {
        const float DotsPerMm = 4f;
        int narrow = Math.Max(1, (int)MathF.Round(narrowMm * DotsPerMm));
        int high = Math.Max(1, (int)MathF.Round(heightMm * DotsPerMm));

        int width = 0;
        foreach (bool isBar in elements) width += isBar ? narrow * 3 : narrow;

        return PngWriter.Barcode(elements, width, high);
    }

    /// <summary>
    /// The width of one narrow element, in millimetres, chosen so a barcode of
    /// this many elements fills the label and no more.
    ///
    /// The floor is the narrowest a 300 dpi head holds, about three dots. Fifteen
    /// digits of Code39 is 339 units, which is 95mm at a comfortable width, so an
    /// 89mm label is given the narrowest code that still scans rather than a code
    /// that runs off the side of the paper.
    /// </summary>
    private static float NarrowBarWidth(IReadOnlyList<bool> elements, LabelLayout size)
    {
        float available = size.WidthMm - (2 * size.MarginMm);
        int units = 0;
        foreach (bool isBar in elements) units += isBar ? 3 : 1;

        float fitted = units > 0 ? available / units : available;
        return Math.Max(fitted, NarrowestNarrowMm);
    }

    /// <summary>
    /// The narrowest Code39 is read at, about three dots on a 300 dpi head. Below
    /// this a scanner stops reading the code, which looks exactly like a label with
    /// no barcode on it.
    /// </summary>
    private const float NarrowestNarrowMm = 0.19f;

    /// <summary>
    /// The specification line under the barcode.
    ///
    /// Set to a size that fits the stock on one line, measured rather than guessed.
    /// A fixed 9 point line is a comfortable read on a 106mm label and overflows it,
    /// and an overflowing line wraps: two lines take the room the fault line needs,
    /// so the faults are squeezed to nothing at the bottom of the label. The faults
    /// going missing is worse than the specification being set a size smaller.
    /// </summary>
    private static void TextLine(IContainer container, string text, LabelLayout layout, float points, string? family)
    {
        container.PaddingHorizontal(layout.MarginMm)
            .AlignCenter().AlignMiddle()
            .ScaleToFit()
            .Text(text)
            .FontFamily(family ?? "Helvetica")
            .FontSize(points)
            .SemiBold()
            .FontColor(Colors.Black);
    }

    /// <summary>
    /// The lines below the specification: the charge count, the faults, the locks.
    ///
    /// The locks are set larger than everything else on the label, because they are
    /// the one fault that costs a shop the sale and the one an operator reads last
    /// if it is the same size as the rest. A FRP-locked phone that the next owner
    /// activates wipes itself, and this line is the last place that could have said
    /// so.
    /// </summary>
    private static void Detail(
        IContainer container, string text, LabelLayout layout, float points, string? family)
    {
        container.PaddingHorizontal(layout.MarginMm)
            .AlignCenter().AlignMiddle()
            .ScaleToFit()
            .Text(text)
            .FontFamily(family ?? "Helvetica")
            .FontSize(points)
            .SemiBold()
            .FontColor(Colors.Black);
    }

    /// <summary>
    /// The largest size at which this text still fits across the stock on one line.
    /// </summary>
    private static float FittedSize(string text, LabelLayout layout, float largest, float smallest)
    {
        if (text.Length == 0) return smallest;

        float available = layout.WidthMm - (2 * layout.MarginMm);
        float perPoint = available / (text.Length * AdvanceMmPerPoint);

        return Math.Clamp(perPoint, smallest, largest);
    }

    /// <summary>
    /// The one size the whole text block is set at, taken from its longest line.
    /// </summary>
    /// <remarks>
    /// A line is allowed to wrap: forty characters of specification do not fit
    /// across a 106mm label at a size anybody can read, and the original template
    /// had DYMO shrink the same line until it did. Set per line, the block reads as
    /// three different pieces of paper. Set once from the longest line, it reads as
    /// one label with a wrapped first line.
    /// </remarks>
    private static float BlockSize(string first, string second, LabelLayout layout)
    {
        int longest = Math.Max(first.Length, second.Length);
        if (longest == 0) return 8f;

        float available = layout.WidthMm - (2 * layout.MarginMm);
        float perPoint = available / (longest * AdvanceMmPerPoint);

        // Half the width, so the longest line fills it and wraps rather than
        // filling it exactly: a line measured to land on the edge is a line that
        // overflows by a hair on the next machine.
        return Math.Clamp(perPoint * 0.5f, 5f, 9f);
    }

    /// <summary>How wide one character is per point of type, in millimetres.</summary>
    private const float AdvanceMmPerPoint = 0.35f;
}
