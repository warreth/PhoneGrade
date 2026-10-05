using Avalonia.Headless;
using Avalonia.Threading;

namespace Tests;

/// <summary>
/// Runs the render timer dry before a test lets go of its window.
/// <para>
/// A tick schedules a render pass on the dispatcher, and a pass that is still
/// queued when the test ends is carried over to whatever runs next. It then runs
/// during the next test's session setup, where the locator has not registered the
/// font manager yet, so that test fails for this test's pending work. The queue is
/// drained here instead, while this test's own application is the one on duty and
/// every service it needs is registered.
/// </para>
/// </summary>
public static class HeadlessRender
{
    /// <summary>
    /// Runs the render timer dry. The drain also runs after every test through
    /// <see cref="DrainTheRenderQueueAfterEveryTestAttribute"/>, so a test calls
    /// this only when it needs the queue settled mid-test, such as before taking
    /// the measurement it is about to assert on.
    /// </summary>
    public static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}