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
        string text = size.TextLine(fields);
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

                    // The rest of the paper, with the text centred in it and no
                    // height of its own. A height here is what fought with the
                    // barcode band in the first place: two items that both think
                    // they own the paper is a layout the engine cannot always
                    // satisfy, and it answers by dropping the text rather than by
                    // complaining.
                    column.Item()
                        .PaddingTop(Math.Max(1f, size.TextYmm - size.BarcodeYmm - size.BarcodeHeightMm))
                        .AlignCenter().AlignMiddle()
                        .Element(container => TextLine(container, text, size, family));
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
    /// Scaled to whatever room the stock has rather than set at a size chosen for
    /// the widest one, and allowed to wrap rather than be cut off. A 9 point line
    /// is a comfortable read on a 106mm label and does not fit across an 89mm one,
    /// where a fixed size is cut off after "128GB" and the half that goes missing
    /// is the half carrying the grade, the battery and the payment method.
    ///
    /// Wrapping costs a second line on the narrow stocks, which is the lesser of
    /// the two faults: the label is a different shape, and everything on it is
    /// still readable. A label that is cut off is a label that lies.
    /// </summary>
    private static void TextLine(IContainer container, string text, LabelLayout layout, string? family)
    {
        container.PaddingHorizontal(layout.MarginMm)
            .AlignCenter().AlignMiddle()
            .ScaleToFit()
            .Text(text)
            .FontFamily(family ?? "Helvetica")
            .FontSize(9)
            .SemiBold()
            .FontColor(Colors.Black);
    }
}
