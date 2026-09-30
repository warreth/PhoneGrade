using PhoneGrade.UI.Services;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers the addresses the QR code is built from.
///
/// The scheme is the entire reason an address is chosen, so it is asserted here
/// rather than left to a string built elsewhere: a https address with a port on it,
/// or a LAN address without one, produces a page the phone cannot open.
/// </summary>
public class WebRunnerAddressTests
{
    [Fact]
    public void SessionUrl_CarriesASecureAddressWithoutAPort()
    {
        // The tunnel terminates on Cloudflare's 443, so adding our own port would
        // send the phone to a service that is not listening.
        string url = QrCodeService.GenerateSessionUrl("https://quiet-marble-otter.trycloudflare.com", "00008030-001234567890ABCD");

        Assert.Equal("https://quiet-marble-otter.trycloudflare.com/?sessionId=00008030-001234567890ABCD", url);
    }

    [Fact]
    public void SessionUrl_TrimsTheSlashOffAnAddressThatAlreadyHasOne()
    {
        string url = QrCodeService.GenerateSessionUrl("https://quiet-marble-otter.trycloudflare.com/", "X");

        Assert.Equal("https://quiet-marble-otter.trycloudflare.com/?sessionId=X", url);
    }

    [Fact]
    public void SessionUrl_KeepsTheDebugAndPhoneNumberFlags()
    {
        string url = QrCodeService.GenerateSessionUrl("https://a-b-c.trycloudflare.com", "X", isDebug: true, testPhoneNumber: "0470 12 34 56");

        Assert.Equal("https://a-b-c.trycloudflare.com/?sessionId=X&debug=true&testPhoneNumber=0470%2012%2034%2056", url);
    }

    [Fact]
    public void SessionUrl_FallsBackToTheLocalServerWithoutAnAddress()
    {
        Assert.Equal("http://127.0.0.1:5055/?sessionId=X",
            QrCodeService.GenerateSessionUrl((string?)null!, "X"));
        Assert.Equal("http://127.0.0.1:5055/?sessionId=X",
            QrCodeService.GenerateSessionUrl("   ", "X"));
    }

    [Fact]
    public void LoopbackAddress_IsTheLocalhostAdbReverseMapsOntoThePhone()
    {
        Assert.Equal("http://localhost:5055", QrCodeService.LoopbackAddress(5055));
    }

    [Fact]
    public void NetworkAddress_IsHttpOnTheMachinesOwnIPv4Address()
    {
        Assert.Matches(@"^http://\d{1,3}(\.\d{1,3}){3}:5056$", QrCodeService.NetworkAddress(5056));
    }

    [Theory]
    [InlineData("https://quiet-marble-otter.trycloudflare.com", true)]
    [InlineData("https://192.168.0.216:5055", true)]
    [InlineData("http://localhost:5055", true)]
    [InlineData("http://127.0.0.1:5055", true)]
    [InlineData("http://[::1]:5055", true)]
    [InlineData("http://192.168.0.216:5055", false)]
    [InlineData("http://phonegrade.local:5055", false)]
    [InlineData("http://127.0.0.1.nip.io:5055", false)]
    [InlineData("/?sessionId=X", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSecureAddress_SaysWhetherTheRestrictedApisWillBeThere(string? address, bool expected)
        => Assert.Equal(expected, QrCodeService.IsSecureAddress(address));

    [Theory]
    [InlineData("http://localhost:5055", true)]
    [InlineData("http://127.0.0.1:5055", true)]
    [InlineData("http://[::1]:5055", true)]
    [InlineData("https://localhost:5055", true)]
    [InlineData("http://192.168.0.216:5055", false)]
    [InlineData("https://quiet-marble-otter.trycloudflare.com", false)]
    [InlineData(null, false)]
    public void IsLoopbackAddress_SaysWhetherTheAddressWentOverUsb(string? address, bool expected)
        => Assert.Equal(expected, QrCodeService.IsLoopbackAddress(address));
}
