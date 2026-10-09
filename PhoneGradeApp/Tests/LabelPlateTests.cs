using PhoneGrade.Core;
using Xunit;

namespace Tests;

// ============ The label plate: one drawing plan for PDF and preview ============
//
// Everything that can be decided without a drawing surface is decided here: which
// values are on the label, how many barcodes there are, the size of every line and
// how much empty paper sits above the content. The PDF writer and the on screen
// preview both read it, so these tests are where "the preview shows what the file
// carries" is actually kept.

public class LabelPlateTests
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

    private static LabelPlate Plate(
        LabelVariant variant = LabelVariant.Clean, int cyclesMinimum = 0,
        LabelBarcodeMode mode = LabelBarcodeMode.Identifier, DeviceData? device = null)
    {
        LabelFields fields = LabelFields.From(device ?? Phone());
        return LabelPlate.Build(fields, LabelLayout.Address, mode, LabelCodeSymbology.Code39, variant, cyclesMinimum);
    }

    private static string All(LabelPlate plate) =>
        string.Join(" | ", plate.Lines.Select(line => line.Text));

    [Fact]
    public void TheCleanPlateIsTheLabelAsItAlwaysWas_WithThePlaceholdersCleanedOff()
    {
        LabelPlate plate = Plate(LabelVariant.Clean);

        Assert.Equal(LabelVariant.Clean, plate.Variant);
        Assert.Contains("iPhone 13 Pro 256GB Graphite C 78% [X] BTW", All(plate), StringComparison.Ordinal);
        Assert.Contains("612 CYCLES", All(plate), StringComparison.Ordinal);
        Assert.Contains("1x NON-OEM", All(plate), StringComparison.Ordinal);
        Assert.Contains("FRP! ACT!", All(plate), StringComparison.Ordinal);

        // The locks on the plain arrangement are ordinary text; the black band is
        // for the arrangements that put them under the eye on purpose.
        Assert.False(plate.LocksInverted);
    }

    [Fact]
    public void TheStructuredPlateSeparatesWhatTheDeviceIsFromWhatItIsWorth()
    {
        LabelPlate plate = Plate(LabelVariant.Structured);

        string title = plate.Lines.First(line => line.Role == LabelLineRole.Title).Text;
        string meta = plate.Lines.First(line => line.Role == LabelLineRole.Meta).Text;

        Assert.Equal("iPhone 13 Pro 256GB", title);
        Assert.Contains("Graphite", meta, StringComparison.Ordinal);
        Assert.Contains("C", meta, StringComparison.Ordinal);
        Assert.Contains("78% [X]", meta, StringComparison.Ordinal);
        Assert.Contains("612", meta, StringComparison.Ordinal);
        Assert.Contains("BTW", meta, StringComparison.Ordinal);

        Assert.Contains(plate.Lines, line => line.Role == LabelLineRole.Faults && line.Text.Contains("NON-OEM"));
        Assert.Contains(plate.Lines, line => line.Role == LabelLineRole.Locks && line.Text == "FRP! ACT!");
        Assert.True(plate.LocksInverted, "the locks are not drawn white on black");
    }

    [Fact]
    public void TheGradeBlockCarriesTheGradeAsABlockLargerThanAnyLine()
    {
        LabelPlate plate = Plate(LabelVariant.GradeBlock);

        Assert.Equal("C", plate.Grade);
        Assert.True(plate.GradePoint > plate.BodyPoint,
            $"the grade is set at {plate.GradePoint}pt against a {plate.BodyPoint}pt body");

        // The locks still reach the plate; they sit below the block rather than
        // beside it, and a plate that forgot them would print a locked phone as a
        // free one.
        Assert.Contains(plate.Lines, line => line.Role == LabelLineRole.Locks && line.Text == "FRP! ACT!");
        Assert.True(plate.LocksInverted);
        Assert.True(plate.GradeBoxHeightMm > 0);
    }

    [Fact]
    public void TheLocksAreNeverSmallerThanTheBody_OnEveryArrangement()
    {
        // The locks are set larger than the block where the block has room; on a
        // small label where the body is already at the largest size the paper takes,
        // they hold that size rather than shrink below it. What they must never be is
        // the smallest thing on the label.
        foreach (LabelVariant variant in Enum.GetValues<LabelVariant>())
        {
            LabelPlate plate = Plate(variant);
            Assert.True(plate.LocksPoint >= plate.BodyPoint,
                $"{variant} sets the locks at {plate.LocksPoint}pt against a {plate.BodyPoint}pt body");
        }
    }

    [Fact]
    public void TheLockLinesNeverShareTheirLine_WithTheFaults()
    {
        // A lock that shares a line with the faults is a lock an operator reads
        // last, and it is the one line that can cost a shop the sale.
        foreach (LabelVariant variant in Enum.GetValues<LabelVariant>())
        {
            LabelPlate plate = Plate(variant);
            LabelLine locks = plate.Lines.First(line => line.Role == LabelLineRole.Locks);

            Assert.DoesNotContain("NON-OEM", locks.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("CYCLES", locks.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CyclesUnderTheFloorAreLeftOff_OnEveryArrangement()
    {
        // 300 cycles is not news on a phone with a healthy battery; 612 is. The
        // number is left off the label, never replaced by a placeholder.
        foreach (LabelVariant variant in Enum.GetValues<LabelVariant>())
        {
            DeviceData device = Phone();
            device.BatteryCycleCount = 300;

            Assert.DoesNotContain("300", All(Plate(variant, cyclesMinimum: 500, device: device)), StringComparison.Ordinal);
            Assert.DoesNotContain("CYCLES", All(Plate(variant, cyclesMinimum: 500, device: device)), StringComparison.Ordinal);

            device.BatteryCycleCount = 612;
            Assert.Contains("612", All(Plate(variant, cyclesMinimum: 500, device: device)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APlaceholderIsNeverPrintedAsAValue()
    {
        // NOMODEL on paper is a word a customer reads as part of what the shop is
        // selling. The panel blocks the print on the four that matter and the rest
        // are simply left off.
        var bare = new DeviceData();
        LabelPlate plate = Plate(LabelVariant.Structured, device: bare);

        Assert.DoesNotContain(DevicePlaceholders.Model, All(plate), StringComparison.Ordinal);
        Assert.DoesNotContain(DevicePlaceholders.Color, All(plate), StringComparison.Ordinal);
        Assert.DoesNotContain(DevicePlaceholders.PayMethod, All(plate), StringComparison.Ordinal);
        Assert.DoesNotContain(DevicePlaceholders.Battery, All(plate), StringComparison.Ordinal);

        LabelFields fields = LabelFields.From(bare);
        Assert.False(fields.HasRequiredForLabel);
        Assert.Contains(LabelField.Identifier, fields.MissingForLabel);
        Assert.Contains(LabelField.Model, fields.MissingForLabel);
        Assert.Contains(LabelField.Grade, fields.MissingForLabel);
        Assert.Contains(LabelField.Color, fields.MissingForLabel);
    }

    [Fact]
    public void AnOptionalValueThatWasNotReportedIsLeftOff_WithoutBlockingTheLabel()
    {
        // The payment method is not one of the four a label cannot do without: a
        // shop that does not record one gets a label without it, not a blocked
        // finish.
        DeviceData device = Phone();
        device.PayMethod = "";

        LabelFields fields = LabelFields.From(device);
        Assert.True(fields.HasRequiredForLabel);

        LabelPlate plate = LabelPlate.Build(fields, LabelLayout.Address, LabelBarcodeMode.Identifier);
        Assert.DoesNotContain("BTW", All(plate), StringComparison.Ordinal);
        Assert.DoesNotContain(DevicePlaceholders.PayMethod, All(plate), StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheBarcodeOffThePlateHasNoCodesAndTheWordsMoveUp()
    {
        // The barcode switch is a real off: no payload, no band, and the words take
        // the paper the codes would have used.
        LabelPlate withCodes = Plate(LabelVariant.Structured);
        LabelPlate without = Plate(LabelVariant.Structured, mode: LabelBarcodeMode.None);

        Assert.Equal(1, withCodes.BarcodeCount);
        Assert.Equal(0, without.BarcodeCount);
        Assert.Empty(without.Barred);
        Assert.True(without.BodyPoint >= withCodes.BodyPoint);
    }

    [Fact]
    public void ThePlateCarriesTheSymbologyTheBarsAreDrawnIn()
    {
        // A preview that quietly drew Code39 for a Code128 label would look like a
        // barcode and not be the one the printer prints.
        LabelFields fields = LabelFields.From(Phone());
        LabelPlate plate = LabelPlate.Build(
            fields, LabelLayout.Address, LabelBarcodeMode.Identifier, LabelCodeSymbology.Code128);

        Assert.Equal(LabelCodeSymbology.Code128, plate.Symbology);
    }

    [Fact]
    public void TheSamePlateIsBuiltFromTheSameValues_EveryTime()
    {
        // Determinism is what lets the panel and the settings preview share one
        // renderer: two builds that could differ are two previews.
        LabelFields fields = LabelFields.From(Phone());

        LabelPlate first = LabelPlate.Build(fields, LabelLayout.Address, LabelBarcodeMode.Identifier);
        LabelPlate second = LabelPlate.Build(fields, LabelLayout.Address, LabelBarcodeMode.Identifier);

        Assert.Equal(All(first), All(second));
        Assert.Equal(first.BodyPoint, second.BodyPoint);
        Assert.Equal(first.TopOffsetMm, second.TopOffsetMm);
    }
}
