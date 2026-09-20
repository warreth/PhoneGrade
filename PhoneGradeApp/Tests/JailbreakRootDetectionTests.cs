using PhoneGrade.Core.SecurityServices;
using Xunit;

namespace Tests;

public class RootDetectionTests
{
    [Fact]
    public void RootStatus_InitializesWithDefaults()
    {
        var status = new RootDetectionService.RootStatus();
        Assert.False(status.IsRooted);
        Assert.NotNull(status.Evidence);
        Assert.Empty(status.Evidence);
        Assert.Equal("Unknown", status.Method);
    }

    [Fact]
    public void RootStatus_CanSetProperties()
    {
        var status = new RootDetectionService.RootStatus
        {
            IsRooted = true,
            Method = "su binary found",
            Evidence = new() { "/system/bin/su", "Magisk installed" }
        };

        Assert.True(status.IsRooted);
        Assert.Equal("su binary found", status.Method);
        Assert.Equal(2, status.Evidence.Count);
    }
}
