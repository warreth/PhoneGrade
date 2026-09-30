using System.Collections.Concurrent;
using System.Reactive.Subjects;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace Tests;

public class SerialPollTests
{
    /// <summary>
    /// The interval keeps firing whatever the refresh is doing. Every tick that arrives
    /// while one is running has to be dropped, because a second one on top of it reads the
    /// same devices again and hands back its own, older answer.
    /// </summary>
    [Fact]
    public async Task TickDuringARun_IsDroppedInsteadOfStackingUp()
    {
        using var ticks = new Subject<long>();
        var results = new ConcurrentQueue<int>();
        var firstRun = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        object sync = new();
        int started = 0;
        int live = 0;
        int peak = 0;

        async Task<int> Refresh()
        {
            int run;
            lock (sync)
            {
                run = ++started;
                live++;
                peak = Math.Max(peak, live);
            }

            try
            {
                // The first run only ends when the test lets it.
                return run == 1 ? await firstRun.Task : run;
            }
            finally
            {
                lock (sync) { live--; }
            }
        }

        using var subscription = SerialPoll.OneAtATime(ticks, Refresh).Subscribe(results.Enqueue);

        ticks.OnNext(1);
        Assert.True(await Eventually(() => Volatile.Read(ref started) == 1),
            "the first tick never started a refresh");

        // Two more ticks arrive while that refresh is still going.
        ticks.OnNext(2);
        ticks.OnNext(3);
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref started));
        Assert.Empty(results);

        firstRun.SetResult(10);
        Assert.True(await Eventually(() => results.Count == 1),
            "the refresh that finished never reported back");
        Assert.Equal(10, results.First());

        // A tick that lands after the run is over has to get through: the gate is not a
        // latch. Ticked repeatedly so the test says so only once the slot really is free.
        Assert.True(await Eventually(() =>
        {
            ticks.OnNext(4);
            return Volatile.Read(ref started) >= 2;
        }), "a tick after the run finished never started a new refresh");

        Assert.True(await Eventually(() => results.Count >= 2),
            "the refresh after the finished one never reported back");
        Assert.Contains(2, results);

        // Nothing ever ran at the same time as something else.
        Assert.Equal(1, Volatile.Read(ref peak));
    }

    private static async Task<bool> Eventually(Func<bool> condition, int timeoutMs = 5000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            if (condition()) return true;
            await Task.Delay(15);
        } while (DateTime.UtcNow < deadline);

        return condition();
    }
}
