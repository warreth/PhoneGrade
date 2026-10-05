using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using PhoneGrade.Core;
using PhoneGrade.UI.Views;
using Xunit;

namespace Tests;

// ============ The barcode on the preview sheet ============
//
// The preview is the one thing an operator looks at to decide whether to trust a
// label. It used to be a run of bar and dot characters, which reads as a barcode
// and is not one, and a value that had been rewritten for the printer showed up
// correctly on the file and wrongly here. These are measured on the rendered
// control, because the fault was in what was drawn.

public class BarcodeViewTests
{
    /// <summary>
    /// One character's elements as the control draws them: nine of them, with a
    /// narrow gap after each one so the next character starts on a space.
    /// </summary>
    /// <remarks>
    /// The stride is ten rather than nine because of that gap. Reading the code in
    /// blocks of nine from offset zero is a mistake that looks right for the first
    /// character and wrong for every one after it.
    /// </remarks>
    private const int ElementsPerCharacter = 9;
    private const int Stride = 10;

    [Fact]
    public void EveryCharacterDrawsNineElementsPlusOneGap()
    {
        int[] bars = BarcodeView.Elements("*356938035643809*");

        int characters = 17; // the fifteen digits and the two markers
        Assert.Equal(characters * ElementsPerCharacter + (characters - 1), bars.Length);

        // Sixteen gaps for seventeen characters, the last of which ends the code
        // rather than being followed by another.
        for (int gap = 1; gap < characters; gap++)
            Assert.Equal(1, bars[gap * Stride - 1]);
    }

    [Fact]
    public void EveryCharacterCarriesThreeWideElements()
    {
        // Three wide out of nine is what makes this Code39 rather than any other
        // nine element symbology, and it is what a decoder counts to find where
        // one character ends and the next begins.
        int[] bars = BarcodeView.Elements("*356938035643809*");

    int characters = 17;
        for (int character = 0; character < characters; character++)
        {
int wide = bars.Skip(character * Stride).Take(ElementsPerCharacter).Count(width => width == 3);
   Assert.True(wide == 3, $"character {character} drew {wide} wide elements instead of three");
        }
    }

    [Fact]
    public void EveryElementIsEitherNarrowOrWide_AndNothingElse()
    {
        // The drawing is two widths. A third value is a mistake in the table or in
        // the code that turns a character into elements.
        foreach (int width in BarcodeView.Elements("*356938035643809*"))
            Assert.True(width is 1 or 3, $"an element is {width}, which is neither narrow nor wide");
    }

    [Fact]
    public void EveryCharacterDrawsTheElementsTheStandardGivesForIt()
    {
        // Read back against the table rather than against a count, because the
        // count cannot tell one wrong character from a right one.
        foreach (char character in "3569AZ")
        {
            int[] drawn = BarcodeView.Elements(character.ToString());
            string expected = Code39.Encode(character.ToString()).Single();

            for (int element = 0; element < ElementsPerCharacter; element++)
                Assert.True((expected[element] == 'W' ? 3 : 1) == drawn[element],
                    $"{character} drew the wrong width at element {element}");
        }
    }

    [Fact]
    public void ACharacterTheCodeCannotCarryStillDrawsSomething()
    {
        // Dropping it would make the barcode shorter than the number printed under
        // it, and a scanner would read a different serial than the one shown.
        int[] withLetter = BarcodeView.Elements("*ABC*");
        int[] withDash = BarcodeView.Elements("*A-C*");

        Assert.Equal(withLetter.Length, withDash.Length);
    }

    [Fact]
    public void TheCodeFillsTheWidthItIsGiven_AtEveryStockSize()
    {
  // Scaled rather than laid out at a fixed unit, so a narrow label shows the
        // same code rather than a code running off the side of it.
   int[] bars = BarcodeView.Elements("*356938035643809*");

        double narrow = BarcodeView.FitWidth(bars, 400);

Assert.Equal(400, bars.Sum(width => narrow * width), 1);
   Assert.True(narrow > 0);
    }

    [Fact]
    public void TheCodeNeverDrawsWiderThanTheSheetItIsOn()
    {
        // A long identifier has more bars than fit comfortably, and the honest
        // thing is to scale them down rather than let them run off the side. The
        // minimum a printer can hold is applied where the printing happens; this
        // drawing prints nothing, so it only has to fit.
        foreach (string value in new[] { "*356938035643809*", "*" + new string('1', 60) + "*" })
        {
            int[] bars = BarcodeView.Elements(value);
            double narrow = BarcodeView.FitWidth(bars, 300);
            double total = bars.Sum(width => narrow * width);

      Assert.True(total <= 300.001,
          $"a {value.Length} character code draws {total:F0} pixels wide on a 300 pixel sheet");
        }
    }

    [Fact]
    public void AnEmptyValueDrawsNothingRatherThanCrashing()
    {
        // A device whose serial could not be read. The panel is still open and the
        // sheet is still drawn; only the bars are missing.
        Assert.Empty(BarcodeView.Elements(""));
    }

    [AvaloniaFact]
    public void TheControlPaintsSomethingForAValue_AndNothingForNone()
    {
        var withValue = new Window { Width = 300, Height = 80, Content = new BarcodeView { Value = "*356938035643809*" } };
        var without = new Window { Width = 300, Height = 80, Content = new BarcodeView { Value = "" } };

        try
        {
            withValue.Show();
            without.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            double painted = AverageLuminance(withValue);
            double blank = AverageLuminance(without);

            // A barcode is black on white, so it darkens the frame. Nothing drawn
            // leaves the two identical, which is what the run of characters used
            // to look like from here: it was a row of text, not bars.
            Assert.True(painted < blank - 5,
                $"the barcode control paints nothing for a value ({painted:F0} against {blank:F0})");
        }
        finally
        {
            withValue.Close();
            without.Close();
        }
    }

    /// <summary>The mean brightness of what a window is currently showing.</summary>
    private static double AverageLuminance(Window window)
    {
        using var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window);
        using var png = new MemoryStream();
        frame.Save(png);
        return PngLuminance.Average(png.ToArray());
    }
}
