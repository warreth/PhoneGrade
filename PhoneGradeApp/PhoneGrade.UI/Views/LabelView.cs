using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using PhoneGrade.Core;

namespace PhoneGrade.UI.Views;

/// <summary>
/// Draws a label sheet: the same plate the label PDF is written from.
/// </summary>
/// <remarks>
/// One control, used by the finish panel and by the label settings, so a shop that
/// configures its label and a shop that prints one are looking at the same drawing
/// from the same code. Everything it draws comes out of <see cref="LabelPlate"/>:
/// which values are on the label, how many barcodes there are, the size of every
/// line and the empty paper above the content. The measurements are the same
/// millimetres and points the PDF writer uses, scaled to fit the room the control
/// is given.
///
/// A control rather than a XAML tree, for the same reason the barcode is one: the
/// arrangement changes shape with the variant, and three hand kept copies of a
/// label layout are three chances to draw a label the file does not contain.
/// </remarks>
public sealed class LabelView : Control
{
    /// <summary>What the sheet carries. Null draws an empty sheet.</summary>
    public static readonly StyledProperty<LabelPlate?> PlateProperty =
        AvaloniaProperty.Register<LabelView, LabelPlate?>(nameof(Plate));

    public LabelPlate? Plate
    {
        get => GetValue(PlateProperty);
        set => SetValue(PlateProperty, value);
    }

    /// <summary>The stock the sheet is the shape of.</summary>
    public static readonly StyledProperty<LabelLayout?> LayoutProperty =
        AvaloniaProperty.Register<LabelView, LabelLayout?>(nameof(Layout));

    public LabelLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>
    /// Whether the sheet had to be drawn smaller than life size to fit its column.
    /// </summary>
    /// <remarks>
    /// A direct property rather than a styled one: it is an answer about the last
    /// layout, not an input. The panel says so under the sheet, because a scaled
    /// preview keeps the right shape and stops being a picture of the physical label.
    /// </remarks>
    public static readonly DirectProperty<LabelView, bool> SheetScaledProperty =
        AvaloniaProperty.RegisterDirect<LabelView, bool>(nameof(SheetScaled), view => view.SheetScaled);

    private bool _sheetScaled;
    public bool SheetScaled
    {
        get => _sheetScaled;
        private set => SetAndRaise(SheetScaledProperty, ref _sheetScaled, value);
    }

    static LabelView()
    {
        AffectsMeasure<LabelView>(PlateProperty, LayoutProperty);
        AffectsRender<LabelView>(PlateProperty, LayoutProperty);
    }

    /// <summary>
    /// How finely the sheet is drawn, chosen so a label fills the room its column
    /// has rather than sitting small in the middle of it.
    /// </summary>
    /// <remarks>
    /// At 96 dpi an 89mm address label came out 336 pixels wide in a column with
    /// room for 400, and 105 pixels high, which is not enough for a barcode band
    /// and three lines of words: the locks line came off the bottom. The panel
    /// already says the sheet is not life size whenever it does not fit, so drawing
    /// it as large as it will go is the better of the two, and the shape is
    /// unaffected because every measurement is scaled by the same figure.
    /// </remarks>
    private const double PixelsPerMm = 120.0 / 25.4;

    /// <summary>How many millimetres a point of type is, which is 72 to the inch.</summary>
    private const double MillimetresPerPoint = 25.4 / 72.0;

    /// <summary>How much taller the box around a line of type is than the type itself.</summary>
    private const double LineBoxOverFontSize = 1.35;

    /// <summary>The size the value under the barcode is set at, as the PDF sets it.</summary>
    private const double CaptionPoint = 5.5;

    private LabelLayout Sheet => Layout ?? LabelLayout.Address;

    protected override Size MeasureOverride(Size availableSize)
    {
        LabelLayout sheet = Sheet;
        double wanted = sheet.PaperWidthMm * PixelsPerMm;
        double height = sheet.PaperHeightMm * PixelsPerMm;

        if (availableSize.Width is > 0 and < double.PositiveInfinity && wanted > availableSize.Width)
        {
            height *= availableSize.Width / wanted;
            wanted = availableSize.Width;
        }

        return new Size(wanted, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LabelLayout sheet = Sheet;
        double design = sheet.PaperWidthMm * PixelsPerMm;
        double scale = design > 0 ? Math.Min(1, finalSize.Width / design) : 1;
        SheetScaled = finalSize.Width > 0 && finalSize.Width < design - 0.5;

        // The height follows the width, so the sheet keeps the shape of the stock
        // whatever the parent does with the width it was given.
        return new Size(finalSize.Width, sheet.PaperHeightMm * PixelsPerMm * scale);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        LabelLayout sheet = Sheet;
        LabelPlate? plate = Plate;

        // The sheet itself, whichever theme is on: a label is white paper, and the
        // preview has to show the ink on the paper rather than the ink on a dark card.
        context.FillRectangle(Brushes.White, new Rect(0, 0, bounds.Width, bounds.Height));

        if (plate is null || !plate.HasContent) return;

        double design = sheet.PaperWidthMm * PixelsPerMm;
        double scale = design > 0 ? bounds.Width / design : 1;
        if (scale <= 0) return;

        double side = sheet.Stock.SideMarginMm * PixelsPerMm * scale;
        double top = sheet.Stock.TopMarginMm * PixelsPerMm * scale;
        double printable = sheet.WidthMm * PixelsPerMm * scale;
        double center = bounds.Width / 2;

        // The barcodes, each in its own band, placed by the same offsets the PDF
        // uses so the words sit where the printed label puts them.
        int count = plate.BarcodeCount;
        double bandMm = sheet.BarcodeBandMm(count);
        double y = top + (plate.TopOffsetMm * PixelsPerMm * scale);

        foreach (string payload in plate.Barred)
        {
            double band = bandMm * PixelsPerMm * scale;
            double caption = Math.Min(3, bandMm * 0.3) * PixelsPerMm * scale;
            double bars = band - caption;

            DrawBarcode(context, payload, plate.Symbology, printable, y, bars, side);
            DrawText(context, payload, CaptionPoint, bold: false, Brushes.Black,
                new Rect(0, y + bars, bounds.Width, caption), TextAlignment.Center, scale);

            y += band + (LabelLayout.GapMm * PixelsPerMm * scale);
        }

        double textTop = y;

        if (plate.Variant == LabelVariant.GradeBlock)
        {
            DrawGradeBlock(context, plate, sheet, scale, textTop, side, printable);
            return;
        }

        foreach (LabelLine line in plate.Lines)
        {
            double size = line.PointSize * MillimetresPerPoint * PixelsPerMm * scale;
            double height = size * LineBoxOverFontSize;

            if (line.Role == LabelLineRole.Locks && plate.LocksInverted)
            {
                DrawInverted(context, line.Text, size, center, textTop, scale);
            }
            else
            {
                DrawText(context, line.Text, line.PointSize, line.Bold, Brushes.Black,
                    new Rect(0, textTop, bounds.Width, height), TextAlignment.Center, scale);
            }

            textTop += height;
        }
    }

    /// <summary>
    /// The grade block arrangement: the letter in a box with the values beside it
    /// and the locks on their own line below.
    /// </summary>
    private void DrawGradeBlock(
        DrawingContext context, LabelPlate plate, LabelLayout sheet, double scale,
        double top, double side, double printable)
    {
        double y = top;

        // The spelled values, when a barcode would not fit, sit above everything as
        // they do in the other arrangements.
        foreach (LabelLine spelled in plate.Lines.Where(line => line.Role == LabelLineRole.Spelled))
        {
            double size = spelled.PointSize * MillimetresPerPoint * PixelsPerMm * scale;
            DrawText(context, spelled.Text, spelled.PointSize, false, Brushes.Black,
                new Rect(0, y, Bounds.Width, size * LineBoxOverFontSize), TextAlignment.Center, scale);
            y += size * LineBoxOverFontSize;
        }

        double box = LabelPlate.GradeBoxWidthMm * PixelsPerMm * scale;
        double boxHeight = plate.GradeBoxHeightMm * PixelsPerMm * scale;

        var beside = plate.Lines.Where(line =>
            line.Role is not (LabelLineRole.Locks or LabelLineRole.Spelled)).ToList();

        double besideHeight = beside.Sum(line =>
            line.PointSize * MillimetresPerPoint * PixelsPerMm * scale * LineBoxOverFontSize);
        double rowHeight = Math.Max(boxHeight, besideHeight);

        var boxRect = new Rect(side, y + ((rowHeight - boxHeight) / 2), box, boxHeight);
        context.DrawRectangle(null, new Pen(Brushes.Black, Math.Max(1, 1.5 * scale)), boxRect);

        double gradeSize = plate.GradePoint * MillimetresPerPoint * PixelsPerMm * scale;
        DrawText(context, plate.Grade, plate.GradePoint, bold: true, Brushes.Black,
            new Rect(boxRect.X, boxRect.Y + ((boxRect.Height - gradeSize * LineBoxOverFontSize) / 2),
                boxRect.Width, gradeSize * LineBoxOverFontSize),
            TextAlignment.Center, scale);

        double infoX = boxRect.Right + (2 * PixelsPerMm * scale);
        double infoWidth = Math.Max(0, side + printable - infoX);
        double infoY = y + ((rowHeight - besideHeight) / 2);

        foreach (LabelLine line in beside)
        {
            double size = line.PointSize * MillimetresPerPoint * PixelsPerMm * scale;
            DrawText(context, line.Text, line.PointSize, line.Bold, Brushes.Black,
                new Rect(infoX, infoY, infoWidth, size * LineBoxOverFontSize), TextAlignment.Left, scale);
            infoY += size * LineBoxOverFontSize;
        }

        y += rowHeight;

        LabelLine? locks = plate.Lines.FirstOrDefault(line => line.Role == LabelLineRole.Locks);
        if (locks is not null)
        {
            y += 1 * PixelsPerMm * scale;
            double size = locks.PointSize * MillimetresPerPoint * PixelsPerMm * scale;
            DrawInverted(context, locks.Text, size, Bounds.Width / 2, y, scale);
        }
    }

    /// <summary>
    /// The locks white on black. A thermal label cannot print colour, so emphasis
    /// has to come out of black, and a solid band around the one line that can cost
    /// a shop the sale is what the paper has to offer.
    /// </summary>
    private void DrawInverted(
        DrawingContext context, string text, double sizePx, double centerX, double topY, double scale)
    {
        FormattedText formatted = Format(text, sizePx, bold: true, Brushes.White);
        double paddingX = 1.5 * PixelsPerMm * scale;
        double paddingY = 0.4 * PixelsPerMm * scale;
        var rect = new Rect(
            centerX - (formatted.Width / 2) - paddingX,
            topY,
            formatted.Width + (2 * paddingX),
            formatted.Height + (2 * paddingY));

        context.FillRectangle(Brushes.Black, rect);
        context.DrawText(formatted, new Point(rect.X + paddingX, rect.Y + paddingY));
    }

    /// <summary>One line of type, in a box, on the alignment the arrangement asks for.</summary>
    private void DrawText(
        DrawingContext context, string text, double pointSize, bool bold, IBrush brush,
        Rect box, TextAlignment alignment, double scale = 1)
    {
        double sizePx = pointSize * MillimetresPerPoint * PixelsPerMm * scale;
        FormattedText formatted = Format(text, sizePx, bold, brush);

        double x = alignment == TextAlignment.Center
            ? box.X + ((box.Width - formatted.Width) / 2)
            : box.X;

        // Vertically centred in the line box, so a larger line and a smaller one sit
        // on the same block rather than each against its own top edge.
        double y = box.Y + ((box.Height - formatted.Height) / 2);

        context.DrawText(formatted, new Point(Math.Max(box.X, x), y));
    }

    private FormattedText Format(string text, double sizePx, bool bold, IBrush brush)
    {
        FontFamily family = TextElement.GetFontFamily(this) ?? FontFamily.Default;
        var typeface = new Typeface(family, FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal);

        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, Math.Max(1, sizePx), brush);
    }

    /// <summary>
    /// The bars of one barcode, out of the same table the label file is drawn from.
    /// </summary>
    private static void DrawBarcode(
        DrawingContext context, string value, LabelCodeSymbology symbology,
        double printable, double top, double height, double left)
    {
        BarcodeElement[] elements = BarcodeView.Barcode(value, symbology);
        if (elements.Length == 0) return;

        double narrow = BarcodeView.FitWidth([.. elements.Select(element => element.Units)], printable);
        double x = left;

        foreach (BarcodeElement element in elements)
        {
            double wide = element.Units * narrow;
            if (element.IsBar)
                context.DrawRectangle(Brushes.Black, null, new Rect(x, top, wide, height));
            x += wide;
        }
    }
}
