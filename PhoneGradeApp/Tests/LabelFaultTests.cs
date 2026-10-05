using System.Collections.Generic;
using System.Linq;
using PhoneGrade.Core;
using PhoneGrade.Core.SecurityServices;
using Xunit;

namespace Tests;

// ============ What the label says is wrong with the phone ============
//
// The label is a shop tag. Whoever takes the phone next cannot open the report, so
// a non-OEM screen, a failed test or an active lock has to be on the paper or it
// is not on the label at all. These hold the reduction to short codes, because a
// label has room for three characters of a word and the report has the rest.

public class LabelFaultTests
{
    private static DeviceData Clean() => new()
    {
        Identifier = "356938035643809", Model = "13 Pro", Color = "Wit",
        Storage = "256GB", BatteryHealth = "90", Quality = "A", PayMethod = "Marge",
        BatteryCycleCount = 84,
    };

    [Fact]
    public void APhoneWithNothingWrongHasNoFaultsToReport()
    {
        // Silence is the message. A label reading "OK" spends four characters of a
        // short line to say what its own emptiness already says. The charge count
        // is not a fault, so a clean phone still carries it.
        LabelFaults faults = LabelFaultReader.From(Clean());

        Assert.True(faults.IsClean);
        Assert.Equal("", faults.Summary);
        Assert.Equal("", faults.LockLine);
        Assert.Equal("84 CYCLES", LabelLayout.DetailLine(LabelFields.From(Clean())));
    }

    [Fact]
    public void NonOriginalPartsAreCountedRatherThanListed()
    {
        // A count is shorter than a list and faster to read, and a phone with four
        // mismatched parts cannot fit four names on the paper.
        var phone = Clean();
        phone.ComponentChecks =
        [
            new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch },
            new ComponentStatus { Name = "Batterij", Status = ComponentStatusType.Mismatch },
            new ComponentStatus { Name = "Achtercamera", Status = ComponentStatusType.Mismatch },
            new ComponentStatus { Name = "Speaker", Status = ComponentStatusType.Mismatch },
            new ComponentStatus { Name = "Frontcamera", Status = ComponentStatusType.Match },
        ];

        LabelFaults faults = LabelFaultReader.From(phone);

        Assert.Equal(4, faults.NotOriginal.Count);
        Assert.Contains("4x NON-OEM", faults.Summary);
        Assert.DoesNotContain("Scherm", faults.Summary);
    }

    [Fact]
    public void APartThatCouldNotBeCheckedIsNotCountedAsAFault()
    {
        // An unread serial is a gap in the report, not a fault on the phone, and
        // saying "2x NON-OEM" on a phone whose second screen was never read is a
        // claim the shop cannot back up.
        var phone = Clean();
        phone.ComponentChecks =
        [
            new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch },
            new ComponentStatus { Name = "Batterij", Status = ComponentStatusType.Unknown },
            new ComponentStatus { Name = "Speaker", Status = ComponentStatusType.Passed },
        ];

        LabelFaults faults = LabelFaultReader.From(phone);

        Assert.Single(faults.NotOriginal);
        Assert.Contains("1x NON-OEM", faults.Summary);
    }

    [Fact]
    public void FailedTestsAreOnTheLabelUnderTheirOwnShortCodes()
    {
        var phone = Clean();
        phone.InteractiveTests = new InteractiveTestSuiteResult
        {
            SessionId = "S",
            Tests =
            [
                new InteractiveTestResult { Id = "touch", Name = "Touchscreen", Status = TestStatus.Passed },
                new InteractiveTestResult { Id = "camera", Name = "Camera achter", Status = TestStatus.Failed },
                new InteractiveTestResult { Id = "mic", Name = "Microfoon", Status = TestStatus.Failed },
                new InteractiveTestResult { Id = "vib", Name = "Trilmotor", Status = TestStatus.Skipped },
            ],
        };

        LabelFaults faults = LabelFaultReader.From(phone);

        Assert.Equal(["Camera achter", "Microfoon"], faults.FailedTests);
        Assert.Equal("CAMERA MICROF", faults.Summary);
        Assert.DoesNotContain("TOUCHSCREEN", faults.Summary);
        Assert.DoesNotContain("TRILMOTOR", faults.Summary);
    }

    [Theory]
    [InlineData(TestStatus.Passed, false)]
    [InlineData(TestStatus.Failed, true)]
    [InlineData(TestStatus.Skipped, false)]
    public void OnlyAFailedTestIsAFault(TestStatus status, bool expected)
    {
        var phone = Clean();
        phone.InteractiveTests = new InteractiveTestSuiteResult
        {
            SessionId = "S",
            Tests = [new InteractiveTestResult { Id = "x", Name = "Test", Status = status }],
        };

        Assert.Equal(expected, !LabelFaultReader.From(phone).FailedTests.Count.Equals(0));
    }

    [Fact]
    public void ALockedPhoneCarriesEveryLockItHas_AndTheyAreNotCountedAsFaults()
    {
        // The three of them together are what makes a phone unsellable, and they get
        // a line of their own rather than a share of the fault line, because they are
        // the one thing an operator must not skim past.
        var phone = Clean();
        phone.FactoryResetProtection = FrpLockService.FrpLockStatus.Locked;
        phone.ActivationLock = ActivationLockService.ActivationLockStatus.Locked;
        phone.CarrierLockIOS = new ActivationLockService.CarrierLockStatus { IsCarrierLocked = true };
        phone.Blacklist = new BlacklistCheckService.BlacklistStatus { IsBlacklisted = true };

        LabelFaults faults = LabelFaultReader.From(phone);

        Assert.Equal(
            [FaultCodes.Frp, FaultCodes.ActivationLock, FaultCodes.CarrierLock, FaultCodes.Blacklisted],
            faults.Locks);
        Assert.Equal("FRP! ACT! CARRIER! BLACKLIST!", faults.LockLine);

        // Not repeated in the fault line: the two lines sit on the label together.
        Assert.Equal("", faults.FaultsOnly);
        Assert.DoesNotContain("FRP", faults.FaultsOnly);
    }

    [Fact]
    public void AnUnknownLockIsNotPrinted()
    {
        // Unknown means the phone would not say. Printing FRP! for a phone that
        // refused to answer costs a sale on a phone that was fine.
        var phone = Clean();
        phone.FactoryResetProtection = FrpLockService.FrpLockStatus.Unknown;
        phone.ActivationLock = ActivationLockService.ActivationLockStatus.Unknown;

        LabelFaults faults = LabelFaultReader.From(phone);

        Assert.Empty(faults.Locks);
    }

    [Fact]
    public void AnUnlockedPhoneSaysNothingAboutLocks()
    {
        var phone = Clean();
        phone.FactoryResetProtection = FrpLockService.FrpLockStatus.Unlocked;
        phone.ActivationLock = ActivationLockService.ActivationLockStatus.Unlocked;

        Assert.Empty(LabelFaultReader.From(phone).Locks);
    }

    [Fact]
    public void ALockIsNotSomethingAnOperatorCanSwitchOff()
    {
        // The tests and the locks have a switch between them and the locks do not:
        // there is no setting that puts a FRP-locked phone on a clean label.
        var phone = Clean();
        phone.FactoryResetProtection = FrpLockService.FrpLockStatus.Locked;
        phone.ComponentChecks = [new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch }];

        LabelFaults faults = LabelFaultReader.From(phone, tests: false, locks: false);

        Assert.Equal(1, faults.NotOriginal.Count);
        Assert.Empty(faults.Locks);
    }

    [Fact]
    public void TheChargeCountIsOnTheLabel_BecauseAPercentageAloneMisleads()
    {
        // A battery at 90 percent after nine hundred charges is worse than one at
        // 80 percent after fifty, and the percentage cannot tell the two apart.
        Assert.Equal("612 CYCLES", LabelLayout.DetailLine(LabelFields.From(
            new DeviceData { BatteryCycleCount = 612, BatteryHealth = "90" })));

        Assert.Equal(DevicePlaceholders.BatteryCycles, LabelFields.From(
            new DeviceData { BatteryHealth = "90" }).BatteryCycles);
    }

    [Fact]
    public void AChargeCountOfZeroIsTreatedAsNotReported()
    {
        // Zero is what an unset field reads as, and a label claiming zero charges
        // is a claim about a phone nobody has measured.
        LabelFields fields = LabelFields.From(new DeviceData { BatteryCycleCount = 0 });

        Assert.Equal(DevicePlaceholders.BatteryCycles, fields.BatteryCycles);
    }

    [Fact]
    public void EveryFaultWearsACodeShortEnoughForThePaper()
    {
        // Six characters is the limit, and it is the reason the report has to be
        // opened to learn which test failed: the label can only say CAMERA.
        Assert.Equal("CAMERA", FaultCodes.Short("Camera achter"));
        Assert.Equal("TOUCHS", FaultCodes.Short("Touchscreen"));
        Assert.Equal("MICROF", FaultCodes.Short("Microfoon"));
        Assert.Equal("A1B2", FaultCodes.Short("a-1_b 2"));

        foreach (string name in new[] { "Camera achter", "Touchscreen", "Battery" })
            Assert.True(FaultCodes.Short(name).Length <= 6, $"{name} does not shorten to six characters");
    }

    [Fact]
    public void TheDetailLineCarriesTheCountAndTheFaults_AndNotTheLocks()
    {
        var phone = Clean();
        phone.BatteryCycleCount = 300;
        phone.FactoryResetProtection = FrpLockService.FrpLockStatus.Locked;
        phone.ComponentChecks = [new ComponentStatus { Name = "Scherm", Status = ComponentStatusType.Mismatch }];

        LabelFields fields = LabelFields.From(phone);
        string detail = LabelLayout.DetailLine(fields);

        Assert.Equal("300 CYCLES 1x NON-OEM", detail);
        Assert.DoesNotContain("FRP", detail);
        Assert.Equal("FRP!", LabelLayout.LockLine(fields));
    }

    [Fact]
    public void ALabelSaysHowManyLinesItHas_SoThePreviewCanReserveThem()
    {
        var clean = LabelFields.From(Clean());
        Assert.Equal(2, LabelLayout.Lines(clean));

        var locked = Clean();
        locked.FactoryResetProtection = FrpLockService.FrpLockStatus.Locked;
        LabelFields faults = LabelFields.From(locked);
        Assert.Equal(3, LabelLayout.Lines(faults));

        var phone = Clean();
        phone.BatteryCycleCount = null;
        LabelFields bare = LabelFields.From(phone);
        Assert.Equal(1, LabelLayout.Lines(bare));
    }
}
