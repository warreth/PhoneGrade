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
/// Everything drawn here comes from <see cref="LabelPlate"/>: the barcode payloads,
/// the lines, their sizes and the empty paper above the content. The on screen
/// preview reads the same plate, so a preview and a printed label cannot disagree
/// about what is on the paper.
/// </summary>
public static class LabelPdfWriter
{
    /// <summary>
    /// Writes one label, on the stock, in the barcode mode and in the arrangement
    /// the operator chose.
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
    /// <param name="symbology">
    /// Which symbology the bars are drawn in. Code39 by default, which is what
    /// every shop scanner reads and what the DYMO template declares.
    /// </param>
    /// <param name="variant">
    /// How the label is arranged. The information is the same whichever is chosen;
    /// what changes is what the eye meets first.
    /// </param>
    /// <param name="cyclesMinimum">
    /// The charge count floor the shop set, so a number that says nothing is left
    /// off here exactly as it is left off the preview.
    /// </param>
    public static void Write(
        string path,
        LabelFields fields,
        LabelLayout? layout = null,
        LabelBarcodeMode mode = LabelBarcodeMode.Identifier,
        LabelCodeSymbology symbology = LabelCodeSymbology.Code39,
        LabelVariant variant = LabelVariant.Clean,
        int cyclesMinimum = 0)
    {
        // Done here rather than left to the caller. Setting the licence up is one
        // line but it is the one line that has to happen before the document is
        // drawn, and a writer that throws without it is a writer every caller has
        // to remember to prepare first.
        ReportFonts.Ensure();

        LabelLayout size = layout ?? LabelLayout.Address;
        LabelPlate plate = LabelPlate.Build(fields, size, mode, symbology, variant, cyclesMinimum);

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
                    for (int index = 0; index < plate.Barred.Count; index++)
                    {
                        string payload = plate.Barred[index];

                        // Only the gap. Every band is the same height and the column
                        // stacks them with no spacing between them, so the second band
                        // already starts where the first one ended and the offset from
                        // the top of the label has been accounted for. Padding it by
                        // its own offset as well put the second barcode lower than its
                        // place and pushed the words off the bottom of the label.
                        column.Item()
                            .PaddingTop(Points(index == 0 ? plate.TopOffsetMm : LabelLayout.GapMm))
                            .Element(container => Barcode(
                                container, payload, size, plate.Barred.Count, family, symbology));
                    }

                    // The text, below however many barcodes there were. The plate
                    // decided what each line says and how large it is; this only
                    // paints them.
                    float above = plate.Barred.Count == 0 ? 0f : LabelLayout.GapMm + plate.TopOffsetMm;

                    column.Item().PaddingTop(Points(above))
                        .Element(container => Block(container, plate, family));
                });
            });
        }).GeneratePdf(path);
    }

    /// <summary>
    /// The text of one label, in the arrangement the plate describes.
    /// </summary>
    private static void Block(IContainer container, LabelPlate plate, string? family)
    {
        if (plate.Variant == LabelVariant.GradeBlock)
        {
            GradeBlock(container, plate, family);
            return;
        }

        container.Column(column =>
        {
            column.Spacing(0);
            foreach (LabelLine line in plate.Lines)
                column.Item().Element(item => Line(item, line, plate, family));
        });
    }

    /// <summary>
    /// The grade block arrangement: the grade as a bordered block with the values
    /// beside it and the locks on their own line below.
    /// </summary>
    private static void GradeBlock(IContainer container, LabelPlate plate, string? family)
    {
        container.Column(column =>
        {
            column.Spacing(0);

            // The spelled values, when a barcode would not fit, sit above everything
            // as they do in the other arrangements.
            foreach (LabelLine spelled in plate.Lines.Where(line => line.Role == LabelLineRole.Spelled))
                column.Item().Element(item => Line(item, spelled, plate, family));

            column.Item().Row(row =>
            {
                row.ConstantItem(Points(LabelPlate.GradeBoxWidthMm))
                    .Height(Points(plate.GradeBoxHeightMm))
                    .Border(1.5f)
                    .BorderColor(Colors.Black)
                    .AlignCenter()
                    .AlignMiddle()
                    .Text(plate.Grade)
                    .FontFamily(family ?? "Helvetica")
                    .FontSize(plate.GradePoint)
                    .Bold()
                    .FontColor(Colors.Black);

                row.RelativeItem().PaddingLeft(Points(2)).AlignMiddle().Column(info =>
                {
                    info.Spacing(0);
                    foreach (LabelLine line in plate.Lines.Where(line =>
                                 line.Role is not (LabelLineRole.Locks or LabelLineRole.Spelled)))
                        info.Item().AlignLeft().Element(item => Line(item, line, plate, family));
                });
            });

            LabelLine? locks = plate.Lines.FirstOrDefault(line => line.Role == LabelLineRole.Locks);
            if (locks is not null)
                column.Item().PaddingTop(Points(1)).Element(item => Line(item, locks, plate, family));
        });
    }

    /// <summary>
    /// One line. The locks are drawn white on black when the plate says so: a
    /// thermal label cannot print colour, so emphasis has to come out of black, and
    /// a solid band around the one line that can cost a shop the sale is what the
    /// paper has to offer.
    /// </summary>
    private static void Line(IContainer container, LabelLine line, LabelPlate plate, string? family)
    {
        if (line.Role == LabelLineRole.Locks && plate.LocksInverted)
        {
            container.AlignCenter().Element(box => box
                .Background(Colors.Black)
                .PaddingVertical(Points(0.35f))
                .PaddingHorizontal(Points(1.4f))
                .Text(line.Text)
                .FontFamily(family ?? "Helvetica")
                .FontSize(line.PointSize)
                .Bold()
                .FontColor(Colors.White));
            return;
        }

        Styled(container.AlignCenter().Text(line.Text), line, family);
    }

    /// <summary>The family, size, colour and weight every line of the label is set in.</summary>
    private static TextSpanDescriptor Styled(TextSpanDescriptor text, LabelLine line, string? family)
    {
        text.FontFamily(family ?? "Helvetica").FontSize(line.PointSize).FontColor(Colors.Black);
        if (line.Bold) text.Bold();
        return text;
    }

    /// <summary>
    /// One barcode: the bars, and the value under them.
    ///
    /// The bars are drawn here rather than handed to a barcode library because they
    /// are a list of widths out of the standard's own table, and a dependency that
    /// draws them would be a hundred times the size of the twenty lines that do. The
    /// symbology arrives as a flag and never as a branch: everything below works in
    /// units, and which units they are was decided by whoever built the list.
    ///
    /// The caption is what a barcode is read against. A code nobody can read back
    /// is worse than no code, because the operator believes it was scanned.
    /// </summary>
    private static void Barcode(
        IContainer container, string value, LabelLayout size, int count, string? family,
        LabelCodeSymbology symbology)
    {
        // The bar width is rounded to whole dots before anything else uses it, so
        // that the picture and the box it is handed are the same width. Rounding
        // twice, once for the picture and once for the box, is how a barcode ends
        // up drawn at one size inside a box of another: it sits in the corner of the
        // band, a sixth of the width it was measured at, because the layout engine
        // scaled it to fit. Rounded down rather than to the nearest, so the code is
        // never wider than the paper it was measured against.
        int narrowDots = PngWriter.DotsFor(LabelBarcode.NarrowMm(value, symbology, size.WidthMm));
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
                    .Width(Points(LabelBarcode.WidthMm(value, symbology, narrow)))
                    .Height(Points(bars))
                    .Image(Bars(value, symbology, narrowDots, bars)));

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
    private static byte[] Bars(
        string value, LabelCodeSymbology symbology, int narrowDots, float heightMm)
    {
        int high = PngWriter.DotsFor(heightMm);

        return PngWriter.Barcode(
            LabelBarcode.ElementsOf(value, symbology).ToList(), narrowDots,
            quietEachSide: LabelBarcode.QuietZoneUnits * narrowDots, high);
    }
}
