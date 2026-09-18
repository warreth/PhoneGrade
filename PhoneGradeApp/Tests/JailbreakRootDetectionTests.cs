using Xunit;
using PhoneGrade.Core.SecurityServices;

namespace Tests;

public class JailbreakRootDetectionTests
{
    [Fact]
    public void JailbreakStatus_InitializesWithDefaults()
    {
        var status = new JailbreakDetectionService.JailbreakStatus();
        Assert.False(status.IsJailbroken);
        Assert.Empty(status.Evidence);
        Assert.Equal("Unknown", status.Confidence);
    }

    [Fact]
    public void JailbreakStatus_CanSetProperties()
    {
        var status = new JailbreakDetectionService.JailbreakStatus
        {
            IsJailbroken = true,
            Evidence = ["com.saurik.cydia", "io.sileo.app"],
            Confidence = "High"
        };

        Assert.True(status.IsJailbroken);
        Assert.Equal(2, status.Evidence.Count);
        Assert.Contains("com.saurik.cydia", status.Evidence);
        Assert.Equal("High", status.Confidence);
    }

    [Fact]
    public void RootStatus_InitializesWithDefaults()
    {
        var status = new RootDetectionService.RootStatus();
        Assert.False(status.IsRooted);
        Assert.Empty(status.Evidence);
        Assert.Equal("Unknown", status.Method);
    }

    [Fact]
    public void RootStatus_CanSetProperties()
    {
        var status = new RootDetectionService.RootStatus
        {
            IsRooted = true,
            Evidence = ["su command succeeded", "com.topjohnwu.magisk"],
            Method = "su"
        };

        Assert.True(status.IsRooted);
        Assert.Equal(2, status.Evidence.Count);
        Assert.Equal("su", status.Method);
    }

    [Fact]
    public void RootStatus_MultipleEvidenceSources()
    {
        var status = new RootDetectionService.RootStatus
        {
            IsRooted = true,
            Evidence = ["ro.secure=0", "ro.debuggable=1", "Root app: com.koushikdutta.superuser"],
            Method = "property"
        };

        Assert.True(status.IsRooted);
        Assert.Equal(3, status.Evidence.Count);
        Assert.Contains("ro.secure=0", status.Evidence);
    }
}
