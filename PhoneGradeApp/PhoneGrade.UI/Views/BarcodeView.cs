using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhoneGrade.Core;

namespace PhoneGrade.UI.Views;

/// <summary>
/// Draws the label's barcode as bars.
///
/// A control rather than a run of text: a barcode made of characters is a drawing
/// of the idea of a barcode, and on the preview sheet it is the one thing an
/// operator has to decide whether to trust. These are the same elements, in the
/// same order and at the same ratio as the ones the label PDF draws, out of the same
/// table, so an identifier that has been rewritten shows up rewritten here.
///
/// The pattern is read from the value, not from the width. A control that scaled a
/// fixed pattern would look right at one size and be wrong at another, and a narrow
/// label is one of the sizes an operator chooses.
///
/// Which table is asked of is the operator's choice and not this control's, because
/// a preview that quietly drew Code39 for a Code128 label would be a preview that
/// looks like a barcode and is not the one the printer prints.
/// </summary>
public sealed class BarcodeView : Control
{
    /// <summary>What to draw. Already in the form the symbology carries it in.</summary>
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<BarcodeView, string?>(nameof(Value));

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Which symbology the bars come out of.</summary>
    public static readonly StyledProperty<LabelCodeSymbology> SymbologyProperty =
        AvaloniaProperty.Register<BarcodeView, LabelCodeSymbology>(nameof(Symbology));

    public LabelCodeSymbology Symbology
    {
        get => GetValue(SymbologyProperty);
        set => SetValue(SymbologyProperty, value);
    }

    /// <summary>How tall the bars are as a share of the control's own height.</summary>
    public static readonly StyledProperty<double> BarShareProperty =
        AvaloniaProperty.Register<BarcodeView, double>(nameof(BarShare), 0.78);

    public double BarShare
    {
        get => GetValue(BarShareProperty);
        set => SetValue(BarShareProperty, value);
    }

    static BarcodeView()
    {
        AffectsRender<BarcodeView>(ValueProperty, BarShareProperty, SymbologyProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (string.IsNullOrEmpty(Value)) return;

        BarcodeElement[] elements = Barcode(Value!, Symbology);
        if (elements.Length == 0) return;

        // Narrow is one unit and wide three, which is the ratio the standard is
        // read at, and the unit is scaled so the code fills the width it is given.
        double narrow = FitWidth([.. elements.Select(element => element.Units)], bounds.Width);
        double height = bounds.Height * Math.Clamp(BarShare, 0.2, 1.0);

        int units = 0;
        foreach (BarcodeElement element in elements) units += element.Units;

        double codeWidth = units * narrow;
        double x = Math.Max(0, (bounds.Width - codeWidth) / 2);

        foreach (BarcodeElement element in elements)
        {
            double wide = element.Units * narrow;

            // Only the bars are drawn. A space is the paper showing through, so
            // drawing it white would be the same thing said twice. A narrow bar is
            // a bar: taking anything wider than a wide element for ink drew the
            // wide elements only, which is half a barcode.
            if (element.IsBar)
                context.DrawRectangle(Brushes.Black, null, new Rect(x, 0, wide, height));

            x += wide;
        }
    }

    /// <summary>
    /// The bars and spaces of a value, each with the width it is drawn at.
    /// </summary>
    /// <remarks>
    /// Asks <see cref="LabelBarcode.ElementsOf"/>, so the preview is out of the same
    /// table the label file is and there is no second copy of the rule kept here. Two
    /// copies of a barcode pattern are two chances to disagree, and the one place
    /// that must not disagree is the preview: an operator who trusts a barcode drawn
    /// on screen has to be looking at the code the printer draws.
    ///
    /// A character the symbology cannot carry becomes a dash rather than being
    /// dropped, so the value printed under the code and the code itself always
    /// describe the same thing.
    /// </remarks>
    internal static BarcodeElement[] Barcode(string value, LabelCodeSymbology symbology)
    {
        if (string.IsNullOrEmpty(value)) return [];

        var encodable = new System.Text.StringBuilder(value.Length);
        foreach (char character in value)
            encodable.Append(
                LabelBarcode.CanCarry(character, symbology) ? character.ToString() : "-");

        try
        {
            return [.. LabelBarcode.ElementsOf(encodable.ToString(), symbology)];
        }
        catch (ArgumentException)
        {
            // A value the symbology refuses outright is drawn as nothing rather than
            // as a code that reads back as another device.
            return [];
        }
    }

    /// <summary>The same, in Code39, which is what the preview has always drawn.</summary>
    internal static BarcodeElement[] Barcode(string value) =>
        Barcode(value, LabelCodeSymbology.Code39);

    /// <summary>The bar widths in units, wide being three.</summary>
    internal static int[] Elements(string value, LabelCodeSymbology symbology) =>
        [.. Barcode(value, symbology).Select(element => element.Units)];

    /// <summary>The same, in Code39, which is what the preview has always drawn.</summary>
    internal static int[] Elements(string value) =>
        Elements(value, LabelCodeSymbology.Code39);

    /// <summary>
    /// The width of one narrow element, chosen so the code fills the width given.
    /// </summary>
    /// <remarks>
    /// The clear paper a scanner needs on each side is counted, because it is part
    /// of the code. A preview that filled its whole width with bars would draw a
    /// wider code than the file carries, and an operator comparing the two would be
    /// comparing two different barcodes.
    ///
    /// No floor, and that is deliberate. The width at which a code stops being
    /// readable is decided once, by <see cref="LabelCode.Fits"/>, and this control
    /// is only ever handed a value that passed. Holding the bars at some larger
    /// minimum here would push the code off the edge of the preview, which is a
    /// drawing that prints nothing, telling the operator something untrue of the
    /// label.
    /// </remarks>
    internal static double FitWidth(IReadOnlyList<int> widths, double available)
    {
        int units = LabelBarcode.QuietZoneUnits * 2;
        foreach (int width in widths) units += width;

        return units > 0 ? available / units : available;
    }
}
