// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.MainModel;

namespace Scheduler.Core.Tests.MainModel;

public class FetchGateTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(10);

    // MM-9: one fetch per poll per repository, shared by every consumer; clicks and move requests
    // never fetch; a worker's end asks for one more; a failed fetch makes the next ask fetch; a
    // the fetch count is reported.
    [Fact]
    public void MM_9_FetchGate_OncePerPollPerRepository_SharedByConsumers_ClickAndMoveNeverFetch_WorkerEndAsks_FailedFetchAsksAgain()
    {
        var clock = new FakeMain.FixedClock(new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero));
        var works = true;
        var fetches = 0;
        var gate = new FetchGate(clock, Poll, () =>
        {
            fetches++;
            return works;
        });

        // A click before anything else still does not fetch (1002-B).
        Assert.Equal(FetchAnswer.Never, gate.Ask(FetchAsk.Click));
        Assert.Equal(FetchAnswer.Never, gate.Ask(FetchAsk.MoveRequest));
        Assert.Equal(0, fetches);

        // The first ask fetches; every consumer within the poll shares it.
        Assert.Equal(FetchAnswer.Fetched, gate.Ask(FetchAsk.Poll));
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Poll));
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Planner));
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Health));
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Audit));
        Assert.Equal(1, fetches);

        // Just under a poll: still shared; a full poll: due again (the boundary counts, as MainFetcher's >=).
        clock.Now += Poll - TimeSpan.FromMilliseconds(1);
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Poll));
        clock.Now += TimeSpan.FromMilliseconds(1);
        Assert.Equal(FetchAnswer.Fetched, gate.Ask(FetchAsk.Planner));
        Assert.Equal(2, fetches);

        // A worker ended: one more fetch, shared by the consumers after it.
        gate.WorkerEnded();
        Assert.Equal(FetchAnswer.Never, gate.Ask(FetchAsk.Click));
        Assert.Equal(FetchAnswer.Fetched, gate.Ask(FetchAsk.Poll));
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Poll));
        Assert.Equal(3, fetches);

        // A failed fetch: the next ask fetches, even within the poll.
        works = false;
        clock.Now += Poll;
        Assert.Equal(FetchAnswer.Failed, gate.Ask(FetchAsk.Poll));
        works = true;
        Assert.Equal(FetchAnswer.Fetched, gate.Ask(FetchAsk.Poll));
        Assert.Equal(FetchAnswer.Shared, gate.Ask(FetchAsk.Planner));
        Assert.Equal(5, fetches);
        Assert.Equal(5, gate.Fetches);
    }

    // MM-9: consumers that ask while a fetch is in flight are answered by it, not by a second fetch.
    [Fact]
    public async Task MM_9_FetchInFlight_AnswersTheAsksWaitingOnIt()
    {
        var clock = new FakeMain.FixedClock(new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero));
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var fetches = 0;
        var gate = new FetchGate(clock, Poll, () =>
        {
            Interlocked.Increment(ref fetches);
            started.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return true;
        });

        var first = Task.Run(() => gate.Ask(FetchAsk.Poll));
        Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        var others = Enumerable.Range(0, 4).Select(_ => Task.Run(() => gate.Ask(FetchAsk.Planner))).ToArray();
        release.Set();

        Assert.Equal(FetchAnswer.Fetched, await first);
        Assert.All(await Task.WhenAll(others), a => Assert.Equal(FetchAnswer.Shared, a));
        Assert.Equal(1, fetches);
    }
}
