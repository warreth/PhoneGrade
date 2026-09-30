using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneGrade.Core;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Covers which address ends up in the QR code.
///
/// The routes themselves are covered elsewhere, against a real adb and a real
/// connector. What is pinned here is the order, the fact that a route which failed
/// cannot hide the ones behind it, and that an insecure address always carries the
/// reason, because those three are the whole point of the class.
/// </summary>
public class WebRunnerOriginResolverTests
{
    private const string Lan = "http://192.168.0.216:5055";
    private const string Loopback = "http://localhost:5055";
    private const string Internet = "https://quiet-marble-otter.trycloudflare.com";
    private const string Port = "5055";

    private const string PixelSerial = "38091FDJG00EMF";
    private const string IphoneUdid = "00008030-001234567890ABCD";

    /// <summary>
    /// Both routes record themselves, so a test can say what was asked and in which
    /// order instead of only what came back.
    /// </summary>
    private sealed class Routes
    {
        private readonly List<string> _calls = new();

        public bool UsbOpens { get; set; } = true;
        public string? InternetAddress { get; set; } = Internet;
        public Exception? UsbFailure { get; set; }
        public Exception? InternetFailure { get; set; }

        public IReadOnlyList<string> Calls
        {
            get { lock (_calls) return _calls.ToArray(); }
        }

        public WebRunnerOriginResolver Build() => new(
            (session, port) =>
            {
                lock (_calls) _calls.Add($"usb {session} {port}");
                if (UsbFailure != null) throw UsbFailure;
                return Task.FromResult(UsbOpens);
            },
            (port, _) =>
            {
                lock (_calls) _calls.Add($"internet {port}");
                if (InternetFailure != null) throw InternetFailure;
                return Task.FromResult(InternetAddress);
            });
    }

    private static Task<WebRunnerOrigin> Resolve(
        Routes routes, string session, bool secureOrigin = true, bool publicTunnel = true,
        Action<string>? onStatus = null) =>
        routes.Build().ResolveAsync(session, 5055, Lan, Loopback, secureOrigin, publicTunnel, onStatus);

    [Fact]
    public async Task Android_StaysOnUsbAndNeverOpensTheInternet()
    {
        var routes = new Routes();

        WebRunnerOrigin origin = await Resolve(routes, PixelSerial);

        Assert.Equal(Loopback, origin.Address);
        Assert.True(origin.IsSecure);
        Assert.Null(origin.Warning);
        Assert.Equal(new[] { $"usb {PixelSerial} {Port}" }, routes.Calls);
    }

    [Fact]
    public async Task IPhone_GoesToTheInternetWithoutBotheringAdb()
    {
        // adb has never heard of an iPhone, and asking it anyway costs a timeout on
        // every single device.
        var routes = new Routes();

        WebRunnerOrigin origin = await Resolve(routes, IphoneUdid);

        Assert.Equal(Internet, origin.Address);
        Assert.True(origin.IsSecure);
        Assert.Null(origin.Warning);
        Assert.Equal(new[] { $"internet {Port}" }, routes.Calls);
    }

    [Fact]
    public async Task Android_FallsBackToTheInternetWhenUsbFails()
    {
        // Losing the cable route should not cost the phone its camera.
        var routes = new Routes { UsbOpens = false };

        WebRunnerOrigin origin = await Resolve(routes, PixelSerial);

        Assert.Equal(Internet, origin.Address);
        Assert.True(origin.IsSecure);
        Assert.Null(origin.Warning);
        Assert.Equal(new[] { $"usb {PixelSerial} {Port}", $"internet {Port}" }, routes.Calls);
    }

    [Fact]
    public async Task IPhone_StatesWhyTheNetworkAddressIsNotEnough()
    {
        var routes = new Routes { InternetAddress = null };

        WebRunnerOrigin origin = await Resolve(routes, IphoneUdid);

        Assert.Equal(Lan, origin.Address);
        Assert.False(origin.IsSecure);
        Assert.Contains("de veilige verbinding via internet lukte niet", origin.Warning);
        Assert.Contains("Camera, microfoon en bewegingssensoren werken daardoor niet", origin.Warning);
    }

    [Fact]
    public async Task BothRoutesFailing_AreBothNamedInTheWarning()
    {
        var routes = new Routes { UsbOpens = false, InternetAddress = null };

        WebRunnerOrigin origin = await Resolve(routes, PixelSerial);

        Assert.Equal(Lan, origin.Address);
        Assert.Contains("de USB-tunnel", origin.Warning);
        Assert.Contains("de veilige verbinding via internet", origin.Warning);
        Assert.Contains("lukten niet", origin.Warning);
    }

    [Fact]
    public async Task TunnelSwitchedOff_SaysSoInsteadOfFailingQuietly()
    {
        // Nothing was even attempted, and the operator still needs to know why the
        // camera and motion steps are about to be unavailable.
        var routes = new Routes();

        WebRunnerOrigin origin = await Resolve(routes, IphoneUdid, publicTunnel: false);

        Assert.Equal(Lan, origin.Address);
        Assert.Empty(routes.Calls);
        Assert.Contains("De veilige verbinding staat uit", origin.Warning);
    }

    [Fact]
    public async Task DemoSession_GoesToTheNetworkWithoutOpeningAnything()
    {
        // Before a phone is plugged in the address only ever gets opened by the
        // technician's own browser, and a tunnel for it would publish the app for
        // nothing.
        var routes = new Routes();

        WebRunnerOrigin origin = await Resolve(routes, "DEMO");

        Assert.Equal(Lan, origin.Address);
        Assert.Null(origin.Warning);
        Assert.Empty(routes.Calls);
    }

    [Fact]
    public async Task SecureOriginSwitchedOff_GoesStraightToTheNetwork()
    {
        var routes = new Routes();

        WebRunnerOrigin origin = await Resolve(routes, PixelSerial, secureOrigin: false);

        Assert.Equal(Lan, origin.Address);
        Assert.False(origin.IsSecure);
        Assert.Null(origin.Warning);
        Assert.Empty(routes.Calls);
    }

    [Fact]
    public async Task ARouteThatThrows_DoesNotHideTheRouteBehindIt()
    {
        // adb is missing, or the connector could not be installed. The chain has to
        // carry on and report what it ended up with.
        var routes = new Routes { UsbFailure = new InvalidOperationException("adb is missing") };

        WebRunnerOrigin origin = await Resolve(routes, PixelSerial);

        Assert.Equal(Internet, origin.Address);
        Assert.True(origin.IsSecure);
        Assert.Null(origin.Warning);
    }

    [Fact]
    public async Task TheInternetRouteThrowing_StillProducesAnAddress()
    {
        var routes = new Routes { InternetFailure = new InvalidOperationException("no internet") };

        WebRunnerOrigin origin = await Resolve(routes, IphoneUdid);

        Assert.Equal(Lan, origin.Address);
        Assert.Contains("lukte niet", origin.Warning);
    }

    [Fact]
    public async Task StatusIsReportedWhileEachRouteIsBeingOpened()
    {
        // The tunnel can take a minute on a first run, and a QR code that sits there
        // without saying what it is doing is what makes people unplug the phone.
        var routes = new Routes { UsbOpens = false };
        var seen = new List<string>();

        await Resolve(routes, PixelSerial, onStatus: seen.Add);

        Assert.Equal(new[]
        {
            "Beveiligde verbinding via USB opzetten...",
            "Beveiligde verbinding via internet opzetten..."
        }, seen);
    }

    [Theory]
    [InlineData("DEMO", true)]
    [InlineData("demo", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("38091FDJG00EMF", false)]
    [InlineData("00008030-001234567890ABCD", false)]
    public void IsPlaceholderSession_OnlyCoversSessionsWithNoPhone(string? session, bool expected)
        => Assert.Equal(expected, WebRunnerOriginResolver.IsPlaceholderSession(session));
}
