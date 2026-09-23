using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using PhoneGrade.UI.Web;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;
using Avalonia.Threading;
using System.Linq;

namespace Tests;

public class CapabilityScannerTests
{
    [Fact]
    public async Task LogWarningEndpoint_ParsesWarningAndRaisesEvent()
    {
        var server = new TestRunnerServer(5099);
        await server.StartAsync();

        bool eventRaised = false;
        string? reportedApi = null;
        string? reportedSession = null;

        server.MissingApiReported += (s, e) =>
        {
            eventRaised = true;
            reportedApi = e.MissingApi;
            reportedSession = e.SessionId;
        };

        using var client = new HttpClient();
        var content = new StringContent("{\"sessionId\":\"TEST123\",\"missingApi\":\"wakeLock\",\"userAgent\":\"TestAgent\",\"osVersion\":\"iOS 16.0\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("http://127.0.0.1:5099/api/pwa/log-warning", content);

        response.EnsureSuccessStatusCode();
        Assert.True(eventRaised);
        Assert.Equal("wakeLock", reportedApi);
        Assert.Equal("TEST123", reportedSession);
    }
}
