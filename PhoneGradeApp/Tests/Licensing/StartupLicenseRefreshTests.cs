using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PhoneGrade.Core.Licensing;
using PhoneGrade.Tests;
using PhoneGrade.UI.Services;
using PhoneGrade.UI.ViewModels;
using Tests;
using Xunit;
using static Tests.LicensingTestContext;

namespace PhoneGrade.UI.Tests.Web;

// ============ The licence check at startup ============
//
// The gate was asked only when a scan started, so a shop with a stored Pro seat
// saw "Free tier" on every launch until it scanned, which read as if the key had
// been forgotten. One check at startup answers it before anything is touched;
// a machine with no key never reaches the network.

[Collection(LanguageCollection.Name)]
public class StartupLicenseRefreshTests : IDisposable
{
    private readonly SettingsScratch _settings;

    public StartupLicenseRefreshTests()
    {
        // A stored key with this computer's seat, the way a machine that activated
        // earlier carries it: the startup check has to find it on its own.
        string token = TrialStateCipher.Encrypt(
            new TrialState { LicenseKey = "KEY-HELD", InstanceId = "992" });
        _settings = new SettingsScratch(
            $"{{\"Theme\":\"Dark\",\"IntroSeen\":true,\"TrialToken\":\"{token}\"}}");
    }

    public void Dispose() => _settings.Dispose();

    [AvaloniaFact]
    public void AStoredSeatShowsProWithoutWaitingForAScan()
    {
        LocalizationManager.SetLanguage("en");
        using var server = new FakeLicenseServer { ResponseJson = FakeLicenseServer.SeatHeldJson };
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        PumpUntil(() => vm.IsProLicenseActive, "the stored seat turns the app Pro at startup");

        Assert.True(vm.IsProLicenseActive);
        // The seat this computer named rather than the bare key: the same call the
        // panel's own refresh makes, so a startup check cannot burn a second seat.
        Assert.Equal(new[] { "992" }, server.NamedInstanceIds);
    }

    [AvaloniaFact]
    public void AMachineWithNoKeyNeverAsksTheVendor()
    {
        using var bare = new SettingsScratch("""{"Theme":"Dark","IntroSeen":true}""");
        using var server = new FakeLicenseServer();
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));

        // Room for the fire-and-forget path to run before saying it did nothing.
        Drain(400);
        Assert.Equal(0, server.CallCount);
    }

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            Thread.Sleep(10);
        }
        Assert.True(condition(), $"timed out waiting for: {what}");
    }

    private static void Drain(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
