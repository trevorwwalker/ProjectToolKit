// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Tests.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Planner;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Planner;

/// <summary>
/// 1064-B (state-cache.md MM-9, MM-14): the planner works a batch out on one snapshot of the repository's model of
/// main - the jobs tree, blob ids and text in process - and fetches through the model's gate.
/// </summary>
[ParallelMethods]
public class PlannerReadsModelTests
{
    private const string Jobs = PlannerHostFixture.Jobs;
    private const string Card = Jobs + "/queue/010-A-x.md";

    [Fact]
    public void MM_14_PlannerMain_FromOneSnapshot_NoGit()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Card] = PlannerHostFixture.SeedCard,
            [Jobs + "/done/009-A-w.md"] = "# 009-A: w\n\nStatus: done\n",
            [Jobs + "/README.md"] = "# Jobs\n",
            ["code/a.cs"] = "class A {}\n",
        };
        var main = new FakeMain(files, [Jobs]);
        var at = main.Snapshot!;
        var git = new GitRunner();
        var planner = new AnswersPlannerMain(main, at, git, "C:/no/such/repo", Jobs);

        Assert.Equal(at.Commit, planner.Commit);
        Assert.Equal([Jobs + "/README.md", Jobs + "/done/009-A-w.md", Card], planner.Paths());
        Assert.Equal(FakeObjects.BlobId(PlannerHostFixture.SeedCard), planner.BlobId(Card));
        Assert.Null(planner.BlobId(Jobs + "/queue/011-A-none.md"));
        Assert.Equal(PlannerHostFixture.SeedCard, planner.ReadFile(Card));
        Assert.Equal(PlannerHostFixture.SeedCard, planner.ReadBlob(FakeObjects.BlobId(PlannerHostFixture.SeedCard)));
        Assert.Equal(0, git.Started);
    }

    [Fact]
    public void MM_14_PlannerStep_BatchFromTheModel_CommitsAsBefore()
    {
        using var f = new PlannerHostFixture();
        var models = new MainModels(1);
        var repo = models.Join(new MainModelsJoin(f.Fx.Repo, "origin", "main", Jobs, TimeSpan.FromSeconds(15), false,
            new GitRunner(), new ListLog(), new FakeClock(DateTimeOffset.Now)));
        try
        {
            Assert.True(Ended(repo.Loading), "load did not end");
            Assert.NotNull(repo.Model);
            f.Step.Feed = repo;
            var blob = repo.Model!.Entries(Jobs).Single(e => e.Path == Card).BlobId;
            const string Updated = "# 010-A: x\n\nLane: A\nStatus: ready\nPriority: high\n";
            const string NewCard = "# 020-B: y\n\nLane: B\nStatus: ready\n";
            f.Batch("001-a", "Plan x",
            [
                new { op = "write", path = Card, @base = blob, file = "010-A-x.md" },
                new { op = "write", path = $"{Jobs}/queue/020-B-y.md", @base = "new", file = "020-B-y.md" },
            ], new Dictionary<string, string> { ["010-A-x.md"] = Updated, ["020-B-y.md"] = NewCard });

            f.Step.Run();

            Assert.True(Directory.Exists(Path.Combine(f.Outbox, "sent", "001-a")), string.Join(" | ", f.Log.Lines));
            Assert.Equal(Updated, GitFixture.Git(f.Fx.Bare, "show", "main:" + Card).Replace("\r\n", "\n") + "\n");
            Assert.Equal(NewCard, GitFixture.Git(f.Fx.Bare, "show", $"main:{Jobs}/queue/020-B-y.md").Replace("\r\n", "\n") + "\n");
            Assert.Equal(f.RemoteSha(), GitFixture.Git(f.Fx.Repo, "rev-parse", "origin/main"));   // the gate's fetch ran in the repository
        }
        finally
        {
            models.Leave(repo, Jobs);
        }
    }

    private static bool Ended(Task load) => load.Wait(TimeSpan.FromSeconds(60));
}
