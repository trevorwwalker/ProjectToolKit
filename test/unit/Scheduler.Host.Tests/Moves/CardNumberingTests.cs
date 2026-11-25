// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 1111-B (planner-outbox.md "Card numbers"): the pool's one counter, and the step that numbers the
/// <c>new-{L}-{slug}.md</c> cards a commit put on main.
/// </summary>
[ParallelMethods]
public class CardNumberingTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    [Fact]
    public void Counter_StartsAboveTheHighest_NeverHandsOutANumberTwice()
    {
        long? highest = 40;
        var counter = new CardNumbering(() => highest);
        Assert.True(counter.Ready);
        Assert.Equal(41, counter.Next());
        Assert.Equal(42, counter.Next());   // main has not moved: still above the last handed out
        highest = 100;
        Assert.Equal(101, counter.Next());
        highest = 10;
        Assert.Equal(102, counter.Next());   // never lower than the last handed out
        Assert.Equal(102, counter.Last);
    }

    [Fact]
    public void Counter_NotReady_WhileAModelIsMissing()
    {
        var counter = new CardNumbering(() => null);
        Assert.False(counter.Ready);
    }

    [Fact]
    public void Counter_ManyThreads_DistinctNumbers()
    {
        var counter = new CardNumbering(() => 7);
        var numbers = new System.Collections.Concurrent.ConcurrentBag<long>();
        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 4 }, _ => numbers.Add(counter.Next()));
        Assert.Equal(200, numbers.Distinct().Count());
        Assert.Equal(8, numbers.Min());
    }

    // Two projects in the pool, each with a new card on main: distinct numbers above the highest in either.
    [Fact]
    public void NewCardsOnMain_TwoProjects_DistinctNumbersAboveEveryProject()
    {
        using var a = Project("jobs", new Dictionary<string, string?>
        {
            ["jobs/queue/100-A-x.md"] = "# 100-A: x\n\nStatus: ready\n",
            ["jobs/archive/120-A-old.md"] = "# 120-A: old\n",
            ["jobs/queue/new-A-first.md"] = "# new-A: first\n\nStatus: ready\n",
        });
        using var b = Project("work", new Dictionary<string, string?>
        {
            ["work/done/300-B-y.md"] = "# 300-B: y\n",
            ["work/queue/new-B-second.md"] = "# new-B: second\n\nStatus: ready\n",
            ["work/queue/301-B-later.md"] = "# 301-B: later\n\nDepends on: new-B-second\n",
        });
        var models = new MainModels(2);
        var log = new ListLog();
        var ra = Join(models, a, "jobs", log);
        var rb = Join(models, b, "work", log);
        var counter = new CardNumbering(() => CardNumbering.Highest(models.Repositories));
        var stepA = Step(a, ra, counter, "jobs", "alpha", log);
        var stepB = Step(b, rb, counter, "work", "beta", log);

        Assert.True(stepA.Run());
        Assert.True(stepB.Run());

        Assert.Contains("jobs/queue/302-A-first.md", Tree(a));
        Assert.DoesNotContain("jobs/queue/new-A-first.md", Tree(a));
        Assert.Contains("work/queue/303-B-second.md", Tree(b));
        Assert.DoesNotContain("work/queue/new-B-second.md", Tree(b));
        Assert.StartsWith("# 302-A: first", Show(a, "jobs/queue/302-A-first.md"));
        Assert.Contains("Depends on: 303-B", Show(b, "work/queue/301-B-later.md"));
        Assert.Equal("jobs: number new-B-second as 303", GitFixture.Git(b.Fx.Bare, "log", "-1", "--format=%s", "main"));
        Assert.Contains("card numbered: alpha/new-A-first -> 302-A-first", log.Lines);
        Assert.Contains("card numbered: beta/new-B-second -> 303-B-second", log.Lines);

        // The push reached the model at once: the next look finds nothing to number.
        Assert.False(stepA.Run());
        Assert.False(stepB.Run());
        Assert.Equal(303, counter.Last);
    }

    [Fact]
    public void NewCardsOnMain_CounterNotReady_WaitsAndCommitsNothing()
    {
        using var a = Project("jobs", new Dictionary<string, string?> { ["jobs/queue/new-A-first.md"] = "# new-A: first\n" });
        var models = new MainModels(1);
        var log = new ListLog();
        var ra = Join(models, a, "jobs", log);
        var before = GitFixture.Git(a.Fx.Bare, "rev-parse", "main");
        var step = Step(a, ra, new CardNumbering(() => null), "jobs", "", log);

        Assert.False(step.Run());
        Assert.False(step.Run());
        Assert.Equal(before, GitFixture.Git(a.Fx.Bare, "rev-parse", "main"));
        Assert.Single(log.Lines, l => l.Contains("wait until every model of main has loaded", StringComparison.Ordinal));
    }

    private sealed class ProjectRepo(GitFixture fx, string answers) : IDisposable
    {
        public GitFixture Fx { get; } = fx;

        public string Answers { get; } = answers;

        public void Dispose() => Fx.Dispose();
    }

    private static ProjectRepo Project(string jobs, Dictionary<string, string?> files)
    {
        var fx = new GitFixture();
        fx.Push("seed", files);
        foreach (var (k, v) in new[] { ("user.name", "scheduler"), ("user.email", "s@s"), ("commit.gpgsign", "false"), ("core.autocrlf", "false") })
        {
            GitFixture.Git(fx.Repo, "config", k, v);
        }

        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
        return new ProjectRepo(fx, Path.Combine(Path.GetDirectoryName(fx.Repo)!, "answers-" + jobs));
    }

    private static MainModelsRepository Join(MainModels models, ProjectRepo p, string jobs, ISchedulerLog log)
    {
        var repo = models.Join(new MainModelsJoin(p.Fx.Repo, "origin", "main", jobs, TimeSpan.FromSeconds(15), false,
            new GitRunner(), log, new FakeClock(DateTimeOffset.Now)));
        Assert.True(repo.Loading.Wait(Wait), "load did not end");
        Assert.NotNull(repo.Model?.Snapshot);
        return repo;
    }

    private static NewCardStep Step(ProjectRepo p, MainModelsRepository repo, CardNumbering counter, string jobs, string name, ISchedulerLog log)
    {
        var committer = new AnswersCommitter(new GitRunner(), p.Fx.Repo, p.Answers, "origin", "main") { MainModel = () => repo.Model };
        return new NewCardStep(committer, counter, jobs, name, log) { Model = () => repo.Model };
    }

    private static string[] Tree(ProjectRepo p) =>
        GitFixture.Git(p.Fx.Bare, "ls-tree", "-r", "--name-only", "main").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string Show(ProjectRepo p, string path) => GitFixture.Git(p.Fx.Bare, "show", "main:" + path);
}
