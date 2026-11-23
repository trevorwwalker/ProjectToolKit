using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Tests.MainModel;
using Scheduler.Core.Tick;
using Scheduler.Host.Git;

namespace Scheduler.Host.Tests.Git;

/// <summary>
/// 1063-B (state-cache.md MM-8, MM-9, MM-14, MM-15, MM-18): the card and request readers and the fetcher read the
/// repository's model of main once it has loaded: the same lists as the git CLI read, and no git process.
/// </summary>
public class MainModelReadTests
{
    private static readonly Dictionary<string, string> Seed = new(StringComparer.Ordinal)
    {
        ["jobs/README.md"] = "# Jobs\n",
        ["jobs/queue/1-B-a.md"] = "# 1-B: a\n\nCategory: B\nStatus: ready\nDepends on: 3-B\n",
        ["jobs/queue/5-C-e.md"] = "# 5-C: e\n\nCategory: C\nStatus: ready\nPriority: high\n",
        ["jobs/active/2-B-b.md"] = "# 2-B: b\n\nCategory: B\nStatus: ready\nClaimed: 2026-11-22, lane B\n",
        ["jobs/review/4-B-d.md"] = "# 4-B: d\n\nCategory: B\nStatus: review: look\n\n## Questions\n\n- Q1: ok? [yes -> done | no]\n",
        ["jobs/done/3-B-c.md"] = "# 3-B: c\n\nCategory: B\nStatus: done\n",
        ["jobs/requests/001-a.md"] = "# A\n\nStatus: open\nFor: 1-B-a\n",
        ["jobs/requests/002-b.md"] = "# B\n\nStatus: answered\n",
        ["jobs/requests/answered/900-old.md"] = "# Old\n",
        ["docs/x.md"] = "outside the jobs folder\n",
    };

    [Fact]
    public void MM_14_CardReader_FromModel_SameCardsAsTheCliRead_NoGit()
    {
        var feed = FakeFeed.Over(Seed);
        var git = new CountingMain();
        var fromModel = new CardReader(git, "jobs", new ListLog()) { ByCommit = true, Feed = feed }.Read();
        var fromCli = new CardReader(new DictMain(Seed), "jobs", new ListLog()) { ByCommit = true }.Read();

        Assert.Empty(git.Calls);
        Assert.Equal(fromCli.Select(Shape), fromModel.Select(Shape));
        Assert.Equal(["2-B", "3-B", "1-B", "5-C", "4-B"], fromModel.Select(c => c.Id));
    }

    [Fact]
    public void MM_10_CardReader_MainNotMoved_SameListNothingRead_MovedSeesTheMove()
    {
        var feed = FakeFeed.Over(Seed);
        var seen = new List<string>();
        var reader = new CardReader(new CountingMain(), "jobs", new ListLog()) { ByCommit = true, Feed = feed, MainSeen = seen.Add };
        var first = reader.Read();
        feed.Main.Objects.ResetCounts();

        Assert.Same(first, reader.Read());
        Assert.Equal(0, feed.Main.Objects.Reads);

        feed.Main.Move("jobs/queue/1-B-a.md", "jobs/active/1-B-a.md");
        var moved = reader.Read();

        Assert.Equal(CardFolder.Active, moved.Single(c => c.Id == "1-B").Folder);
        Assert.Empty(feed.Main.Objects.BlobReads);   // a moved card keeps its blob: not parsed again (MM-7)
        Assert.Equal(3, seen.Count);   // MainSeen (the locks audit, DL-19) hears every read's commit
        Assert.Equal(seen[0], seen[1]);
        Assert.Equal(feed.Main.Model.Commit, seen[2]);
    }

    [Fact]
    public void CardReader_ModelNotLoaded_ReadsThroughGitAsBefore()
    {
        var git = new CountingMain();
        var reader = new CardReader(git, "jobs", new ListLog()) { ByCommit = true, Feed = new NoModel() };

        Assert.Empty(reader.Read());
        Assert.Contains("ls-tree jobs", git.Calls);
    }

    [Fact]
    public void MM_14_RequestReader_FromModel_SameRequestsAsTheCliRead_NoGit()
    {
        var git = new CountingMain();
        var fromModel = new RequestReader(git, "jobs") { Feed = FakeFeed.Over(Seed) }.Read();
        var fromCli = new RequestReader(new DictMain(Seed), "jobs").Read();

        Assert.Empty(git.Calls);
        Assert.Equal(fromCli, fromModel);
        Assert.Equal(["001-a", "002-b"], fromModel.Select(r => r.Name));
    }

    [Theory]
    [InlineData(TickWake.Click)]
    [InlineData(TickWake.MoveRequest)]
    public void MM_9_Fetcher_ClickOrMoveRequest_NeverFetches_ReadsTheRef(TickWake wake)
    {
        var feed = FakeFeed.Over(Seed);
        var git = new CountingMain();
        var fetcher = new MainFetcher(git, feed.Main.Clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        Assert.True(fetcher.Fetch());   // the first read fetches (1124-B: in the background)
        Settled(feed);
        var reads = feed.RefReads;

        fetcher.Woke(wake);
        Assert.True(fetcher.Fetch());
        Settled(feed);

        Assert.Equal(1, feed.Fetches);
        Assert.Equal(reads + 1, feed.RefReads);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public void MM_9_Fetcher_PollSharesAFreshFetch_FetchesOncePollPassed_WorkerEndFetches()
    {
        var feed = FakeFeed.Over(Seed);
        var clock = (FakeMain.FixedClock)feed.Main.Clock;
        var fetcher = new MainFetcher(new CountingMain(), clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        fetcher.Woke(TickWake.Poll);
        fetcher.Fetch();
        Settled(feed);

        // another consumer (the planner, the audit) asks within the poll: the same fetch answers
        Assert.Equal(FetchAnswer.Shared, feed.Fetch(FetchAsk.Planner));
        clock.Now += TimeSpan.FromSeconds(59);
        fetcher.Woke(TickWake.Poll);
        fetcher.Fetch();
        Settled(feed);
        Assert.Equal(2, feed.Fetches);

        fetcher.Ask();   // a worker ended
        fetcher.Woke(TickWake.Click);
        fetcher.Fetch();
        Settled(feed);
        Assert.Equal(2, feed.Fetches);   // a click still never fetches
        fetcher.Fetch();
        Settled(feed);
        Assert.Equal(3, feed.Fetches);
    }

    [Fact]
    public void MM_9_Fetcher_FailedFetch_KeepsTheCards_NextReadFetchesAgain()
    {
        var feed = FakeFeed.Over(Seed);
        var fetcher = new MainFetcher(new CountingMain(), feed.Main.Clock, TimeSpan.FromSeconds(58)) { Feed = feed };
        feed.FetchWorks = false;

        Assert.True(fetcher.Fetch());   // 1124-B: the fetch runs in the background; its failure shows on the next read
        Settled(feed);
        feed.FetchWorks = true;
        Assert.False(fetcher.Fetch());   // CARD-3: the cards are kept; this read asks again
        Settled(feed);
        Assert.True(fetcher.Fetch());
        Assert.Equal(2, feed.Fetches);
    }

    private static void Settled(IMainFeed feed) => Assert.True(BackgroundFetch.For(feed).Running.Wait(TimeSpan.FromSeconds(10)));

    [Fact]
    public void MainFetcher_ModelNotLoaded_StartsTheLoad_FetchesThroughGit()
    {
        var git = new CountingMain();
        var feed = new NoModel();
        var fetcher = new MainFetcher(git, new FakeMain.FixedClock(DateTimeOffset.UnixEpoch), TimeSpan.FromSeconds(58)) { Feed = feed };

        Assert.True(fetcher.Fetch());
        Assert.Equal(["fetch"], git.Calls);
        Assert.Equal(1, feed.Ticks);
    }

    private static string Shape(Card c) =>
        string.Join("|", c.Folder, c.Path, c.Id, c.Num, c.Category, c.Title, c.Status, c.Prio, c.BlobId,
            string.Join(",", c.Depends), c.Questions.Count, c.HeaderCategory, c.LatestJournal);

    /// <summary>A model that has not loaded yet.</summary>
    private sealed class NoModel : IMainFeed
    {
        public int Ticks { get; private set; }

        public IMainModel? Model => null;

        public FetchGate Gate { get; } = new(new FakeMain.FixedClock(DateTimeOffset.UnixEpoch), TimeSpan.FromSeconds(58), () => true);

        public bool Tick()
        {
            Ticks++;
            return false;
        }

        public FetchAnswer Fetch(FetchAsk why) => Gate.Ask(why);
    }

    /// <summary>The git CLI reader over a dictionary, with the same blob ids as <see cref="FakeObjects"/>.</summary>
    private sealed class DictMain(IReadOnlyDictionary<string, string> files) : IMainRepository
    {
        public bool Fetch() => true;

        public IReadOnlyList<TreeEntry>? ListTree(string folder) =>
        [
            .. files.Keys.Where(p => p.StartsWith(folder + "/", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new TreeEntry { Path = p, BlobId = FakeObjects.BlobId(files[p]) }),
        ];

        public string? ReadBlob(string blobId) =>
            files.Values.FirstOrDefault(t => FakeObjects.BlobId(t) == blobId);

        public string? ReadFile(string path) => files.GetValueOrDefault(path);

        public string? HeadSha() => "c1";
    }
}
