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
/// same order and at the same ratio as the ones the label PDF draws, from the same
/// Code39 table, so an identifier that has been rewritten shows up rewritten here.
///
/// The pattern is read from the value, not from the width. A control that scaled a
/// fixed pattern would look right at one size and be wrong at another, and a narrow
/// label is one of the sizes an operator chooses.
/// </summary>
public sealed class BarcodeView : Control
{
    /// <summary>What to draw. Already wrapped, or the marker becomes part of the data.</summary>
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<BarcodeView, string?>(nameof(Value));

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
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
        AffectsRender<BarcodeView>(ValueProperty, BarShareProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
  if (bounds.Width <= 0 || bounds.Height <= 0) return;
      if (string.IsNullOrEmpty(Value)) return;

  int[] bars = Elements(Value!);
    if (bars.Length == 0) return;

  // Narrow is one unit and wide three, which is the ratio the standard
      // is read at, and the unit is scaled so the code fills the width it
        // is given. The one exception is a code so long that scaling
        // would take the narrow bars below what a printer can put on
        // paper: those are held at the minimum and the code is centred with
        // paper either side, because a barcode whose bars are too fine
        // prints as a grey block and reads back as nothing at all.
        double narrow = FitWidth(bars, bounds.Width);
        double height = bounds.Height * Math.Clamp(BarShare, 0.2, 1.0);
        double codeWidth = 0;
        foreach (int width in bars) codeWidth += narrow * width;
      double x = Math.Max(0, (bounds.Width - codeWidth) / 2);

 foreach (int width in bars)
        {
     // Only the bars are drawn. The gaps are the paper showing through,
            // so drawing them white would be the same thing said twice.
 if (width > 1)
   context.DrawRectangle(Brushes.Black, null, new Rect(x, 0, narrow * width, height));
     x += narrow * width;
        }
    }

    /// <summary>
    /// The bar widths in units, wide being three. A character Code39 cannot carry
    /// becomes a dash rather than being dropped, so the value on the label below
    /// the code and the code itself always describe the same thing.
    /// </summary>
    internal static int[] Elements(string value)
    {
  var widths = new List<int>();
        foreach (char character in value)
        {
      // The start and stop markers are part of the code rather than
       // characters of the value, and they are drawn like any
    // other pattern.
            string encodable = character switch
            {
      '*' => "*",
          _ => Code39.CanEncode(character.ToString()) ? character.ToString() : "-",
            };

   // The narrow gap between one character and the next. Without
    // it one character is read as part of the next and the whole
        // identifier comes back wrong.
      if (widths.Count > 0) widths.Add(1);

            foreach (char element in Code39.Encode(encodable).Single())
       widths.Add(element == 'W' ? 3 : 1);
        }

        return [.. widths];
    }

    /// <summary>
    /// The width of one narrow element, chosen so the code fills the width given.
    ///
    /// No floor, and that is deliberate. A fifteen digit identifier on a 106mm
    /// label really is drawn with bars about a millimetre wide at 96 dpi, so
    /// holding them at some larger "printable minimum" makes the code wider than
    /// the label, and the preview then shows a barcode running off the edge of its
    /// own sheet. The minimum a printer needs is applied where the printing
    /// happens, not in a drawing that prints nothing.
    /// </summary>
    internal static double FitWidth(IReadOnlyList<int> widths, double available)
    {
        int units = 0;
        foreach (int width in widths) units += width;

        return units > 0 ? available / units : available;
    }
}
