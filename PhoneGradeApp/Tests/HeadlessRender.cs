using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
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

    /// <summary>
    /// The frame a window is showing, waiting for the pass that produces it.
    /// <para>
    /// CaptureRenderedFrame answers null until a pass has run, and a pass is
    /// dispatcher work: a tick only queues it. Capturing once right after a show
    /// is therefore a race, and the loser reads a null frame. Ticking, draining
    /// and looking again until there is one turns that race into a bounded wait.
    /// </para>
    /// </summary>
    public static WriteableBitmap Capture(Window window, int attempts = 50)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Drain();

            WriteableBitmap? frame = HeadlessWindowExtensions.CaptureRenderedFrame(window);
            if (frame is not null) return frame;

            // The pass may be waiting on the composition to be scheduled by a
            // task rather than on the queue, so give it a real beat between tries.
            Thread.Sleep(10);
        }

        throw new InvalidOperationException(
            "the window never produced a rendered frame; the headless renderer did not present one");
    }
}
