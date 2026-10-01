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
        using HttpResponseMessage response = await FetchWhenReadyAsync(
            client, $"{address}/?sessionId=TUNNEL_E2E", TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(2));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The title carries a data-i18n attribute so the phone can rewrite it in
        // the reader's language, so the check is on the wording rather than on
        // the exact markup around it.
        Assert.Contains("PhoneGrade Test Suite</title>", body);

        await tunnel.StopAsync();
    }

    /// <summary>
    /// Fetches until the page comes back, or until there is no time left to wait.
    ///
    /// The address is published before the route behind it is settled, so the first
    /// attempts fail in two different ways: the name does not resolve or the
    /// connection is refused, which arrive as exceptions, and Cloudflare answers
    /// while it has nothing connected behind the name, which arrives as a response.
    /// Both are the route still being built rather than a broken page, so both are
    /// waited out. Past the deadline the answer is handed back as it is, so a page
    /// that is genuinely wrong fails the test instead of spinning.
    /// </summary>
    internal static async Task<HttpResponseMessage> FetchWhenReadyAsync(
        HttpClient client, string url, TimeSpan deadline, TimeSpan wait)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow + deadline;

        while (true)
        {
            HttpResponseMessage attempt;
            try
            {
                attempt = await client.GetAsync(url);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException)
                                       && DateTimeOffset.UtcNow < until)
            {
                await Task.Delay(wait);
                continue;
            }

            if (IsStillSettling(attempt.StatusCode) && DateTimeOffset.UtcNow < until)
            {
                attempt.Dispose();
                await Task.Delay(wait);
                continue;
            }

            return attempt;
        }
    }

    /// <summary>
    /// True while an answer says the route is not there yet.
    ///
    /// Only the edge's own 5xx counts: a page that answers 404 or 403 is the server
    /// behind the tunnel speaking, which means the route is up and the page is wrong.
    /// </summary>
    internal static bool IsStillSettling(HttpStatusCode status) =>
        (int)status >= 500 && (int)status <= 599;
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

/// <summary>
/// Covers the wait policy of the end-to-end fetch on its own, against answers that
/// are handed over instead of waited for.
///
/// The real run only reaches that code when a connector is installed, and even
/// then Cloudflare does not reliably repeat the moment where it answers for a
/// tunnel that has nothing connected behind it, which is exactly the moment the
/// policy has to get right.
/// </summary>
public class QuickTunnelReadinessTests
{
    [Fact]
    public void AnEdgeErrorIsTheRouteStillBeingBuiltAndAPageIsNot()
    {
        Assert.True(QuickTunnelEndToEndTests.IsStillSettling((HttpStatusCode)530));
        Assert.True(QuickTunnelEndToEndTests.IsStillSettling(HttpStatusCode.ServiceUnavailable));
        Assert.False(QuickTunnelEndToEndTests.IsStillSettling(HttpStatusCode.OK));
        Assert.False(QuickTunnelEndToEndTests.IsStillSettling(HttpStatusCode.NotFound));
        Assert.False(QuickTunnelEndToEndTests.IsStillSettling(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task APageBehindAnEdgeErrorIsWaitedOutUntilItComesBack()
    {
        var handler = new SequenceHandler((HttpStatusCode)530, (HttpStatusCode)530, HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await QuickTunnelEndToEndTests.FetchWhenReadyAsync(
            client, "https://example.test/", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task AnEdgeErrorThatNeverClearsIsHandedBackWhenThereIsNoTimeLeft()
    {
        // Giving up has to arrive as an answer rather than as a loop, so a page
        // that is genuinely broken still fails instead of spinning forever.
        var handler = new SequenceHandler((HttpStatusCode)530);
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await QuickTunnelEndToEndTests.FetchWhenReadyAsync(
            client, "https://example.test/", TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(5));

        Assert.Equal((HttpStatusCode)530, response.StatusCode);
        Assert.True(handler.Requests >= 2,
            $"The edge error should have been waited out, only {handler.Requests} request(s) were made.");
    }

    /// <summary>Hands out the answers it was given and counts what was asked.</summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _answers;
        private int _next;

        public int Requests { get; private set; }

        public SequenceHandler(params HttpStatusCode[] answers) => _answers = answers;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            HttpStatusCode answer = _answers[Math.Min(_next, _answers.Length - 1)];
            _next++;

            return Task.FromResult(new HttpResponseMessage(answer)
            {
                Content = new StringContent("")
            });
        }
    }
}
