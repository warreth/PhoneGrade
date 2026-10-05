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
                        column.Item()
                            .Height(size.BarcodeYmm + size.BarcodeHeightMm)
                            .PaddingTop(size.BarcodeYmm)
                            .Element(container => Barcode(container, barcode, size, family));
                    }

                    // The rest of the paper, with the text centred in it rather
                    // than pinned to an offset from the barcode. An offset is a
                    // measurement that has to stay true for every stock size, and
                    // a stock size it does not stay true for pushes the text off
                    // the bottom of the label.
                    column.Item()
                        .PaddingTop(Math.Max(0f, size.TextYmm - size.BarcodeYmm - size.BarcodeHeightMm))
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
            column.Item().Row(row =>
            {
                row.Spacing(0);
                foreach (bool isBar in elements)
                {
                    row.ConstantItem(isBar ? narrow * 3f : narrow)
                        .Height(barHeight)
                        .Element(bar => bar.Background(isBar ? Colors.Black : Colors.Transparent));
                }
            });

            // The value under the bars. A barcode nobody can read back is worse
            // than no barcode, because the operator believes it was scanned.
            column.Item().AlignCenter().Text(value)
                .FontFamily(family ?? "Helvetica").FontSize(6).FontColor(Colors.Black);
        });
    }

    /// <summary>
    /// The width of one narrow element, in millimetres, chosen so a barcode of
    /// this many elements fills the label and no more.
    ///
    /// It is never narrower than the smallest a thermal head can print, because a
    /// barcode whose bars are below the head's resolution scans as nothing at all,
    /// which looks identical to a barcode that works.
    /// </summary>
    private static float NarrowBarWidth(IReadOnlyList<bool> elements, LabelLayout size)
    {
        float available = size.WidthMm - (2 * size.MarginMm);
        int units = 0;
        foreach (bool isBar in elements) units += isBar ? 3 : 1;

        float fitted = units > 0 ? available / units : available;
        return Math.Max(fitted, MinimumNarrowMm);
    }

    /// <summary>About a third of a millimetre, the narrowest a label head prints.</summary>
    private const float MinimumNarrowMm = 0.28f;

    /// <summary>The one line of specifications under the barcode.</summary>
    private static void TextLine(IContainer container, string text, LabelLayout layout, string? family)
    {
        container.PaddingHorizontal(layout.MarginMm)
            .AlignCenter().AlignMiddle()
            .Text(text)
            .FontFamily(family ?? "Helvetica")
            .FontSize(9)
            .SemiBold()
            .FontColor(Colors.Black);
    }
}
