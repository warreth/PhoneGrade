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
    /// <summary>
    /// Writes one label, on the stock and in the barcode mode the operator chose.
    /// </summary>
    /// <param name="path">Where the PDF goes.</param>
    /// <param name="fields">What the inspection read.</param>
    /// <param name="layout">
    /// The stock and where things sit on it. Omitted means the DYMO address label,
    /// which is what the shipped template was drawn for and the roll in nearly
    /// every phone shop.
    /// </param>
    /// <param name="mode">
    /// What the barcode carries. Identifiers only by default, because a barcode
    /// that does not scan to a serial is a barcode that gets a phone mislaid.
    /// </param>
    public static void Write(
        string path,
        LabelFields fields,
        LabelLayout? layout = null,
        LabelBarcodeMode mode = LabelBarcodeMode.Identifier)
    {
        // Done here rather than left to the caller. Setting the licence up is one
        // line but it is the one line that has to happen before the document is
        // drawn, and a writer that throws without it is a writer every caller has
        // to remember to prepare first.
        ReportFonts.Ensure();

        LabelLayout size = layout ?? LabelLayout.Address;

        // A code that will not fit at a width a scanner reads is not drawn. It goes
        // on the label as words instead, on the two small multi purpose stocks,
        // where a fifteen digit identifier is simply wider than the paper. Squeezing
        // it in would produce grey rather than bars, and an operator who scans that
        // and gets nothing believes the phone has no identifier.
        var (barred, spelled) = new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields))
            .On(size.Stock, mode, fields.IsIdentifiable);

        string spec = LabelLayout.TextLine(fields);
        string detail = LabelLayout.DetailLine(fields);
        string locks = LabelLayout.LockLine(fields);

        // The values that could not be barcoded are printed instead, above the
        // specification, so a label with no barcode on it still says which device it
        // is. A label that cannot be scanned has to be readable by eye, or it says
        // nothing at all.
        string written = string.Join("  ", spelled);

        // How many lines the text block really is, taken from the lines that have
        // something on them. A clean phone with one line can be set much larger than
        // a phone with three, which is the difference between a label that fills its
        // paper and one that huddles in the middle of it.
        var block = new[] { written, spec, detail, locks }.Where(line => line.Length > 0).ToList();

        float body = LabelType.BlockSize(block, size, barred.Count);
        float lockSize = LabelType.LineSize(locks, size, barred.Count, body * LabelType.LocksLargerThanBody, body);

        Document.Create(document =>
        {
            document.Page(page =>
            {
                // The page is the paper, margins and all, so that a label written
                // here is the same piece of stock a DYMO takes. Drawing on the whole
                // rectangle puts the words where the printer cannot reach them, and
                // they come off the label while still being in the file.
                //
                // Points, not millimetres. A page size given in millimetres is read
                // as millimetres of paper divided by nothing at all, so an 89mm label
                // comes out as a 31mm page with the same words on it: the file is
                // valid, it looks right, and it prints at a third of the size of the
                // roll it was drawn for. Every measurement on this label is in
                // millimetres because that is the language of label stock, and the
                // conversions are the one place the two have to meet.
                page.Size(new PageSize(Points(size.PaperWidthMm), Points(size.PaperHeightMm)));
                page.PageColor(Colors.White);

                string? family = ReportFonts.Resolve();

                // The margin the printer cannot reach is a padding on the content
                // rather than a page margin, and there is one content layer rather
                // than two: QuestPDF takes a page margin once and lets content be
                // defined once, and asking for a second layer fails the whole
                // export rather than quietly drawing somewhere else.
                page.Content().PaddingTop(Points(size.Stock.TopMarginMm))
                    .PaddingBottom(Points(size.Stock.BottomMarginMm))
                    .PaddingHorizontal(Points(size.Stock.SideMarginMm))
                    .Column(column =>
                {
                    column.Spacing(0);

                    // No height is set on any item below. An item that believes it
                    // owns a fixed slice of the paper hands the layout engine two
                    // requirements it cannot both meet, and QuestPDF answers that by
                    // dropping the text rather than by complaining: the label comes
                    // out with bars on it and no words, which reads as a blank label.
                    // Each barcode is placed by its own offset from the top of the
                    // printable area, measured from the top rather than accumulated,
                    // so a second barcode cannot drift a fraction of a millimetre per
                    // label because of the one before it.
                    for (int index = 0; index < barred.Count; index++)
                    {
                        string payload = barred[index];

                        // Only the gap. Every band is the same height and the column
                        // stacks them with no spacing between them, so the second band
                        // already starts where the first one ended and the offset from
                        // the top of the label has been accounted for. Padding it by
                        // its own offset as well put the second barcode lower than its
                        // place and pushed the words off the bottom of the label.
                        column.Item()
                            .PaddingTop(Points(index == 0 ? 0f : LabelLayout.GapMm))
                            .Element(container => Barcode(container, payload, size, barred.Count, family));
                    }

                    // The text, below however many barcodes there were. Every line at
                    // one size, worked out from the longest: sized line by line the
                    // block reads as three pieces of paper rather than one label.
                    string first = block[0];

                    // Sat in the middle of the room that is left rather than against
                    // the top of it. A label is read on a shelf, and one whose words
                    // stop a third of the way up its paper looks like a label that
                    // ran out of something.
                    float room = size.TextHeightMm(barred.Count);
                    float taken = block.Count * body + (block.Contains(locks) && locks.Length > 0
                        ? body * (LabelType.LocksLargerThanBody - 1)
                        : 0);
                    float slack = Math.Max(0f, (room - taken * LabelType.LineHeightInMm) / 2f);

                    float above = barred.Count == 0 ? 0f : LabelLayout.GapMm + slack;

                    column.Item().PaddingTop(Points(above)).AlignCenter().AlignMiddle()
                        .Element(container => TextLine(container, first, body, family));

                    for (int index = 1; index < block.Count; index++)
                    {
                        string line = block[index];
                        if (line.Length == 0) continue;

                        column.Item().AlignCenter().AlignMiddle()
                            .Element(container => Detail(container, line,
                                line == locks ? lockSize : body, family));
                    }
                });
            });
        }).GeneratePdf(path);
    }

    /// <summary>
    /// One barcode: the bars, and the value under them.
    ///
    /// Code39 is drawn here rather than handed to a barcode library because it is
    /// nine elements of fixed widths per character, and a dependency that draws it
    /// would be a hundred times the size of the twenty lines that do. The widths
    /// come from the standard's own table: nine patterns of wide and narrow, three
    /// of them wide, and a narrow gap between one character and the next.
    ///
    /// The caption is what a barcode is read against. A code nobody can read back
    /// is worse than no code, because the operator believes it was scanned.
    /// </summary>
    private static void Barcode(
        IContainer container, string value, LabelLayout size, int count, string? family)
    {
        // The bar width is rounded to whole dots before anything else uses it, so
        // that the picture and the box it is handed are the same width. Rounding
        // twice, once for the picture and once for the box, is how a barcode ends
        // up drawn at one size inside a box of another: it sits in the corner of the
        // band, a sixth of the width it was measured at, because the layout engine
        // scaled it to fit. Rounded down rather than to the nearest, so the code is
        // never wider than the paper it was measured against.
        int narrowDots = PngWriter.DotsFor(LabelBarcode.NarrowMm(value, size.WidthMm));
        float narrow = PngWriter.MmOf(narrowDots);

        float band = size.BarcodeBandMm(count);

        // Two thirds of the band for the bars and the rest for the caption, which
        // is about a two and a half millimetre line at six point.
        float caption = Math.Min(3f, band * 0.3f);
        float bars = band - caption;

        container.Column(column =>
        {
            // The physical size is set rather than left to fill the column. Left to
            // fill, the image is stretched across the whole band and a barcode comes
            // out as a black rectangle, which scans as nothing and looks like a
            // printer fault rather than a drawing one.
            column.Item().Height(Points(bars)).AlignCenter()
                .Element(box => box
                    .Width(Points(LabelBarcode.WidthMm(value, narrow)))
                    .Height(Points(bars))
                    .Image(Bars(value, narrowDots, bars)));

            column.Item().AlignCenter().AlignMiddle().Text(value)
                .FontFamily(family ?? "Helvetica")
                .FontSize(CaptionPoint)
                .FontColor(Colors.Black);
        });
    }

    /// <summary>
    /// Millimetres as points, which is the unit a PDF page is measured in.
    /// </summary>
    /// <remarks>
    /// There are 72 points to the inch and 25.4 millimetres to it. A label is
    /// described in millimetres throughout, because that is how the roll is sold
    /// and how the DYMO template measures, so this is where the two meet. Getting it
    /// wrong is silent: the document still draws, it is still valid, and it comes
    /// out at 72/25.4 of the size it should be.
    /// </remarks>
    private static float Points(float millimetres) => millimetres * PointsPerMm;

    private const float PointsPerMm = 72f / 25.4f;

    /// <summary>The size the value under the barcode is set at.</summary>
    private const float CaptionPoint = 5.5f;

    /// <summary>
    /// The bars as one picture.
    /// </summary>
    /// <remarks>
    /// Painted rather than laid out, and that is the whole point. Laid out as a
    /// hundred and sixty-nine separate boxes, the label's words sat on a knife
    /// edge: making the code a third of a millimetre narrower was enough for the
    /// layout engine to decide the caption under it did not fit, and it then
    /// printed the barcode and nothing else, with every word still in the file.
    /// An image has one width and takes it, so there is nothing left to negotiate.
    ///
    /// The bars are drawn at the same dots per millimetre the page is measured in,
    /// and the picture says so, so the layout engine has no reason to rescale it.
    ///
    /// The quiet zones are painted into the picture rather than left as bare paper
    /// around it, because the picture is given an exact width and the layout engine
    /// will not leave the margin a scanner needs unless it is asked to.
    /// </remarks>
    private static byte[] Bars(string value, int narrowDots, float heightMm)
    {
        int high = PngWriter.DotsFor(heightMm);

        return PngWriter.Barcode(
            Code39.Elements(value).ToList(), narrowDots,
            quietEachSide: LabelBarcode.QuietZoneUnits * narrowDots, high);
    }

    /// <summary>
    /// The specification line under the barcode.
    ///
    /// Set to a size that fits the stock, measured rather than guessed, and set at
    /// that size exactly. The whole block is sized once from its longest line, so
    /// the label reads as one label. What it must not do is let each line find its
    /// own size, which is what scaling every line to fill whatever room is left
    /// does: on a three line label that came out as a specification at nine point,
    /// the faults at four, and the locks in between, and the locks are the one line
    /// a shop cannot afford to have as the smallest thing on the paper.
    /// </summary>
    private static void TextLine(IContainer container, string text, float points, string? family)
    {
        container.AlignCenter().AlignMiddle()
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
    private static void Detail(IContainer container, string text, float points, string? family)
    {
        container.AlignCenter().AlignMiddle()
            .Text(text)
            .FontFamily(family ?? "Helvetica")
            .FontSize(points)
            .SemiBold()
            .FontColor(Colors.Black);
    }

}
