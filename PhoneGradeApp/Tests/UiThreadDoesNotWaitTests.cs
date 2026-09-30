using System;
using System.IO;
using Xunit;

namespace PhoneGrade.Tests;

// ============ The view model must never wait for its own work ============
//
// This exists because of one line that took the whole application down.
//
// A browser only hands out camera, microphone, motion and orientation access on a
// secure origin, so on an Android device the desktop builds one with "adb reverse"
// before it prints the QR code. Building it costs two adb calls, each with their
// own timeout, so it is inherently asynchronous. It used to be resolved by waiting
// for it where the answer was needed:
//
//     bool opened = _adbTunnel.OpenAsync(sessionUdid, port).GetAwaiter().GetResult();
//
// That call sat inside the SelectedDevice setter, which runs on the UI thread. The
// awaited work has no ConfigureAwait(false) anywhere in this repository, so its
// continuation is posted back to the thread that is waiting for it. That thread can
// then never run it. The wait never ends and the window stops responding the moment
// an Android device is picked.
//
// Measured rather than reasoned about: a dump of the running application with a
// Pixel attached showed the main thread inside Task.SpinThenBlockingWait ->
// TaskAwaiter.GetResult -> ResolveWebRunnerHost -> UpdateWebRunnerSession ->
// set_SelectedDevice, reached from the Avalonia dispatcher loop. A second dump
// after the fix showed no PhoneGrade frame at all beyond Program.Main. No iPhone
// could reach it, because AdbReverseTunnel.SupportsReverse rules out an Apple UDID
// and "DEMO", which is what the app uses before a device has been chosen. That is
// why only connecting an Android device reproduced it.
//
// The tunnel now opens in the background and the QR code follows its answer, and
// these tests are here so that going back to waiting for it fails somewhere other
// than on the operator's bench.

public class UiThreadDoesNotWaitTests
{
    private static string ViewModel() => RepoPath.Read(
        "PhoneGradeApp", "PhoneGrade.UI", "ViewModels", "MainWindowViewModel.cs");

    private static string Resolver() => RepoPath.Read(
        "PhoneGradeApp", "PhoneGrade.Core", "WebRunnerOriginResolver.cs");

    /// <summary>Fails with the file name in the message, which the plain asserts do not carry.</summary>
    private static void Contains(string needle, string source, string where)
    {
        Assert.True(source.Contains(needle, StringComparison.Ordinal),
            $"{where} is expected to contain '{needle}' and does not.");
    }

    [Fact]
    public void TheViewModel_NeverWaitsForATask()
    {
        var source = ViewModel();

        // Every one of these blocks the calling thread until other work has
        // finished. On a UI thread the work that could finish it needs that very
        // thread, so the result is not a slow window but a dead one.
        Assert.DoesNotContain(".GetAwaiter().GetResult()", source);
        Assert.DoesNotContain(".Result", source);
        Assert.DoesNotContain(".Wait()", source);
        Assert.DoesNotContain(".WaitAsync(", source);
    }

    [Fact]
    public void TheTunnelIsAwaitedInstead()
    {
        var source = ViewModel();

        // The line that caused it. It lived in a helper that no longer exists, and
        // a helper coming back with the same body is the same bug.
        Assert.DoesNotContain("ResolveWebRunnerHost", source);

        // The replacement has to actually work the answer out in the background.
        // Firing it off and printing the code straight away would show a QR code
        // pointing at the plain LAN address while the tunnel is still being built,
        // and that address has no camera and no motion sensors on the phone, which
        // was the whole reason for building the tunnel.
        Contains("await _originResolver.ResolveAsync", source, "the view model");

        // And the answer has to reach the UI on the UI thread, because it touches
        // the properties the bindings are watching.
        Contains("Dispatcher.UIThread.InvokeAsync", source, "the view model");

        // The waiting itself moved into the resolver, where the two routes are
        // awaited one after the other instead of anyone blocking on them.
        var resolver = Resolver();
        Contains("await _usb(", resolver, "the origin resolver");
        Contains("await _internet(", resolver, "the origin resolver");
        Assert.DoesNotContain(".GetAwaiter().GetResult()", resolver);
        Assert.DoesNotContain(".Result", resolver);
        Assert.DoesNotContain(".Wait()", resolver);
    }

    [Fact]
    public void AStaleAnswer_CannotOverwriteANewerSession()
    {
        var source = ViewModel();

        // Two requests can now be in flight at once, which they could not before,
        // because before the first one blocked until it was done. A slow adb answer
        // for a device that has already been unplugged must be dropped rather than
        // applied to the device that replaced it.
        Contains("_webRunnerGeneration", source, "the view model");
        Contains("IsCurrentWebRunnerSession", source, "the view model");
    }

    [Fact]
    public void NoOtherViewModel_WaitsEither()
    {
        // The same mistake in a smaller view model would freeze the same way.
        var dir = RepoPath.Get("PhoneGradeApp", "PhoneGrade.UI", "ViewModels");
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            Assert.True(!source.Contains(".GetAwaiter().GetResult()", StringComparison.Ordinal),
                $"{name} blocks a thread on a task.");
            Assert.True(!source.Contains(".Result", StringComparison.Ordinal),
                $"{name} blocks a thread on a task.");
        }
    }
}
