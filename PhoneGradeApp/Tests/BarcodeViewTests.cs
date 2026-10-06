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
        double drawn = bars.Sum(width => narrow * width);

        // The clear paper a scanner needs is counted, so what fills the width is
        // the code plus its two quiet zones and not the bars alone. A preview that
        // drew the bars across the whole sheet would be showing a wider code than
        // the file carries, which is the one thing a preview of a barcode must not
        // do.
        double quiet = 2 * LabelBarcode.QuietZoneUnits * narrow;

        Assert.Equal(400, drawn + quiet, 1);
        Assert.True(drawn < 400, $"the bars alone fill {drawn:F0} of 400 pixels");
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
    public void EveryNarrowBarIsDrawn_NotOnlyTheWideOnes()
    {
        // The regression. Inking anything wider than a wide element drew the wide
        // elements alone, which is roughly half of a Code39 code: the preview showed
        // a barcode the label file did not contain, and it was the preview an
        // operator would have compared the printed label against.
        Code39Element[] elements = BarcodeView.Barcode("*356938035643809*");

        int bars = elements.Count(element => element.IsBar);
        int wideBars = elements.Count(element => element.IsBar && element.Units > Code39.NarrowUnit);

        Assert.True(bars > wideBars * 2,
            $"the code has {bars} bars of which {wideBars} are wide; inking only the wide " +
            "ones draws half a barcode");

        // Every other element of a Code39 character is a space, and no two spaces
        // run together, which is what tells a scanner where one character stops.
        foreach (Code39Element element in elements)
            Assert.True(element.Units is Code39.NarrowUnit or Code39.WideUnit,
                $"an element is {element.Units} units, which Code39 does not have");
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

            var (painted, blank) = MeasureBoth(withValue, without);

            // A barcode is black on white, so it darkens the frame. Nothing drawn
            // leaves the two identical, which is what the run of characters used
            // to look like from here: it was a row of text, not bars.
            Assert.True(painted < blank - 5,
                $"the barcode control paints nothing for a value ({painted:F0} against {blank:F0})");
        }
        finally
        {
            // Closing a window queues its last render pass, so the queue is drained
            // while this test's application is still the one on duty. A pass left
            // behind runs during the next test's setup and fails it for this one's
            // window.
            withValue.Close();
            without.Close();
            Tests.HeadlessRender.Drain();
        }
    }

    /// <summary>
    /// The mean brightness of what a window is currently showing.
    ///
    /// The two windows are captured in one go, one after the other and with nothing
    /// in between, because that is what makes the comparison mean anything. Read
    /// separately they race the layout of each other: one window's pass can land
    /// between the other window's paint and its capture, and a barcode that has
    /// not been painted yet measures the same as a barcode that is not drawn.
    /// </summary>
    private static (double Painted, double Blank) MeasureBoth(Window withValue, Window without)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Tests.HeadlessRender.Drain();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            double painted = AverageLuminance(withValue);
            double blank = AverageLuminance(without);

            // A barcode is black on white and darkens its sheet by a wide margin,
            // so a difference this size cannot come from a partial paint. Anything
            // smaller is treated as not yet painted and the pair is taken again.
            if (painted < blank - 5) return (painted, blank);
        }

        return (AverageLuminance(withValue), AverageLuminance(without));
    }

    /// <summary>The mean brightness of what a window is currently showing.</summary>
    private static double AverageLuminance(Window window)
    {
        using var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window);
        Assert.NotNull(frame);

        using var png = new MemoryStream();
        frame.Save(png);
        return PngLuminance.Average(png.ToArray());
    }
}
