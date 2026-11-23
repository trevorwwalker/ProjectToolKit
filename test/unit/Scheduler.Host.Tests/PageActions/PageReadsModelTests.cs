using Scheduler.Core.Categories;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Tests.MainModel;
using Scheduler.Core.WebContract;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 1064-B (state-cache.md MM-14, MM-15): the page and its actions read main from the repository's model of main.
/// A click that only reads starts no git process and never fetches; one that writes reads main only from the model,
/// so it costs nothing but its write chain. The git CLI reader is <see cref="CountingMain"/>: any call to it would
/// have started a git process.
/// </summary>
public sealed class PageReadsModelTests
{
    private const string Jobs = "docs/jobs";
    private const string WorkersRel = Jobs + "/workers.json";
    private const string CategoriesRel = Jobs + "/categories.json";
    private const string Queued = Jobs + "/queue/951-A-old-name.md";
    private const string Reviewed = Jobs + "/review/953-B-look.md";
    private const string RequestPath = Jobs + "/requests/001-a.md";

    private const string Categories = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    private static readonly string Pool = WorkerTypesJson.BuildPool(new WorkerPool
    {
        MaxWorkers = 3,
        Types = [new WorkerType { Name = "worker", Max = 2 }, new WorkerType { Name = "Sonnet", Max = 1 }],
    });

    private static string CardOf(string title, string category, string status, string workerLine = "") =>
        $"# {title}\n\nCategory: {category}\nStatus: {status}\n{workerLine}Priority: normal\n\n## Task\n\nDo it.\n\n## Result\n\n- started\n";

    private static Dictionary<string, string> Seed() => new(StringComparer.Ordinal)
    {
        [WorkersRel] = Pool,
        [CategoriesRel] = Categories,
        [Queued] = CardOf("951-A: Old name", "A", "ready", "Worker: worker\n"),
        [Jobs + "/active/952-B-other.md"] = CardOf("952-B: Other", "B", "ready", "Worker: Sonnet\n"),
        [Reviewed] = CardOf("953-B: Look", "B", "review: look at it"),
        [Jobs + "/done/950-A-old.md"] = CardOf("950-A: Old", "A", "done", "Worker: worker\n"),
        [RequestPath] = "# A request\n\nStatus: open\n",
    };

    private static (PageActionHarness Harness, FakeFeed Feed, CountingMain Git, RecordingCommitter Committer) Make(string project)
    {
        var feed = new FakeFeed(new FakeMain(Seed(), [Jobs], new FakeMain.FixedClock(new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero))));
        var git = new CountingMain();
        var committer = new RecordingCommitter();
        var harness = new PageActionHarness(categoriesJson: Categories, workersJson: Pool, main: git, committer: committer, project: project);
        harness.Actions.Feed = feed;
        harness.Host.ReadCardsFrom = () => feed.Main.Cards(Jobs);
        harness.Host.Requests.AddRange(feed.Main.Requests(Jobs));
        harness.Tick_Once();
        return (harness, feed, git, committer);
    }

    [Fact]
    public void MM_15_PageClick_StartsNoGit()
    {
        var (harness, feed, git, _) = Make("ptk-1064-read");
        using var _h = harness;
        var data = new HostPageData(harness.Config, harness.Clock, harness.Tick, git, harness.Files, () => null) { Feed = feed };
        var request = Assert.Single(harness.Tick.Requests);

        Assert.Equal(Seed()[Queued], data.Card("951-A-old-name"));
        Assert.Equal(Seed()[Reviewed], data.Card("953-B-look"));
        Assert.Equal(Seed()[RequestPath], data.Request(request.Name));
        Assert.Null(data.Card("999-A-none"));

        // A refused click that names the categories file finds it on main through the model too.
        var refused = harness.Actions.AcceptCategory("Z");
        Assert.False(refused.Ok);
        Assert.Contains($"no category Z in {CategoriesRel}", refused.Error, StringComparison.Ordinal);

        Assert.Empty(git.Calls);       // no show, ls-tree, cat-file or rev-parse
        Assert.Equal(0, feed.Fetches);  // and a click never fetches (MM-9)
    }

    [Fact]
    public void MM_15_PageClick_ModelNotLoaded_ReadsThroughTheCli()
    {
        var (harness, _, git, _) = Make("ptk-1064-cli");
        using var _h = harness;
        var data = new HostPageData(harness.Config, harness.Clock, harness.Tick, git, harness.Files, () => null) { Feed = null };

        Assert.Null(data.Card("951-A-old-name"));   // CountingMain has no files
        Assert.Equal([$"show {Queued}"], git.Calls);
    }

    [Fact]
    public void MM_14_WorkerTypes_Rename_OpenCardsAndFileFromTheModel()
    {
        var (harness, feed, git, committer) = Make("ptk-1064-rename");
        using var _h = harness;

        var saved = harness.Actions.SaveWorkerTypes(3,
        [
            new PageWorkerTypeView { Name = "default", OriginalName = "worker", Max = 2 },
            new PageWorkerTypeView { Name = "Sonnet", OriginalName = "Sonnet", Max = 1 },
        ]);

        Assert.True(saved.Ok, saved.Error);
        var change = Assert.Single(committer.Changes);
        Assert.Equal(2, change.Writes.Count);   // workers.json and the one open card naming the type
        Assert.Contains("Worker: default\n", change.Writes[Queued], StringComparison.Ordinal);
        Assert.False(change.Writes.ContainsKey(Jobs + "/done/950-A-old.md"));
        Assert.Empty(git.Calls);
        Assert.True(feed.RefReads >= 1);   // main as the commit sees it: the ref was read first (MM-8)
    }

    [Fact]
    public void MM_14_Categories_RemoveUsed_RefusedWithTheCardsFromTheModel()
    {
        var (harness, _, git, committer) = Make("ptk-1064-categories");
        using var _h = harness;

        var saved = harness.Actions.SaveCategories(
            [new PageCategory { Letter = "A", OriginalLetter = "A", Name = "core", Purpose = "p", Owns = ["src/a/"] }], []);

        Assert.False(saved.Ok);
        Assert.Contains("category B is used by 952-B, 953-B", saved.Error, StringComparison.Ordinal);
        Assert.Empty(committer.Changes);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public void MM_15_ClickThatWrites_ReadsMainOnlyFromTheModel()
    {
        var (harness, feed, git, committer) = Make("ptk-1064-done");
        using var _h = harness;

        var done = harness.Actions.Done("953-B-look");

        Assert.True(done.Ok, done.Error);
        var change = Assert.Single(committer.Changes);
        Assert.Contains(change.Moves, m => m.From == Reviewed);
        Assert.Empty(git.Calls);   // the write chain is the committer's; main was read from the model
        Assert.Equal(0, feed.Fetches);
    }

    [Fact]
    public void MM_14_CardWorkerLine_FromTheModel_SameAsTheCli()
    {
        var main = new PageReviewRefreshTests.InMemoryMain();
        var seed = new FakeMain(Seed(), [Jobs]);
        foreach (var (path, text) in Seed())
        {
            main.Files[path] = text;
        }

        var cli = Scheduler.Host.PageActions.CardWorkerLine.Open(main, Jobs);
        var model = Scheduler.Host.PageActions.CardWorkerLine.Open(seed, Jobs);

        Assert.Equal(cli.Select(c => (c.Card.Path, c.Card.WorkerReq, c.Text)).Order(), model.Select(c => (c.Card.Path, c.Card.WorkerReq, c.Text)).Order());
        Assert.Equal(3, model.Count);
    }
}
