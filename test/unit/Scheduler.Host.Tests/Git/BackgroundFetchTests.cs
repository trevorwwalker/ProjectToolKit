using Scheduler.Core.MainModel;
using Scheduler.Core.Tests.MainModel;
using Scheduler.Core.Tick;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1124-B (state-cache.md MM-9): the fetch runs off the tick thread, one background fetcher per repository. The tick
/// reads the model as it stands; a completed fetch that moves main raises <see cref="BackgroundFetch.Moved"/>; no two
/// fetches of one repository run at once; the writer keeps its own fetch before it writes.
/// </summary>
public class BackgroundFetchTests
{
    private static readonly Dictionary<string, string> Seed = new(StringComparer.Ordinal)
    {
        ["jobs/README.md"] = "# Jobs\n",
        ["jobs/queue/1-B-a.md"] = "# 1-B: a\n\nCategory: B\nStatus: ready\n",
    };

    private static void Settled(IMainFeed feed) => Assert.True(Done(BackgroundFetch.For(feed).Running));

    private static bool Done(Task task) => task.Wait(TimeSpan.FromSeconds(10));

    [Fact]
    public void MM_9_SlowFetch_TheReadReturnsAtOnce_TheModelAsItStands()
    {
        var feed = FakeFeed.Over(Seed);
        feed.RefFollowsFetch = true;
        using var release = new ManualResetEventSlim();
        feed.FetchStarted = () => release.Wait(TimeSpan.FromSeconds(30));
        var fetcher = new MainFetcher(new CountingMain(), feed.Main.Clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        var at = feed.Main.Commit;
        _ = feed.Main.Stage(new Dictionary<string, string?> { ["jobs/queue/2-B-b.md"] = "# 2-B: b\n\nCategory: B\nStatus: ready\n" });

        var took = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(fetcher.Fetch());
        took.Stop();

        Assert.True(took.Elapsed < TimeSpan.FromSeconds(5), $"the read took {took.Elapsed}");
        Assert.False(BackgroundFetch.For(feed).Running.IsCompleted);
        Assert.Equal(at, feed.Main.Commit);   // not moved until the fetch completes
        release.Set();
        Settled(feed);
        Assert.NotEqual(at, feed.Main.Commit);
    }

    [Fact]
    public void MM_9_AFetchThatMovesMain_RaisesMoved_OneThatDoesNot_Stays()
    {
        var feed = FakeFeed.Over(Seed);
        feed.RefFollowsFetch = true;
        var clock = (FakeMain.FixedClock)feed.Main.Clock;
        var fetcher = new MainFetcher(new CountingMain(), clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        var moved = 0;
        BackgroundFetch.For(feed).Moved += () => Interlocked.Increment(ref moved);
        fetcher.Fetch();
        Settled(feed);
        Assert.Equal(0, moved);   // nothing new on the remote

        var pushed = feed.Main.Stage(new Dictionary<string, string?> { ["jobs/queue/2-B-b.md"] = "# 2-B: b\n\nCategory: B\nStatus: ready\n" });
        clock.Now += TimeSpan.FromSeconds(59);
        fetcher.Woke(TickWake.Poll);
        fetcher.Fetch();
        Settled(feed);

        Assert.Equal(1, moved);
        Assert.Equal(pushed, feed.Main.Commit);
        Assert.Contains(feed.Main.Cards("jobs"), c => c.Id == "2-B");
    }

    [Fact]
    public void MM_9_NoTwoFetchesOfOneRepositoryAtOnce()
    {
        var feed = FakeFeed.Over(Seed);
        using var release = new ManualResetEventSlim();
        feed.FetchStarted = () => release.Wait(TimeSpan.FromSeconds(30));
        // two projects in one repository: two fetchers over the one feed, so one background fetcher
        var first = new MainFetcher(new CountingMain(), feed.Main.Clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        var second = new MainFetcher(new CountingMain(), feed.Main.Clock, TimeSpan.FromSeconds(58)) { Feed = feed };

        first.Fetch();
        for (var i = 0; i < 5; i++)
        {
            first.Ask();   // a worker ended: the gate would fetch again
            first.Woke(TickWake.WorkerExit);
            _ = first.Fetch();
            second.Woke(TickWake.Poll);
            _ = second.Fetch();
        }

        var running = BackgroundFetch.For(feed).Running;
        var planner = Task.Run(() => feed.Fetch(FetchAsk.Planner));   // another consumer asks the gate itself
        Assert.False(running.IsCompleted);
        release.Set();
        Settled(feed);
        Assert.True(Done(planner));

        Assert.Equal(1, feed.MaxInFlight);
        Assert.Same(running, BackgroundFetch.For(feed).Running);   // every ask in flight was answered by the one fetch
    }

    [Fact]
    public void MM_9_FailedBackgroundFetch_ReportedOnce_TheNextAskFetchesAgain()
    {
        var feed = FakeFeed.Over(Seed);
        var fetcher = new MainFetcher(new CountingMain(), feed.Main.Clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        feed.FetchWorks = false;
        Assert.True(fetcher.Fetch());
        Settled(feed);

        feed.FetchWorks = true;
        Assert.False(fetcher.Fetch());
        Settled(feed);
        Assert.True(fetcher.Fetch());
        Settled(feed);
        Assert.Equal(2, feed.Fetches);
    }

    // The writer's write chain keeps its fetch before it writes: with no tick fetch at all, the answers worktree a
    // write starts from is main as the remote has it.
    [Fact]
    public void MM_9_TheWriterFetchesBeforeItWrites()
    {
        using var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "# Jobs\n" });
        var answers = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main");
        _ = committer.AnswersTree();
        fx.Push("another clone", new Dictionary<string, string?> { ["jobs/queue/2-B-b.md"] = "# 2-B: b\n" });

        var tree = committer.AnswersTree();

        Assert.Equal(GitFixture.Git(fx.Bare, "rev-parse", "main"), GitFixture.Git(tree, "rev-parse", "HEAD"));
    }
}
