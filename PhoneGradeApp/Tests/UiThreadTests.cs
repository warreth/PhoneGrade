using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using PhoneGrade.Core;
using PhoneGrade.Core.Licensing;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

// ============ Async work that updates the screen comes back to the UI thread ====
//
// The licensing gate and the activation call answer asynchronously (a cached key
// is instant, a network call is not), and everything after them touches bound
// state: Busy, the status line, the workflow, the seat counters. That state feeds
// the CanExecute of bound commands, and Avalonia answers a command change from
// the wrong thread with "Call from invalid thread": the command pipeline errors,
// nobody observes it, and the default handler ends the process. These pin the two
// flows to the UI thread however their continuations land.

public class UiThreadTests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), $"ui-thread-{Guid.NewGuid():N}");
    private readonly string? _origOverride = Environment.GetEnvironmentVariable("AUTODYMO_SETTINGS_DIR");
    private readonly Func<Task<ScanAuthorization>>? _origGate = DeviceService.ScanGate;

    public UiThreadTests()
    {
        Directory.CreateDirectory(_settingsDir);
        Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _settingsDir);
        File.WriteAllText(Path.Combine(_settingsDir, "settings.json"), """{"Theme":"Dark","IntroSeen":true}""");
    }

    [AvaloniaFact]
    public async Task TheScanGate_BringsItsUiWorkBackToTheUiThread()
    {
        using var vm = new MainWindowViewModel();
        try
        {
            // A gate that completes on a pool thread, the way the network
            // validation does. A cached answer completes inline and hides this.
            DeviceService.ScanGate = async () =>
            {
                await Task.Delay(75).ConfigureAwait(false);
                return ScanAuthorization.AllowedPro;
            };

            int uiThread = Environment.CurrentManagedThreadId;
            var raiseThreads = new List<int>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainWindowViewModel.IsProLicenseActive)
                    or nameof(MainWindowViewModel.TrialScanCount)
                    or nameof(MainWindowViewModel.IsTrialLimitReached)
                    or nameof(MainWindowViewModel.LicensingStatusText))
                {
                    raiseThreads.Add(Environment.CurrentManagedThreadId);
                }
            };

            // Started from a pool thread, the way a watcher callback or a stray
            // continuation can start it. The refresh must land on the UI thread
            // all the same.
            bool proceed = await Task.Run(async () => await vm.PassScanGateAsync());

            Assert.True(proceed);
            Assert.NotEmpty(raiseThreads);
            Assert.All(raiseThreads, thread =>
                Assert.True(thread == uiThread,
                    $"the scan gate refreshed the screen from thread {thread} instead of the UI thread {uiThread}"));
        }
        finally
        {
            DeviceService.ScanGate = _origGate;
        }
    }

    [AvaloniaFact]
    public async Task ActivatingALicence_BringsItsUiWorkBackToTheUiThread()
    {
        // A transport that answers on a pool thread, the way the real one does,
        // rather than the synchronous fake the licensing tests use.
        var server = new SlowHandler(new LicensingTestContext.FakeLicenseServer());
        using var vm = new MainWindowViewModel(new LemonSqueezyClient(server));
        vm.Licensing!.LicenseKeyInput = "PG-TEST-KEY";

        int uiThread = Environment.CurrentManagedThreadId;
        var raiseThreads = new List<int>();
        vm.Licensing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LicensingViewModel.StatusMessage))
                raiseThreads.Add(Environment.CurrentManagedThreadId);
        };

        var done = new TaskCompletionSource();
        vm.Licensing.ValidateCommand.Execute().Subscribe(
            _ => { }, _ => done.TrySetResult(), () => done.TrySetResult());
        await done.Task;

        // Cleared before the call and set to the success wording after it, so at
        // least one of the raises follows the await.
        Assert.NotEmpty(raiseThreads);
        Assert.All(raiseThreads, thread =>
            Assert.True(thread == uiThread,
                $"activating a licence updated the screen from thread {thread} instead of the UI thread {uiThread}"));
    }

    /// <summary>Adds the pool-thread hop a real HTTP answer has to the fake transport.</summary>
    private sealed class SlowHandler : DelegatingHandler
    {
        public SlowHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(50).ConfigureAwait(false);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_origOverride is null)
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", null);
        else
            Environment.SetEnvironmentVariable("AUTODYMO_SETTINGS_DIR", _origOverride);

        if (Directory.Exists(_settingsDir)) Directory.Delete(_settingsDir, true);
    }
}
