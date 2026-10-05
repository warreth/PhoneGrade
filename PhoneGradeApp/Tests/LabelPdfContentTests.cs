using System.IO;
using System.Linq;
using PhoneGrade.Core;
using PhoneGrade.Tests;
using Xunit;

namespace Tests;

// ============ What is actually printed on the label ============
//
// The label PDF once came out as a barcode and nothing else. The file existed,
// it parsed, it had every word written into it, and it printed a blank label with
// bars on it, because a height constraint on the band above the words clipped
// them off the page. Every test that asked whether the file was written passed.
//
// So these ask the only question that matters: how much ink landed where. A
// label that is blank in the lower half is a label nobody can read, however
// completely the file describes it.

public class LabelPdfContentTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"labelpdf-{Guid.NewGuid():N}");

    public LabelPdfContentTests() => Directory.CreateDirectory(_folder);

    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809", Model = "SM-G991B", Color = "Phantom Silver",
        Storage = "128GB", BatteryHealth = "87", Quality = "B", PayMethod = "Marge",
    };

    private (byte[] Pdf, LabelLayout Size, DeviceData Phone) Written(
        DeviceData? phone = null, LabelLayout? layout = null)
    {
        DeviceData what = phone ?? Phone();
        LabelLayout size = layout ?? LabelLayout.Address;
        string path = Path.Combine(_folder, $"{Guid.NewGuid():N}.pdf");
        LabelPdfWriter.Write(path, LabelFields.From(what), size);
        return (File.ReadAllBytes(path), size, what);
    }

    [Fact]
    public void TheWordsUnderTheBarcodeArePrinted_NotJustWritten()
    {
        // The regression. The words were in the file and off the page at the same
        // time, which is why a test on the file's contents could not see it.
        var written = Written();
        using var picture = LabelPicture.Of(written.Pdf);

        long bars = picture.InkBetween(0d, 0.45);
        long words = picture.InkBetween(0.55d, 1d);

        Assert.True(bars > 500, $"the barcode drew {bars} pixels of ink, which is not a barcode");
        Assert.True(words > 500,
            $"the lower half of the label holds {words} pixels of ink; the words are not on it");
    }

    [Fact]
    public void TheWordsAreLargeEnoughToReadOnAShelf()
    {
        // A label is read at arm's length while a phone is in the other hand, so a
        // caption rendered at two point is a caption nobody reads. Measured by how
        // much height the ink takes, not by what was asked for: the question is what
        // the printer will honour.
        var written = Written();
        using var picture = LabelPicture.Of(written.Pdf);

        int firstInked = -1;
        int lastInked = -1;
        for (int y = 0; y < picture.Height; y++)
        {
            long row = picture.InkBetween((double)y / picture.Height, (double)(y + 1) / picture.Height);
            if (row < 5) continue;

            if (firstInked < 0) firstInked = y;
            lastInked = y;
        }

        Assert.True(firstInked >= 0 && lastInked > firstInked, "the label is empty");
        Assert.True(lastInked - firstInked > picture.Height / 4,
            $"the ink on the label is {(lastInked - firstInked) * 100 / picture.Height}% of its height");
    }

    [Theory]
    [InlineData("106x57")]
    [InlineData("159x57")]
    [InlineData("89x36")]
    public void EveryStockThePanelOffersCarriesItsWords(string stock)
    {
        // Three stocks, and the smallest one is where the barcode runs out of paper:
        // fifteen digits of Code39 is 339 units wide, and holding every bar to the
        // width a printer likes put the code 6mm past the edge of an 89mm label, at
        // which point the export failed outright instead of coming out narrow.
        var written = Written(layout: LabelLayout.Presets[stock]);
        using var picture = LabelPicture.Of(written.Pdf);

        Assert.True(picture.InkBetween(0.55d, 1d) > 200,
            $"a {stock}mm label printed nothing under its barcode");
    }

    [Fact]
    public void ADeviceWithNothingToEncodeStillSaysWhatItIs()
    {
        // The barcode is dropped when there is nothing to encode, and last time the
        // whole label went with it. The specification line is what an operator reads
        // off a device, so it has to survive every barcode there is not.
        var phone = Phone();
        phone.Identifier = "";

        Assert.Null(LabelBarcode.Encode(phone.Identifier, out _));

        var written = Written(phone);
        using var picture = LabelPicture.Of(written.Pdf);

        Assert.True(picture.InkBetween(0.55d, 1d) > 200,
            "a label with no identifier carries no words at all");
    }

    [Fact]
    public void ALowBatteryIsMarkedOnThePrintedLabel_AndNotOnlyInTheText()
    {
        // The marker is the one thing on the label that moves a price, so it has to
        // be on the paper. Compared as two pictures rather than by counting ink:
        // one extra pair of brackets is a few hundred pixels on a label with a
        // barcode on it, which is a difference no threshold should be trusted to
        // call. Identical pixels means the marker never reached the page.
        var healthy = Phone();
        var tired = Phone();
        tired.BatteryHealth = "78";

        Assert.Equal("SM-G991B 128GB Phantom Silver B 87% Marge",
            LabelLayout.Address.TextLine(LabelFields.From(healthy)));
        Assert.Contains(LabelMarkers.LowBattery,
            LabelLayout.Address.TextLine(LabelFields.From(tired)));

        using var before = LabelPicture.Of(Written(healthy).Pdf);
        using var after = LabelPicture.Of(Written(tired).Pdf);

        Assert.False(before.SameAs(after),
            "a battery under the threshold printed exactly the label a healthy one does");
    }

    [Fact]
    public void TheLabelIsDrawnAtTheSizeThePanelSaysItIs()
    {
        // A preview that is not the size of the label is a picture of a label. The
        // stock sizes are read off the render rather than off the request, so a
        // layout that quietly drew an A4 with the label in the corner would fail.
        foreach (LabelLayout size in LabelLayout.Presets.Values)
        {
            using var picture = LabelPicture.Of(Written(layout: size).Pdf);
            float drawnWidthMm = picture.Width / (picture.Height / (float)size.HeightMm);

            Assert.True(Math.Abs(drawnWidthMm - size.WidthMm) < 1f,
                $"a {size.WidthMm}x{size.HeightMm}mm stock came out {drawnWidthMm:F0}mm wide");
        }
    }

    [Fact]
    public void TheLabelCanBeReadWithoutAnInspectionReport()
    {
        // The whole point of the label. A full report PDF exists and says
        // everything; the label has one line and a barcode, and that line has to
        // carry the values the report would have led with, or the label answers
        // nothing on its own.
        var written = Written();
        string line = written.Size.TextLine(LabelFields.From(written.Phone));

        foreach (string value in new[] { written.Phone.Model, written.Phone.Storage, written.Phone.Color })
            Assert.Contains(value, line, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(written.Phone.Quality, line);
        Assert.Contains("87%", line);
        Assert.Contains(written.Phone.PayMethod, line);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
