using System.IO;
using System.Xml.Linq;
using PhoneGrade.Core;
using Xunit;

namespace Tests;

// ============ The three shipped .dymo arrangements ============
//
// The app draws its own label and also writes a DYMO file. Those are two renderers,
// and the one thing they must not do is put different words on the two pieces of
// paper. The variant templates are filled through the same label lines the app's
// own drawing uses, and these tests keep them together: the file carries the
// arrangement's lines, the locks keep their black band, the grade block keeps its
// frame, and the objects move when a second barcode takes room.

public class DymoVariantTests
{
    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809", Model = "iPhone 13 Pro", Color = ColorKeys.Graphite,
        Storage = "256GB", Memory = "6GB", BatteryHealth = "78", BatteryCycleCount = 612,
        Quality = "C", PayMethod = "BTW",
        FactoryResetProtection = PhoneGrade.Core.SecurityServices.FrpLockService.FrpLockStatus.Locked,
        ActivationLock = PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Locked,
        ComponentChecks =
        [
            new ComponentStatus
            {
                Name = "Battery", SerialRead = "L9", SerialOriginal = "K1",
                Status = ComponentStatusType.Mismatch,
            },
        ],
    };

    /// <summary>The filled file for one arrangement, parsed and as text.</summary>
    private static (string Text, XDocument Xml) Filled(
        LabelVariant variant, DeviceData? device = null,
        LabelBarcodeMode mode = LabelBarcodeMode.Identifier)
    {
        string template = DymoTemplateFiles.Read(null, variant);
        DymoFillResult filled = DymoTemplate.Fill(
            template, LabelFields.From(device ?? Phone()),
            LabelLayout.Address, mode, cyclesMinimum: 0, variant: variant);

        Assert.Empty(filled.UnfilledFields);
        return (filled.Text, XDocument.Parse(filled.Text));
    }

    /// <summary>The text of every Text object in the file, by its object name.</summary>
    private static IReadOnlyDictionary<string, string> ObjectText(XDocument xml) =>
        xml.Descendants("TextObject").ToDictionary(
            element => element.Element("Name")!.Value,
            element => element.Descendants("Text").First().Value);

    [Fact]
    public void EveryArrangementHasItsOwnShippedTemplate()
    {
        foreach (LabelVariant variant in Enum.GetValues<LabelVariant>())
            Assert.NotNull(DymoTemplateFiles.ShippedFor(variant));

        Assert.EndsWith("my-structured.dymo", DymoTemplateFiles.ShippedFor(LabelVariant.Structured));
        Assert.EndsWith("my-gradeblock.dymo", DymoTemplateFiles.ShippedFor(LabelVariant.GradeBlock));
    }

    [Fact]
    public void TheStructuredFileCarriesTheArrangementsLines()
    {
        var (_, xml) = Filled(LabelVariant.Structured);
        var objects = ObjectText(xml);

        Assert.Equal("iPhone 13 Pro 256GB", objects["TEKST_1"]);
        Assert.Contains("Graphite", objects["TEKST_2"], StringComparison.Ordinal);
        Assert.Contains("612 CYCLES", objects["TEKST_2"], StringComparison.Ordinal);
        Assert.Contains("BTW", objects["TEKST_2"], StringComparison.Ordinal);
        Assert.Equal("1x NON-OEM", objects["TEKST_3"]);
        Assert.Equal("FRP! ACT!", objects["TEKST_4"]);
    }

    [Fact]
    public void TheGradeBlockFileCarriesTheGradeAndTheSameValues()
    {
        var (_, xml) = Filled(LabelVariant.GradeBlock);
        var objects = ObjectText(xml);

        Assert.Equal("C", objects["TEKST_1"]);
        Assert.Equal("iPhone 13 Pro 256GB", objects["TEKST_2"]);
        Assert.Contains("78% [X]", objects["TEKST_3"], StringComparison.Ordinal);
        Assert.Contains("612", objects["TEKST_4"], StringComparison.Ordinal);
        Assert.Contains("NON-OEM", objects["TEKST_4"], StringComparison.Ordinal);
        Assert.Equal("FRP! ACT!", objects["TEKST_5"]);
    }

    [Fact]
    public void TheSameValuesAreOnTheFileAsOnTheAppDrawnLabel()
    {
        // One reading of the values feeds both renderers. If these ever drift, a
        // shop is checking one label and printing another.
        LabelFields fields = LabelFields.From(Phone());

        var (_, structured) = Filled(LabelVariant.Structured);
        LabelPlate plate = LabelPlate.Build(
            fields, LabelLayout.Address, LabelBarcodeMode.Identifier,
            LabelCodeSymbology.Code39, LabelVariant.Structured);

        Assert.Contains(plate.Lines, line => line.Text == "iPhone 13 Pro 256GB");
        Assert.Contains(plate.Lines, line => line.Text == "FRP! ACT!");
        Assert.Equal("iPhone 13 Pro 256GB", ObjectText(structured)["TEKST_1"]);

        LabelTexts texts = LabelTexts.From(fields);
        Assert.Equal(texts.Title, ObjectText(structured)["TEKST_1"]);
        Assert.Equal(texts.Meta, ObjectText(structured)["TEKST_2"]);
        Assert.Equal(texts.Faults, ObjectText(structured)["TEKST_3"]);
        Assert.Equal(texts.Locks, ObjectText(structured)["TEKST_4"]);
    }

    [Fact]
    public void TheLocksCarryTheirBlackBand_InBothNewArrangements()
    {
        foreach (LabelVariant variant in new[] { LabelVariant.Structured, LabelVariant.GradeBlock })
        {
            var (_, xml) = Filled(variant);
            string locksName = variant == LabelVariant.Structured ? "TEKST_4" : "TEKST_5";

            XElement locks = xml.Descendants("TextObject")
                .Single(element => element.Element("Name")!.Value == locksName);

            XElement background = locks.Descendants("BackgroundBrush").First().Descendants("Color").First();
            XElement font = locks.Descendants("FontBrush").First().Descendants("Color").First();

            Assert.Equal("1", background.Attribute("A")!.Value);
            Assert.Equal("0", background.Attribute("R")!.Value);
            Assert.Equal("1", font.Attribute("R")!.Value);
            Assert.Equal("1", font.Attribute("G")!.Value);
            Assert.Equal("1", font.Attribute("B")!.Value);
        }
    }

    [Fact]
    public void TheGradeBlockIsDrawnAsAnOutlinedBox()
    {
        var (_, xml) = Filled(LabelVariant.GradeBlock);
        XElement grade = xml.Descendants("TextObject")
            .Single(element => element.Element("Name")!.Value == "TEKST_1");

        Assert.Equal("True", grade.Element("IsOutlined")!.Value);
        Assert.True(int.Parse(grade.Element("OutlineThickness")!.Value) >= 2);
        Assert.True(float.Parse(grade.Element("ObjectLayout")!.Element("Size")!.Element("Width")!.Value,
            System.Globalization.CultureInfo.InvariantCulture) > 0.2f);
    }

    [Fact]
    public void AnUnusedBarcodeObjectIsTakenOutOfTheFile()
    {
        // An empty barcode object is not nothing: DYMO's renderer answers it with a
        // minimal code, a stub under the first barcode. The spare objects are taken
        // out, so one code means one object.
        var (one, oneXml) = Filled(LabelVariant.Structured);
        Assert.Single(oneXml.Descendants("BarcodeObject"));
        Assert.DoesNotContain("BARCODE2", one, StringComparison.Ordinal);

        var (_, twoXml) = Filled(LabelVariant.Structured, mode: LabelBarcodeMode.Split);
        Assert.Equal(2, twoXml.Descendants("BarcodeObject").Count());

        var (_, noneXml) = Filled(LabelVariant.Structured, mode: LabelBarcodeMode.None);
        Assert.Empty(noneXml.Descendants("BarcodeObject"));
    }

    [Fact]
    public void TheObjectsMoveWhenASecondBarcodeTakesRoom()
    {
        // Two codes take a second band. Fixed positions would put the words under
        // it; the position sentinels move them down instead.
        var (one, oneXml) = Filled(LabelVariant.Structured);
        var (two, twoXml) = Filled(LabelVariant.Structured, mode: LabelBarcodeMode.Split);

        Assert.DoesNotContain("T1Y", one, StringComparison.Ordinal);
        Assert.DoesNotContain("B2Y", one, StringComparison.Ordinal);

        float oneTop = Top(oneXml, "TEKST_1");
        float twoTop = Top(twoXml, "TEKST_1");
        Assert.True(twoTop > oneTop,
            $"with two codes the words start at {twoTop} and with one at {oneTop}");

        // Both still end above the bottom of the label.
        float bottom = Top(twoXml, "TEKST_4") + Height(twoXml, "TEKST_4");
        Assert.True(bottom < 1.0567f, $"the locks line ends at {bottom} inches on a 0.9967 inch label");
    }

    [Fact]
    public void TheFilledFileNeverCarriesTheElementShorthand()
    {
        // DYMO's own deserializer rejects <X />; every element with no content must
        // arrive as <X> </X>. The one exception the format seems to want is the
        // margin thickness, which the shipped templates self-close.
        foreach (LabelVariant variant in Enum.GetValues<LabelVariant>())
        {
            var (text, _) = Filled(variant);
            string withoutThickness = System.Text.RegularExpressions.Regex.Replace(
                text, @"<DYMOThickness[^>]*/>", "");

            Assert.DoesNotContain("/>", withoutThickness, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ACustomTemplateReplacesEveryArrangement()
    {
        // A shop's own template is the layout. Importing it must not half-apply:
        // every arrangement reads it, so what the shop sees is what they get.
        string path = Path.Combine(Path.GetTempPath(), $"custom-{Guid.NewGuid():N}.dymo");
        File.WriteAllText(path, DymoTemplateFiles.Read(null, LabelVariant.Structured));
        try
        {
            foreach (LabelVariant variant in Enum.GetValues<LabelVariant>())
                Assert.Equal(path, DymoTemplateFiles.Locate(path, variant));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnUnlockedPhoneCarriesNoLocksObjectAtAll()
    {
        // The locks object is the one with the black band. Left in an unlocked
        // phone's file it prints as an empty black stripe, which is exactly what a
        // clean phone must not come out of the printer with.
        foreach (LabelVariant variant in new[] { LabelVariant.Structured, LabelVariant.GradeBlock })
        {
            var (text, xml) = Filled(variant, device: UnlockedPhone());

            Assert.DoesNotContain("FRP", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LOCKS", text, StringComparison.Ordinal);

            foreach (XElement textObject in xml.Descendants("TextObject"))
            {
                XElement background = textObject.Descendants("BackgroundBrush").First().Descendants("Color").First();
                Assert.False(background.Attribute("A")!.Value == "1" && background.Attribute("R")!.Value == "0",
                    $"{variant} keeps a black band on an unlocked phone");
            }
        }
    }

    [Fact]
    public void TheLocksBandHugsItsWords()
    {
        // The band sits around the codes, as the app's own drawing and the PDF draw
        // it, rather than as a rule across the whole roll.
        var (_, xml) = Filled(LabelVariant.Structured);

        float width = Component(xml, "TEKST_4", "Width");
        float left = Component(xml, "TEKST_4", "X");

        Assert.InRange(width, 0.3f, 3.0f);
        float centred = 0.23f + ((3.21f - width) / 2f);
        Assert.True(Math.Abs(left - centred) < 0.01f,
            $"the band starts at {left} and should be centred at {centred}");
    }

    private static DeviceData UnlockedPhone()
    {
        var phone = Phone();
        phone.FactoryResetProtection = PhoneGrade.Core.SecurityServices.FrpLockService.FrpLockStatus.Unlocked;
        phone.ActivationLock = PhoneGrade.Core.SecurityServices.ActivationLockService.ActivationLockStatus.Unlocked;
        return phone;
    }

    private static float Top(XDocument xml, string name) => Component(xml, name, "Y");
    private static float Height(XDocument xml, string name) => Component(xml, name, "Height");

    private static float Component(XDocument xml, string name, string component)
    {
        XElement layout = xml.Descendants("TextObject")
            .Single(element => element.Element("Name")!.Value == name)
            .Element("ObjectLayout")!;

        string value = component is "Y" or "X"
            ? layout.Element("DYMOPoint")!.Element(component)!.Value
            : layout.Element("Size")!.Element(component)!.Value;

        return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
