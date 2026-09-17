using Xunit;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

public class BlacklistServiceTests
{
    [Fact]
    public void BlacklistStatus_InitializesWithDefaults()
    {
        var status = new BlacklistCheckService.BlacklistStatus();
        Assert.False(status.IsBlacklisted);
        Assert.Null(status.Reason);
        Assert.Equal("Unknown", status.Source);
    }

    [Fact]
    public void BlacklistStatus_CanSetProperties()
    {
        var status = new BlacklistCheckService.BlacklistStatus
        {
            IsBlacklisted = true,
            Reason = "Reported lost or stolen",
            Source = "GSMA"
        };

        Assert.True(status.IsBlacklisted);
        Assert.Equal("Reported lost or stolen", status.Reason);
        Assert.Equal("GSMA", status.Source);
    }

    [Fact]
    public async Task NoOpBlacklistProvider_ReturnsUnknown()
    {
        var provider = new BlacklistCheckService.NoOpBlacklistProvider();
        var status = await provider.CheckAsync("123456789012345");

        Assert.False(status.IsBlacklisted);
        Assert.Equal("NoOp", status.Source);
    }

    [Fact]
    public async Task NoOpBlacklistProvider_WorksWithEmptyIMEI()
    {
        var provider = new BlacklistCheckService.NoOpBlacklistProvider();
        var status = await provider.CheckAsync("");

        Assert.False(status.IsBlacklisted);
        Assert.Equal("NoOp", status.Source);
    }

    [Fact]
    public void BlacklistStatus_CleanDevice()
    {
        var status = new BlacklistCheckService.BlacklistStatus
        {
            IsBlacklisted = false,
            Reason = null,
            Source = "GSMA"
        };

        Assert.False(status.IsBlacklisted);
        Assert.Null(status.Reason);
    }

    [Fact]
    public void BlacklistStatus_BlacklistedDevice()
    {
        var status = new BlacklistCheckService.BlacklistStatus
        {
            IsBlacklisted = true,
            Reason = "Reported stolen",
            Source = "GSMA"
        };

        Assert.True(status.IsBlacklisted);
        Assert.NotNull(status.Reason);
    }
}
