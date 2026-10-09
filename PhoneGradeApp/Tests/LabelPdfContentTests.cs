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

    private (byte[] Pdf, LabelLayout Size, LabelBarcodeMode Mode, DeviceData Phone) Written(
        DeviceData? phone = null, LabelLayout? layout = null,
        LabelBarcodeMode mode = LabelBarcodeMode.Identifier,
        LabelVariant variant = LabelVariant.Clean)
    {
        DeviceData what = phone ?? Phone();
        LabelLayout size = layout ?? LabelLayout.Address;
        string path = Path.Combine(_folder, $"{Guid.NewGuid():N}.pdf");
        LabelPdfWriter.Write(path, LabelFields.From(what), size, mode, LabelCodeSymbology.Code39, variant);
        return (File.ReadAllBytes(path), size, mode, what);
    }

    /// <summary>
    /// Where the text block starts on the label, as a fraction of its height.
    /// </summary>
    /// <remarks>
    /// Taken from the layout and from the same decision the writer makes about
    /// which payloads become bars. Guessing either is how this test came to look at
    /// a region of a 54mm label that was always blank: it assumed a barcode the
    /// writer had decided against drawing, then measured the paper below where the
    /// barcode would have been.
    /// </remarks>
    private static double TextTop(LabelLayout size, LabelBarcodeMode mode, LabelFields fields)
    {
        int barcodes = new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields))
            .On(size.Stock, mode, fields.IsIdentifiable).Barred.Count;

        return Math.Clamp(
            (size.Stock.TopMarginMm + size.TextYmm(barcodes)) / size.PaperHeightMm, 0d, 1d);
    }

    [Fact]
    public void TheWordsUnderTheBarcodeArePrinted_NotJustWritten()
    {
        // The regression. The words were in the file and off the page at the same
        // time, which is why a test on the file's contents could not see it.
        var written = Written();
        using var picture = LabelPicture.Of(written.Pdf);
        double text = TextTop(written.Size, written.Mode, LabelFields.From(written.Phone));

        long bars = picture.InkBetween(0d, text);
        long words = picture.InkBetween(text, 1d);

        Assert.True(bars > 500, $"the barcode drew {bars} pixels of ink, which is not a barcode");
        Assert.True(words > 200,
            $"below the barcode the label holds {words} pixels of ink; the words are not on it");
    }

    /// <summary>
    /// The three arrangements, so an arrangement added to the settings is covered
    /// the day it is added rather than the day somebody remembers.
    /// </summary>
    public static IEnumerable<object[]> EveryVariant =>
        Enum.GetValues<LabelVariant>().Select(variant => new object[] { variant });

    [Theory]
    [MemberData(nameof(EveryVariant))]
    public void EveryArrangementPrintsItsWords(LabelVariant variant)
    {
        // What the plate planned is what the page carries: the plate is the one
        // place the lines and their sizes are decided, and a writer that drew
        // something else would show up here as missing ink.
        var written = Written(variant: variant);
        using var picture = LabelPicture.Of(written.Pdf);

        var fields = LabelFields.From(written.Phone);
        LabelPlate plate = LabelPlate.Build(
            fields, written.Size, written.Mode, LabelCodeSymbology.Code39, variant);

        double text = TextTop(written.Size, written.Mode, fields);
        long words = picture.InkBetween(text, 1d);

        Assert.True(plate.Lines.Count > 0);
        Assert.True(words > 200,
            $"{variant} printed {words} pixels of ink below the barcode; the words are not on it");
    }

    [Theory]
    [MemberData(nameof(EveryVariant))]
    public void TheLocksKeepTheirEmphasis_OnTheArrangementsThatInvertThem(LabelVariant variant)
    {
        // A thermal label cannot print colour, so emphasis has to come out of black.
        // The inverted arrangements carry a solid band around the locks; measured as
        // ink density, a band is unmistakable against a line of type.
        var written = Written(variant: variant);
        using var picture = LabelPicture.Of(written.Pdf);

        var fields = LabelFields.From(written.Phone);
        LabelPlate plate = LabelPlate.Build(
            fields, written.Size, written.Mode, LabelCodeSymbology.Code39, variant);

        double text = TextTop(written.Size, written.Mode, fields);
        var bands = picture.InkBands().Where(band => band.Bottom > text).ToList();
        Assert.NotEmpty(bands);

        (double top, double bottom) = bands[^1];
        double area = (picture.Width / 2.0) * ((bottom - top) * picture.Height / 2.0);
        double density = area > 0 ? picture.InkBetween(top, bottom) / area : 0;

        if (plate.LocksInverted)
            Assert.True(density > 0.4,
                $"{variant} drew its locks band at {density:P0} ink; the black band is not on the page");
        else
            Assert.True(density < 0.4,
                $"{variant} drew its plain locks line at {density:P0} ink; it reads as a black band");
    }

    [Fact]
    public void EveryLineTheLabelIsSupposedToHaveIsOnIt()
    {
        // Counted as bands rather than as pixels. Three lines of small type and one
        // line of large type carry a similar amount of ink, and only one of them is
        // a label with the faults and the locks on it.
        var written = Written();
        using var picture = LabelPicture.Of(written.Pdf);

        var fields = LabelFields.From(written.Phone);
        int expected = 1
            + (LabelLayout.DetailLine(fields).Length > 0 ? 1 : 0)
            + (LabelLayout.LockLine(fields).Length > 0 ? 1 : 0);

        double text = TextTop(written.Size, written.Mode, fields);
        var bands = picture.InkBands().Where(band => band.Bottom > text).ToList();

        Assert.True(bands.Count >= expected,
            $"the label has {bands.Count} lines of text under its barcode and should have {expected}");
    }

    [Fact]
    public void TheWordsAreLargeEnoughToReadOnAShelf()
    {
        // A label is read at arm's length while a phone is in the other hand, so a
        // line rendered at two point is a line nobody reads. Measured off the render
        // rather than off the request, because the question is what the printer will
        // honour and not what was asked of it.
        var written = Written();
        using var picture = LabelPicture.Of(written.Pdf);

        var bands = picture.InkBands();
        Assert.NotEmpty(bands);

        float mmPerPixel = written.Size.PaperHeightMm / picture.Height;
        foreach ((double top, double bottom) in bands)
        {
            float tallMm = (float)((bottom - top) * picture.Height * mmPerPixel);
            Assert.True(tallMm >= 1f, $"a line of the label is {tallMm:N1}mm tall");
        }
    }

    /// <summary>
    /// Every stock the panel offers, as its DYMO part number.
    ///
    /// Taken from the list rather than written out here, so a stock added to the
    /// panel is covered the day it is added rather than the day somebody remembers.
    /// </summary>
    public static IEnumerable<object[]> EveryStock =>
        LabelStock.All.Select(stock => new object[] { stock.PartNumber });

    [Theory]
    [MemberData(nameof(EveryStock))]
    public void EveryStockThePanelOffersCarriesItsWords(string partNumber)
    {
        // The smallest stocks are where a barcode runs out of paper. Fifteen digits
        // of Code39 with its two asterisks is 271 units, and at the narrowest bar a
        // scanner reads that is 51.5mm against the 48mm and 51mm of printable width
        // those two rolls have, so they print the identifier as words instead of as
        // bars. The words have to be there either way, which is what this asks.
        var written = Written(layout: new LabelLayout(LabelStock.FromPartNumber(partNumber)));
        using var picture = LabelPicture.Of(written.Pdf);

        double text = TextTop(written.Size, written.Mode, LabelFields.From(written.Phone));

        Assert.True(picture.InkBetween(text, 1d) > 100,
            $"stock {partNumber} printed nothing below where its text block starts");
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

        // With no identifier there is no barcode band, so the words move up to the
        // top of the label rather than sitting below where they usually are. What is
        // being asked is only that there is something on it at all.
        Assert.NotEmpty(picture.InkBands());
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
            LabelLayout.TextLine(LabelFields.From(healthy)));
        Assert.Contains(LabelMarkers.LowBattery,
            LabelLayout.TextLine(LabelFields.From(tired)));

        using var before = LabelPicture.Of(Written(healthy).Pdf);
        using var after = LabelPicture.Of(Written(tired).Pdf);

        Assert.False(before.SameAs(after),
            "a battery under the threshold printed exactly the label a healthy one does");
    }

    [Theory]
    [MemberData(nameof(EveryStock))]
    public void TheLabelIsDrawnAtTheSizeTheStockSaysItIs(string partNumber)
    {
        // A label that is not the size of the roll is not a label. Read off the
        // render rather than off the request, so a writer that quietly drew an A4
        // with the label in the corner would fail here rather than at a printer.
        LabelStock stock = LabelStock.FromPartNumber(partNumber);
        var written = Written(layout: new LabelLayout(stock));

        using var picture = LabelPicture.Of(written.Pdf);
        float drawnWidthMm = picture.Width / (picture.Height / (float)stock.HeightMm);

        Assert.True(Math.Abs(drawnWidthMm - stock.WidthMm) < 1f,
            $"stock {partNumber} is {stock.WidthMm}x{stock.HeightMm}mm and came out {drawnWidthMm:F0}mm wide");
    }

    [Fact]
    public void ThePrintableAreaMatchesWhatTheShippedTemplateDescribes()
    {
        // The template that ships with the app was drawn for a 89 x 28mm address
        // label, part 1982991, and its DYMORect is 3.21 x 0.9967 inches. If the
        // printable area here does not come out at those millimetres then the PDF
        // and the .dymo file are drawing on different paper, and the preview is a
        // picture of neither.
        LabelStock address = LabelStock.Address;

        Assert.Equal("1982991", address.PartNumber);
        Assert.Equal(3.21f * 25.4f, address.PrintableWidthMm, 1);
        Assert.Equal(0.9966666f * 25.4f, address.PrintableHeightMm, 1);
    }

    [Theory]
    [InlineData(LabelBarcodeMode.Identifier, 1)]
    [InlineData(LabelBarcodeMode.Split, 2)]
    public void TheBarcodeModeDecidesHowManyCodesAreOnTheLabel(LabelBarcodeMode mode, int expected)
    {
        // Measured by drawing both ways and asking whether the label changed, which
        // is the only way to know the mode reached the paper rather than only the
        // settings file.
        using var identifier = LabelPicture.Of(Written(mode: LabelBarcodeMode.Identifier).Pdf);
        using var chosen = LabelPicture.Of(Written(mode: mode).Pdf);

        if (mode == LabelBarcodeMode.Identifier) Assert.True(identifier.SameAs(chosen));
        else Assert.False(identifier.SameAs(chosen),
            $"choosing {mode} printed the same label as the identifier alone");
    }

    [Fact]
    public void TheTwoCodesAreBothDrawnWhenTheOperatorAsksForThemSeparately()
    {
        // Split is the only mode with more than one code, and both have to be on the
        // paper: a second code that was planned for and left off is a label whose
        // scanner reads half of what the label says.
        var fields = LabelFields.From(Phone());
        var (barred, _) = new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields))
            .On(LabelStock.Address, LabelBarcodeMode.Split, fields.IsIdentifiable);

        Assert.Equal(2, barred.Count);
        Assert.Equal("*356938035643809*", barred[0]);
        Assert.Contains("128GB", barred[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACodeThatCannotBeReadIsNotDrawnAsBars()
    {
        // The two small multi purpose rolls have 48mm and 51mm of printable width.
        // A fifteen digit Code39 with its two asterisks needs 51.5mm at the narrowest
        // bar a scanner reads, so on those it is spelled out rather than squeezed
        // into grey. Every stock is checked, because which ones are too narrow is a
        // consequence of the arithmetic and not something to hard code here.
        var fields = LabelFields.From(Phone());
        var code = new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields));

        // Every symbology, because the width a code is measured against is worked out
        // from its own table and Code128 is the denser of the two. A fit test run
        // against Code39 says nothing about whether the same label is right in the
        // other one.
        foreach (LabelCodeSymbology symbology in new[]
                 { LabelCodeSymbology.Code39, LabelCodeSymbology.Code128 })
        foreach (LabelStock stock in LabelStock.All)
        {
            var (barred, spelled) = code.On(
                stock, LabelBarcodeMode.Identifier, symbology, fields.IsIdentifiable);

            Assert.Equal(barred.Count + spelled.Count, 1);

            foreach (string payload in barred)
            {
                Assert.True(LabelBarcode.DrawnNarrowMm(payload, symbology, stock.PrintableWidthMm)
                        >= LabelBarcode.NarrowestNarrowMm,
                    $"stock {stock.PartNumber} was asked to draw {payload} narrower than a scanner reads");
                Assert.True(LabelBarcode.WidthMm(payload, symbology,
                        LabelBarcode.DrawnNarrowMm(payload, symbology, stock.PrintableWidthMm))
                        <= stock.PrintableWidthMm,
                    $"stock {stock.PartNumber} was asked to draw a code wider than its paper");
            }
        }
    }

    /// <summary>
    /// The shipped template, filled, so a template that has drifted from the code
    /// is caught here rather than at a printer.
    /// </summary>
    private static string ShippedTemplate => File.ReadAllText(
        RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "Assets", "my.dymo"));

    [Theory]
    [InlineData(LabelBarcodeMode.Identifier)]
    [InlineData(LabelBarcodeMode.Split)]
    [InlineData(LabelBarcodeMode.Combined)]
    [InlineData(LabelBarcodeMode.None)]
    public void TheShippedTemplateCarriesWhatTheLabelCarries(LabelBarcodeMode mode)
    {
        // The .dymo file and the label PDF are two renderings of one label. If the
        // template has lost a barcode object, or a sentinel was renamed to something
        // the exporter does not fill, the file still prints: it just prints an empty
        // band where a barcode should be, and nothing anywhere says so.
        var fields = LabelFields.From(Phone());
        var layout = LabelLayout.Address;
        var (barred, spelled) = new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields))
            .On(layout.Stock, mode, fields.IsIdentifiable);

        DymoFillResult filled = DymoTemplate.Fill(ShippedTemplate, fields, layout, mode);

        var values = new Dictionary<string, string>();
        foreach (var element in System.Xml.Linq.XDocument.Parse(filled.Text).Descendants())
        {
            if (element.Name.LocalName is not ("TextObject" or "BarcodeObject")) continue;

            values[element.Element("Name")?.Value ?? "?"] = element.Name.LocalName == "BarcodeObject"
                ? string.Concat(element.Descendants("DataString").Select(d => d.Value))
                : element.Descendants("Text").FirstOrDefault()?.Value.Trim() ?? "";
        }

        // An object with no code to carry is not in the file at all: DYMO's own
        // renderer answers an empty barcode with a minimal stub, so the spare
        // object is taken out rather than left for the renderer to fill in.
        Assert.Equal(barred.Count >= 1, values.ContainsKey("BARCODE_1"));
        if (barred.Count >= 1) Assert.Equal(barred[0], values["BARCODE_1"]);

        Assert.Equal(barred.Count >= 2, values.ContainsKey("BARCODE_2"));
        if (barred.Count >= 2) Assert.Equal(barred[1], values["BARCODE_2"]);

        Assert.Equal(string.Join(" ", spelled), values["TEKST_1"]);
        Assert.Equal(LabelLayout.TextLine(fields), values["TEKST_2"]);
        Assert.Equal(LabelLayout.DetailLine(fields), values["TEKST_3"]);
        Assert.Equal(LabelLayout.LockLine(fields), values["TEKST_4"]);
    }

    [Fact]
    public void EveryObjectInTheShippedTemplateSitsOnThePrintableLabel()
    {
        // The template describes a 1982991 address label: 3.21 by 0.9967 inches of
        // printable area at 0.23, 0.06. An object outside that is drawn on the
        // backing carrier, which is the part of the roll the operator sees and the
        // customer does not.
        var document = System.Xml.Linq.XDocument.Parse(ShippedTemplate);

        var rect = document.Descendants("DYMORect").Single();
        float left = float.Parse(rect.Descendants("X").First().Value);
        float top = float.Parse(rect.Descendants("Y").First().Value);
        float width = float.Parse(rect.Descendants("Width").First().Value);
        float height = float.Parse(rect.Descendants("Height").First().Value);

        foreach (var layout in document.Descendants("ObjectLayout").ToList())
        {
            string name = layout.Parent!.Element("Name")!.Value;
            float x = float.Parse(layout.Descendants("X").First().Value);
            float y = float.Parse(layout.Descendants("Y").First().Value);
            float w = float.Parse(layout.Descendants("Width").First().Value);
            float h = float.Parse(layout.Descendants("Height").First().Value);

            Assert.True(x >= left - 0.001f && x + w <= left + width + 0.001f,
                $"{name} runs from {x} to {x + w} across a label {left} to {left + width}");
            Assert.True(y >= top - 0.001f && y + h <= top + height + 0.001f,
                $"{name} runs from {y} to {y + h} down a label {top} to {top + height}");
        }
    }

    [Fact]
    public void TheShippedTemplateNamesNothingThatLooksLikeAField()
    {
        // A sentinel inside an object name is substituted inside the name. That is
        // how an object called TEKST_SPELLED came to be called TEKST_FRP!ACT!, and
        // a DYMO object whose name no longer matches the one in the layout is a
        // label the DYMO software does not recognise.
        foreach (string name in System.Xml.Linq.XDocument.Parse(ShippedTemplate)
                     .Descendants("Name").Select(e => e.Value))
        {
            foreach (string sentinel in DymoTemplate.KnownSentinels)
                Assert.DoesNotContain(sentinel, name, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("1982991")]
    [InlineData("30256")]
    [InlineData("30336")]
    public void ThePreviewSetsItsWordsAtTheSizeTheFileWill(string partNumber)
    {
        // The whole reason the sizing lives in Core. A preview that set its text at a
        // size of its own choosing could not answer the question it is on the panel
        // for, which is whether the words will fit on the roll: it would look right
        // on screen and come off the bottom of the label.
        LabelStock stock = LabelStock.FromPartNumber(partNumber);
        var layout = new LabelLayout(stock);
        var fields = LabelFields.From(Phone());

        var block = new[] { LabelLayout.TextLine(fields), LabelLayout.DetailLine(fields) }
            .Where(line => line.Length > 0).ToList();

        float written = LabelType.BlockSize(block, layout, barcodes: 1);

        Assert.InRange(written, LabelType.SmallestBodyPoint, LabelType.LargestBodyPoint);

        // And it is the same number for the same inputs, which is the property the
        // two renderers depend on rather than merely resembling.
        Assert.Equal(written, LabelType.BlockSize(block, layout, barcodes: 1));
    }

    [Fact]
    public void ASecondBarcodeLeavesTheTextLessRoomThanOne()
    {
        // The reason the barcode mode is a setting rather than something asked at
        // print time. A label whose words move because something asked later is a
        // label that moves under the operator.
        var layout = LabelLayout.Address;

        Assert.True(layout.TextHeightMm(2) < layout.TextHeightMm(1));
        Assert.True(layout.TextYmm(2) > layout.TextYmm(1));

        // And the words still have somewhere to go with two codes on the stock most
        // shops use, which is 25.3mm of printable height.
        Assert.True(layout.TextHeightMm(2) >= LabelLayout.MinimumTextMm,
            $"two barcodes leave {layout.TextHeightMm(2):N1}mm for the words");
    }

    [Fact]
    public void APhoneThatCannotBeIdentifiedNeverGetsABarcodeOfItsPlaceholder()
    {
        // A barcode reading NOID scans on every phone in the shop, which is worse
        // than no barcode at all.
        var code = new LabelCode(DevicePlaceholders.Identifier, "SM-G991B 128GB B 87%");

        foreach (LabelBarcodeMode mode in Enum.GetValues<LabelBarcodeMode>())
        foreach (LabelCodeSymbology symbology in Enum.GetValues<LabelCodeSymbology>())
        {
            var payloads = code.Payloads(mode, symbology, identifiable: false);
            Assert.True(payloads.Count == 0 || payloads.All(string.IsNullOrEmpty),
                $"{mode} in {symbology} put {string.Join(", ", payloads)} " +
                "on a label for a phone with no serial");
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
        string line = LabelLayout.TextLine(LabelFields.From(written.Phone));

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
