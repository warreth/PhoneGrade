using System.Reflection;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;
using Xunit.Sdk;

// Test classes are run in parallel by default, and this suite cannot tolerate
// that. Several classes mutate process-wide state that every other class can
// see: AUTODYMO_SETTINGS_DIR and AUTODYMO_LOG_DIR are read at call time rather
// than cached, UsbEventWatcher keeps its watchers in static fields, and each
// MainWindowViewModel binds port 5055. One class's constructor pointing the
// settings directory at its own temporary folder made a concurrently running
// class load "System" instead of "Dark", and a concurrently appending writer
// made the log assertions race. Running the classes one at a time costs a few
// seconds and makes the suite deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: Tests.DrainTheRenderQueueAfterEveryTest]

namespace Tests;

/// <summary>
/// Runs the render timer dry after every test, so no test can fail because of
/// what a different one left queued.
///
/// A tick schedules a render pass on the dispatcher, and a pass still queued when
/// a test ends is carried over to whatever runs next. It then runs during that
/// next test's session setup, where the locator has not registered the font
/// manager yet, and the failure it produces names the wrong test: a luminance
/// assertion, or a capture of an empty frame, in a class that never touched any
/// of it. Which class it lands on depends on how the run was ordered, which is
/// why this showed up as a different failure on each run.
///
/// Individual tests used to drain by hand at the end of the happy path, which
/// covers exactly the case where every assertion passed and none of the others.
/// An assertion that throws skips the drain, and so does any early return. The
/// drain is therefore attached to the end of the run rather than written into
/// the bodies, which makes it unconditional by construction.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DrainTheRenderQueueAfterEveryTestAttribute : BeforeAfterTestAttribute
{
    /// <summary>Counts the hooks that ran, so the wiring can be checked.</summary>
    public static int Drained { get; private set; }

    /// <summary>Counts the before hook separately, to tell the two apart.</summary>
    public static int Started { get; private set; }

    public override void Before(MethodInfo methodUnderTest) => Started++;

    public override void After(MethodInfo methodUnderTest)
    {
        Drained++;
        Drain();
    }

    /// <summary>Runs the render timer dry, so no queued pass outlives the test.</summary>
    private static void Drain()
    {
        try
        {
            // A single pass leaves behind whatever a task posted while it was
            // running; that pass then executes during the next test's session
            // setup, where the font manager is not registered yet. Give those
            // continuations a beat to land and drain each round too.
            for (int pass = 0; pass < 3; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                if (pass < 2) Thread.Sleep(5);
            }
        }
        catch (Exception)
        {
            // A test that never touched Avalonia has no session to pump, which is
            // the expected case rather than a problem to report. Anything thrown
            // here lands after the test has already been given its result and would
            // replace it with a failure of its own, in a test with no connection to
            // rendering. The next test that measures drains again before it does.
        }
    }
}