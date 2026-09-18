using Xunit;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

public class ImeiApiServiceTests
{
    [Fact]
    public async Task CheckActivationLockAsync_NoApiKey_ReturnsUnknown()
    {
        // Ensure no API key is set
        Environment.SetEnvironmentVariable("IMEI_API_KEY", null);
        Environment.SetEnvironmentVariable("IMEI_API_PROVIDER", null);

        var result = await ImeiApiService.CheckActivationLockAsync("123456789012345");

        Assert.Equal("Unknown", result.Status);
        Assert.Contains("niet geconfigureerd", result.Message);
    }

    [Fact]
    public async Task CheckActivationLockAsync_InvalidProvider_ReturnsUnknown()
    {
        Environment.SetEnvironmentVariable("IMEI_API_KEY", "test_key");
        Environment.SetEnvironmentVariable("IMEI_API_PROVIDER", "invalid_provider");

        var result = await ImeiApiService.CheckActivationLockAsync("123456789012345");

        Assert.Equal("Unknown", result.Status);
        Assert.Contains("Onbekende provider", result.Message);
        
        // Cleanup
        Environment.SetEnvironmentVariable("IMEI_API_KEY", null);
        Environment.SetEnvironmentVariable("IMEI_API_PROVIDER", null);
    }

    [Fact]
    public void ActivationLockApiResult_InitializesWithDefaults()
    {
        var result = new ActivationLockApiResult();
        
        Assert.Equal("Unknown", result.Status);
        Assert.Null(result.CarrierLock);
        Assert.Null(result.Blacklisted);
        Assert.Null(result.Message);
        Assert.Equal("Unknown", result.Source);
    }

    [Fact]
    public void ActivationLockApiResult_CanSetProperties()
    {
        var result = new ActivationLockApiResult
        {
            Status = "ON",
            CarrierLock = "Locked",
            Blacklisted = "No",
            Message = "Find My iPhone is enabled",
            Source = "sickw"
        };

        Assert.Equal("ON", result.Status);
        Assert.Equal("Locked", result.CarrierLock);
        Assert.Equal("No", result.Blacklisted);
        Assert.NotNull(result.Message);
        Assert.Equal("sickw", result.Source);
    }

    [Fact]
    public void ActivationLockApiResult_BlacklistedScenario()
    {
        var result = new ActivationLockApiResult
        {
            Status = "OFF",
            Blacklisted = "Yes",
            Message = "Device reported stolen",
            Source = "imeipro"
        };

        Assert.Equal("OFF", result.Status);
        Assert.Equal("Yes", result.Blacklisted);
        Assert.Contains("stolen", result.Message);
    }
}
