using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.Tests;

// ============ Whether DYMO's own software can take the label ============
//
// The answer decides how the finish screen hands the label over: opened in
// DYMO's software when it is installed, through the label PDF when it is not.
// The machine probe itself is environment, so what is pinned here is the
// naming rule that tells DYMO's label software from everything else, including
// this app's own old AutoDymoLabel install.

public class DymoSoftwareTests
{
    [Theory]
    [InlineData("DYMO Connect for Desktop")]
    [InlineData("DYMO Connect")]
    [InlineData("DYMO Label v.8")]
    [InlineData("DYMO ID 1.5.1")]
    [InlineData("dymo connect")]
    public void TheNamesDymoInstallsItsLabelSoftwareUnder_AreRecognised(string name)
        => Assert.True(DymoSoftware.LooksLikeDymoSoftware(name));

    [Theory]
    [InlineData("AutoDymoLabel")]          // this app's own name until v5
    [InlineData("PhoneGrade")]
    [InlineData("DymoPrint queue helper")] // DYMO without one of its product words
    [InlineData("DYMO LabelWriter drivers")] // a driver package has no .dymo handler
    [InlineData("LabelWriter")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElse_IsNotDymoSoftware(string? name)
        => Assert.False(DymoSoftware.LooksLikeDymoSoftware(name));
}
