using System.Reactive.Linq;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// Drives a poll from a tick stream and keeps a single run in flight.
/// </summary>
public static class SerialPoll
{
    /// <summary>
    /// Runs <paramref name="refresh"/> once for every tick and drops the ticks that arrive
    /// while an earlier run is still going.
    ///
    /// The interval fires whatever the work is doing, and SelectMany would start one run per
    /// tick. On a device poll that means reads stacking up on a slow adb, each one reporting
    /// its own idea of what is connected, with the oldest result landing last. A dropped tick
    /// costs nothing: the next one is at most an interval away, and the run already going is
    /// the freshest request anyone asked for.
    /// </summary>
    public static IObservable<T> OneAtATime<T>(IObservable<long> ticks, Func<Task<T>> refresh)
    {
        int inFlight = 0;

        return ticks.SelectMany(_ =>
        {
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
            {
                return Observable.Empty<T>();
            }

            return Observable.FromAsync(async () =>
            {
                try
                {
                    return await refresh();
                }
                finally
                {
                    // The slot is given back before the result travels downstream, so a
                    // caller that sees a finished run knows the next tick will get through.
                    Interlocked.Exchange(ref inFlight, 0);
                }
            });
        });
    }
}
