using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Web;
using Xunit;

namespace PhoneGrade.UI.Tests.Web;

/// <summary>
/// Runs the connector the way the application does and asks the address it prints
/// for the test page.
///
/// The other tests here prove that an address is read off a pipe and that the
/// decision behind it is right; only this one proves that the whole chain, from
/// finding the connector through Cloudflare and back to the embedded server, ends
/// up serving the page the phone is going to open.
///
/// It needs a connector on disk, so it reports itself as skipped when there is
/// none rather than passing without having done anything. Installing one is a
/// download, and a test that pulls fifty megabytes every run is a test people turn
/// off.
/// </summary>
public class QuickTunnelEndToEndTests : IAsyncLifetime
{
    private TestRunnerServer? _server;

    public async Task InitializeAsync()
    {
        _server = new TestRunnerServer(6133);
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_server != null) await _server.DisposeAsync();
    }

    [ConnectorInstalledFact]
    public async Task TheAddressTheConnectorPrints_ServesTheTestPage()
    {
        using var tunnel = QuickTunnel.CreateDefault();

        string? address = await tunnel.StartAsync(_server!.BoundPort);

        Assert.NotNull(address);
        Assert.StartsWith("https://", address);
        Assert.DoesNotContain(":", address.Substring("https://".Length));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // The connector prints the address before Cloudflare has finished pointing
        // it at this machine, and its own banner says it may take some time to be
        // reachable. The phone gets there only after a QR code has been scanned and
        // read, which is longer than this, so the delay is waited out rather than
        // reported as a failure.
        HttpResponseMessage? response = null;
        string body = "";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);

        while (response == null)
        {
            try
            {
                response = await client.GetAsync($"{address}/?sessionId=TUNNEL_E2E");
                body = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException)
                                       && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(2000);
            }
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The title carries a data-i18n attribute so the phone can rewrite it in
        // the reader's language, so the check is on the wording rather than on
        // the exact markup around it.
        Assert.Contains("PhoneGrade Test Suite</title>", body);

        await tunnel.StopAsync();
    }
}

/// <summary>
/// A fact that reports itself as skipped, with a reason, when the connector is not
/// installed. Skipping is visible in the run; a test that quietly returns would not
/// be.
/// </summary>
public sealed class ConnectorInstalledFactAttribute : FactAttribute
{
    public ConnectorInstalledFactAttribute()
    {
        if (!ConnectorIsInstalled())
        {
            Skip = "No tunnel connector installed next to the tools or on the system path.";
        }
    }

    private static bool ConnectorIsInstalled()
    {
        try
        {
            string local = Path.Combine(ToolRunner.ToolsDir, ToolInstallerService.CloudflaredExecutableName);
            if (File.Exists(local)) return true;

            string resolved = ToolRunner.Resolve("cloudflared");
            return !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
        }
        catch
        {
            return false;
        }
    }
}
