using PhoneGrade.Core;
using Xunit;

namespace Tests;

/// <summary>
/// The Code128 encoder, against the standard's own rules rather than against a
/// table of expected bars.
/// </summary>
/// <remarks>
/// These exist because a barcode table that is one row out does not fail a build.
/// It fails at the counter, months later, as a device filed under another device's
/// record. So each property below is a rule of the standard, checked over the whole
/// table, and the checksum is recomputed rather than copied.
/// </remarks>
public class Code128Tests
{
    [Fact]
    public void EveryPatternHasTheModuleCountTheStandardGivesIt()
    {
        foreach (int value in Enumerable.Range(0, Code128.LastValue + 1))
        {
            var elements = Code128.Of(value).ToList();

            // Eleven modules for every value, and thirteen for the stop, which is
            // the one that carries two extra modules so a reader can find the end.
            int expected = value == Code128.Stop ? 13 : 11;
            int total = elements.Sum(element => element.Units);

            Assert.True(total == expected,
                $"Code128 value {value} comes to {total} modules, the standard gives it {expected}");
        }
    }

    [Fact]
    public void EveryPatternStartsWithABarAndThenAlternates()
    {
        foreach (int value in Enumerable.Range(0, Code128.LastValue + 1))
        {
            var elements = Code128.Of(value).ToList();

            Assert.True(elements[0].IsBar,
                $"Code128 value {value} starts with a space rather than a bar");

            for (int i = 1; i < elements.Count; i++)
                Assert.True(elements[i].IsBar != elements[i - 1].IsBar,
                    $"Code128 value {value} has two elements in a row at {i}");
        }
    }

    [Fact]
    public void NoTwoValuesShareAPattern()
    {
        // One value read as another is a device filed under the wrong record, and a
        // duplicated row is the only way that happens.
        var shapes = Enumerable.Range(0, Code128.LastValue + 1)
            .Select(value => string.Concat(
                Code128.Of(value).Select(element => element.Units.ToString())))
            .ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }

    [Fact]
    public void EveryElementIsBetweenOneAndFourModules()
    {
        foreach (int value in Enumerable.Range(0, Code128.LastValue + 1))
            foreach (BarcodeElement element in Code128.Of(value))
                Assert.True(element.Units is >= 1 and <= 4,
                    $"Code128 value {value} has an element {element.Units} modules wide");
    }

    [Fact]
    public void AnEmptyValueStillEndsWithTheStopPattern()
    {
        // A reader has to find the end of the data. A code with no stop is a run it
        // cannot tell where it finished.
        List<int> codes = Code128.Values("");

        Assert.Equal(Code128.Stop, codes[^1]);
    }

    [Fact]
    public void TheStopIsTheOnlyValueWithSevenElements()
    {
        foreach (int value in Enumerable.Range(0, Code128.LastValue))
            Assert.Equal(6, Code128.Of(value).Count());
    }

    [Fact]
    public void TheChecksumIsTheOneTheStandardDescribes()
    {
        // Computed here from the rule rather than copied from the encoder, so the
        // two have to agree without either being able to drift into the other.
        foreach (string value in new[] { "A", "AB", "1", "12", "123", "1234",
                                         "356938035643809", "256GB C 78",
                                         "356938035643809 256GB C 78" })
        {
            List<int> codes = Code128.Values(value);

            // The stop is not part of the sum, and neither is the checksum itself.
            int expected = codes[0];
            for (int i = 1; i < codes.Count - 1; i++) expected += i * codes[i];

            Assert.Equal(expected % 103, Code128.Checksum(codes));
            Assert.InRange(Code128.Checksum(codes), 0, 102);
        }
    }

    [Fact]
    public void OneCharacterDroppedChangesTheChecksum()
    {
        // The whole point of the checksum: a code read with something missing out of
        // the middle does not pass.
        Assert.NotEqual(
            Code128.Checksum(Code128.Values("356938035643809")),
            Code128.Checksum(Code128.Values("35693803564380")));
    }

    [Fact]
    public void ADigitRunStartsInTheSetThatReadsTwoDigitsAsOneValue()
    {
        // Which is what makes Code128 worth having over Code39 for an identifier:
        // five and a half modules a digit instead of eleven a character.
        List<int> codes = Code128.Values("356938035643809");

        Assert.Equal(Code128.CodeC, codes[0]);

        // The digits come after as pairs, so 35 and 69 and so on rather than one
        // value a character.
        Assert.Equal(35, codes[1]);
        Assert.Equal(69, codes[2]);
        Assert.Equal(38, codes[3]);
    }

    [Fact]
    public void TheDenserSetIsLeftAgainWhenTheDigitsRunOut()
    {
        List<int> codes = Code128.Values("356938035643809 256GB C 78");

        Assert.Contains(Code128.CodeB, codes);
        Assert.Equal(Code128.Stop, codes[^1]);
    }

    [Fact]
    public void OneDigitOnItsOwnIsCarriedAsACharacter()
    {
        // Code C cannot encode a single digit, and a value that stops one character
        // short of a pair is the case that catches a writer that switches greedily.
        List<int> codes = Code128.Values("12345");

        Assert.Contains(Code128.CodeB, codes);
        Assert.Equal(Code128.Stop, codes[^1]);
    }

    [Fact]
    public void EveryValueTheLabelCarriesEncodesWithoutThrowing()
    {
        // The label's own payloads, because a writer that throws on one of them is a
        // writer whose exception escapes the export.
        foreach (string value in new[]
                 {
                     "356938035643809", "SM-G991B", "SM-G991B/128GB",
                     "256GB C 78", "iPhone 13 Pro 256GB", "12-34 56_78",
                     "356938035643809 256GB C 78",
                 })
        {
            Assert.True(Code128.CanEncode(value), $"Code128 cannot carry {value}");
            Assert.True(Code128.ModulesOf(value) > 0);
        }
    }

    [Fact]
    public void ACharacterOutsideTheAlphabetIsRefusedRatherThanRewritten()
    {
        // Silently dropping it would shorten the code, and the fit test would then
        // have measured a different code from the one drawn.
        Assert.Throws<ArgumentException>(() => Code128.Elements("35693803564380é9").ToList());
    }
}

/// <summary>
/// Which barcode settings the roll and the symbology in force actually allow.
/// </summary>
public class LabelSymbologyTests
{
    private static LabelCode Code() =>
        new("356938035643809", LabelLayout.ScannableLine(LabelFields.From(Phone())));

    [Fact]
    public void TheCombinedModeIsOfferedInCode128OnAStandardRoll()
    {
        // The reason the symbology exists at all. Code39 cannot put the identifier
        // and the specification on one code at any width a scanner reads; Code128 can.
        Assert.False(Code().Available(LabelStock.Address, LabelBarcodeMode.Combined, LabelCodeSymbology.Code39));
        Assert.True(Code().Available(LabelStock.Address, LabelBarcodeMode.Combined, LabelCodeSymbology.Code128));
    }

    [Fact]
    public void TheCombinedModeIsStillRefusedOnTheNarrowestRolls()
    {
        // Denser is not wide enough. The two small multi purpose stocks have 48mm and
        // 51mm of printable width, and the combined value does not come to that in
        // any symbology this app can write.
        foreach (string part in new[] { "30334", "30336" })
        {
            LabelStock stock = LabelStock.FromPartNumber(part);

            Assert.False(Code().Available(stock, LabelBarcodeMode.Combined, LabelCodeSymbology.Code39),
                $"{part} was offered the combined mode in Code39");
            Assert.False(Code().Available(stock, LabelBarcodeMode.Combined, LabelCodeSymbology.Code128),
                $"{part} was offered the combined mode in Code128");
        }
    }

    [Fact]
    public void Code128CarriesTheSameValueInLessThanHalfTheWidth()
    {
        string identifier = "356938035643809";

        int code39 = LabelBarcode.UnitsOf(
            LabelBarcode.Encode(identifier, LabelCodeSymbology.Code39, out _)!, LabelCodeSymbology.Code39);
        int code128 = LabelBarcode.UnitsOf(
            LabelBarcode.Encode(identifier, LabelCodeSymbology.Code128, out _)!, LabelCodeSymbology.Code128);

        Assert.True(code128 * 2 < code39,
            $"Code128 came to {code128} units against Code39's {code39}, which is not the halving it is there for");
    }

    [Fact]
    public void Code39WrapsTheIdentifierAndCode128DoesNot()
    {
        // The asterisks are Code39's start and stop markers and are part of the data
        // everywhere else. Putting them on a Code128 code adds two characters that
        // are not the phone's serial number.
        Assert.Equal("*356938035643809*",
            LabelBarcode.Encode("356938035643809", LabelCodeSymbology.Code39, out _));
        Assert.Equal("356938035643809",
            LabelBarcode.Encode("356938035643809", LabelCodeSymbology.Code128, out _));
    }

    [Fact]
    public void Code128CarriesALowerCaseModelNameWhereCode39Cannot()
    {
        // The reason a shop might choose it beyond the width: the specification goes
        // on the code as the phone spelled it rather than as a row of dashes.
        string code39 = LabelBarcode.Encode("iphone 13 pro", LabelCodeSymbology.Code39, out _)!;
        string code128 = LabelBarcode.Encode("iphone 13 pro", LabelCodeSymbology.Code128, out _);

        Assert.DoesNotContain("P", code128);
        Assert.True(LabelBarcode.UnitsOf(code128, LabelCodeSymbology.Code128)
            < LabelBarcode.UnitsOf(code39, LabelCodeSymbology.Code39));
    }

    [Fact]
    public void TheCombinedValueSeparatesItsTwoHalves()
    {
        // Fifteen digits followed by letters with nothing between them is one string
        // rather than two, and a reader cannot tell the identifier from the grade.
        string payload = Code().Payloads(LabelBarcodeMode.Combined, LabelCodeSymbology.Code128, true)[0];

        Assert.Contains(" ", payload);
        Assert.StartsWith("356938035643809", payload);
    }

    [Fact]
    public void TheCombinedValueCarriesNoMarkersOfItsOwn()
    {
        // Code39 wrapping the specification put a second pair of asterisks in the
        // middle of the value, where a reader takes them for data.
        string payload = Code().Payloads(LabelBarcodeMode.Combined, LabelCodeSymbology.Code39, true)[0];

        Assert.Equal("356938035643809 256GB C 78", payload);
    }

    [Fact]
    public void EveryModeAndSymbologyStillDecidesTheSameWayAboutFitting()
    {
        // The fit test is asked per symbology, because the width it measures against
        // comes out of the symbology's own table.
        foreach (LabelStock stock in LabelStock.All)
        foreach (LabelCodeSymbology symbology in Enum.GetValues<LabelCodeSymbology>())
        foreach (LabelBarcodeMode mode in Enum.GetValues<LabelBarcodeMode>())
        {
            // No barcode asked for carries nothing, which is the point of it rather
            // than a fault, so it is the one mode with nothing to check.
            if (mode == LabelBarcodeMode.None) continue;

            var (barred, spelled) = Code().On(stock, mode, symbology, true);

            Assert.True(barred.Count + spelled.Count > 0,
                $"{stock.PartNumber} in {symbology} with {mode} put nothing on the label at all");

            foreach (string payload in barred)
                Assert.True(LabelBarcode.DrawnNarrowMm(payload, symbology, stock.PrintableWidthMm)
                        >= LabelBarcode.NarrowestNarrowMm,
                    $"{stock.PartNumber} was asked to draw {payload} narrower than a scanner reads");
        }
    }

    [Fact]
    public void ADymoTemplateIsFilledInItsOwnSymbologyNotTheOnesChosen()
    {
        // DYMO draws that file rather than this app, so a payload encoded for the
        // other symbology prints as a code the shop's scanner cannot read. The
        // template's own declaration wins, and there is no way to override it, so
        // the file is correct whatever the operator has chosen for the PDF.
        string filled = DymoTemplate.Fill(TestTemplate(), LabelFields.From(Phone())).Text;

        // The template declares Code39, so the code goes in wrapped in the markers
        // Code39 needs. The text field beside it holds the bare identifier, which is
        // a different thing on the label and stays bare.
        Assert.Contains("<BarcodeFormat>Code39</BarcodeFormat>", filled);
        Assert.Contains("<DataString>*356938035643809*</DataString>", filled);
        Assert.Contains("<Text>356938035643809</Text>", filled);
    }

    [Fact]
    public void ATemplateThatDeclaresCode128IsReadAsCode128()
    {
        Assert.Equal(LabelCodeSymbology.Code39, DymoTemplate.DeclaredSymbology(TestTemplate()));
        Assert.Equal(LabelCodeSymbology.Code128, DymoTemplate.DeclaredSymbology(
            TestTemplate().Replace("Code39", "Code128")));
    }

    [Fact]
    public void AMentionOfTheWordInATextFieldDoesNotDecideTheSymbology()
    {
        // Only the barcode objects' own declarations count. The word appears in
        // label text often enough that reading it there would be a coin toss.
        Assert.Equal(LabelCodeSymbology.Code39,
            DymoTemplate.DeclaredSymbology(TestTemplate().Replace("Code39", "Code128", StringComparison.Ordinal)
                .Replace("<BarcodeFormat>Code128</BarcodeFormat>", "<BarcodeFormat>Code39</BarcodeFormat>")));
    }

    private static string TestTemplate() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Label xmlns="http://www.dymo.com/xml/namespaces/dymolabel/v3">
          <BarcodeObject>
            <Name>BARCODE_1</Name>
            <BarcodeFormat>Code39</BarcodeFormat>
            <DataString>BARCODE1</DataString>
          </BarcodeObject>
          <TextObject><Text>IDENTIFIER</Text></TextObject>
        </Label>
        """;

    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809",
        Model = "iPhone 13 Pro",
        Color = "Graphite",
        Storage = "256GB",
        Memory = "6GB",
        BatteryHealth = "78",
        BatteryCycleCount = 612,
        Quality = "C",
        PayMethod = "Btw",
    };
}