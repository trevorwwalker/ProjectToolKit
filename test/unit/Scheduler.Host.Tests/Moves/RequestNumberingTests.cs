// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 1493-B: the scheduler numbers requests as it numbers cards. A worker files <c>requests/new-{slug}.md</c>; on main
/// it becomes <c>{n}-{slug}.md</c>, n next after the highest of open and answered, and the cards' lines follow.
/// </summary>
[ParallelMethods]
public class RequestNumberingTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    [Fact]
    public void OneNewRequest_NumberedNextAfterTheHighestOfOpenAndAnswered()
    {
        using var p = Project(new Dictionary<string, string?>
        {
            ["jobs/requests/README.md"] = "# Requests\n",
            ["jobs/requests/027-open.md"] = "# #027: open\n\nStatus: open\n",
            ["jobs/requests/answered/041-old.md"] = "# #041: old\n\nStatus: answered\n",
            ["jobs/requests/new-power-led.md"] = "# #NNN: The power LED\n\nFor: 1468-E\nStatus: open\n",
        });
        var log = new ListLog();
        var repo = Join(p, log);
        var step = Step(p, repo, "amigax", log);

        Assert.True(step.Run());

        Assert.Contains("jobs/requests/042-power-led.md", Tree(p));
        Assert.DoesNotContain("jobs/requests/new-power-led.md", Tree(p));
        Assert.StartsWith("# #042: The power LED", Show(p, "jobs/requests/042-power-led.md"), StringComparison.Ordinal);
        Assert.Equal("jobs: number request new-power-led as 042", GitFixture.Git(p.Fx.Bare, "log", "-1", "--format=%s", "main"));
        Assert.Contains("request numbered: amigax/new-power-led -> 042-power-led", log.Lines);

        // The push reached the model at once: the next look finds nothing to do.
        Assert.False(step.Run());
    }

    [Fact]
    public void ThreeInOneTick_ConsecutiveNumbersInNameOrder_OneCommit()
    {
        using var p = Project(new Dictionary<string, string?>
        {
            ["jobs/requests/196-before.md"] = "# #196: before\n",
            ["jobs/requests/new-paula.md"] = "# #new: paula\n",
            ["jobs/requests/new-amigasys.md"] = "# #NNN: amigasys\n",
            ["jobs/requests/new-last-error.md"] = "# last error\n",
        });
        var log = new ListLog();
        var repo = Join(p, log);
        var before = GitFixture.Git(p.Fx.Bare, "rev-parse", "main");

        Assert.True(Step(p, repo, "", log).Run());

        var tree = Tree(p);
        Assert.Contains("jobs/requests/197-amigasys.md", tree);
        Assert.Contains("jobs/requests/198-last-error.md", tree);
        Assert.Contains("jobs/requests/199-paula.md", tree);
        Assert.DoesNotContain(tree, path => path.Contains("/new-", StringComparison.Ordinal));
        Assert.StartsWith("# #199: paula", Show(p, "jobs/requests/199-paula.md"), StringComparison.Ordinal);
        Assert.StartsWith("# last error", Show(p, "jobs/requests/198-last-error.md"), StringComparison.Ordinal);
        Assert.Equal(before, GitFixture.Git(p.Fx.Bare, "rev-parse", "main~1"));
        Assert.Equal(["request numbered: new-amigasys -> 197-amigasys", "request numbered: new-last-error -> 198-last-error", "request numbered: new-paula -> 199-paula"],
            log.Lines.Where(l => l.StartsWith("request numbered:", StringComparison.Ordinal)));
    }

    [Fact]
    public void CardLine_RewrittenInTheSameCommit_InQueueAndActive()
    {
        using var p = Project(new Dictionary<string, string?>
        {
            ["jobs/requests/new-power-led.md"] = "# #NNN: power led\n",
            ["jobs/queue/1468-E-led.md"] = "# 1468-E: led\n\nStatus: waiting: planner (request new-power-led)\n",
            ["jobs/active/1471-W-two.md"] = "# 1471-W: two\n\nStatus: waiting: planner (request new-power-led.md)\n",
            ["jobs/queue/1472-E-other.md"] = "# 1472-E: other\n\nStatus: waiting: planner (request new-someone-else)\n",
        });
        var log = new ListLog();
        var repo = Join(p, log);

        Assert.True(Step(p, repo, "", log).Run());

        Assert.Contains("Status: waiting: planner (request #001)", Show(p, "jobs/queue/1468-E-led.md"), StringComparison.Ordinal);
        Assert.Contains("Status: waiting: planner (request #001)", Show(p, "jobs/active/1471-W-two.md"), StringComparison.Ordinal);
        Assert.Contains("(request new-someone-else)", Show(p, "jobs/queue/1472-E-other.md"), StringComparison.Ordinal);
        var changed = GitFixture.Git(p.Fx.Bare, "show", "--name-only", "--format=", "main").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("jobs/queue/1468-E-led.md", changed);
        Assert.Contains("jobs/active/1471-W-two.md", changed);
        Assert.Contains("jobs/requests/001-power-led.md", changed);
    }

    // The card's move can land after its request was numbered: the next look rewrites the line from the number on main.
    [Fact]
    public void CardLine_LandingAfterTheNumbering_RewrittenOnTheNextLook()
    {
        using var p = Project(new Dictionary<string, string?>
        {
            ["jobs/requests/answered/030-power-led.md"] = "# #030: power led\n",
            ["jobs/queue/1468-E-led.md"] = "# 1468-E: led\n\nStatus: waiting: planner (request new-power-led)\n",
        });
        var log = new ListLog();
        var repo = Join(p, log);

        Assert.True(Step(p, repo, "", log).Run());

        Assert.Contains("(request #030)", Show(p, "jobs/queue/1468-E-led.md"), StringComparison.Ordinal);
        Assert.Equal("jobs: request numbers in 1468-E-led", GitFixture.Git(p.Fx.Bare, "log", "-1", "--format=%s", "main"));
    }

    [Fact]
    public void UnnumberedRequest_IsListedByTheReader_ThroughGitAndTheModel()
    {
        using var p = Project(new Dictionary<string, string?>
        {
            ["jobs/requests/012-numbered.md"] = "# #012: numbered\n\nStatus: open\n",
            ["jobs/requests/new-not-yet.md"] = "# #NNN: not yet\n\nStatus: open\n",
        });
        var log = new ListLog();
        var repo = Join(p, log);
        var main = p.Fx.Main(new GitRunner());
        Assert.True(main.Fetch());

        foreach (var reader in new[] { new RequestReader(main, "jobs"), new RequestReader(main, "jobs") { Feed = repo } })
        {
            var r = Assert.Single(reader.Read(), r => r.Name == "new-not-yet");
            Assert.Equal("#NNN: not yet", r.Title);
            Assert.True(r.Open);
        }
    }

    // At start: a number used by several files is logged once, and nothing is renamed.
    [Fact]
    public void ThreeFilesNumbered197_OneLineAtStart_NothingRenamed()
    {
        using var p = Project(new Dictionary<string, string?>
        {
            ["jobs/requests/197-amigasys-reset.md"] = "# #197: a\n",
            ["jobs/requests/197-last-error.md"] = "# #197: b\n",
            ["jobs/requests/answered/197-paula-tests.md"] = "# #197: c\n",
            ["jobs/requests/198-fine.md"] = "# #198: d\n",
        });
        var log = new ListLog();
        var repo = Join(p, log);
        var before = GitFixture.Git(p.Fx.Bare, "rev-parse", "main");
        var step = Step(p, repo, "amigax", log);

        Assert.False(step.Run());
        Assert.False(step.Run());

        Assert.Equal(before, GitFixture.Git(p.Fx.Bare, "rev-parse", "main"));
        var line = Assert.Single(log.Lines, l => l.StartsWith("requests: number", StringComparison.Ordinal));
        Assert.Equal("requests: number 197 is used by 3 files: amigax/197-amigasys-reset.md, amigax/197-last-error.md, amigax/answered/197-paula-tests.md", line);
    }

    private sealed class ProjectRepo(GitFixture fx, string answers) : IDisposable
    {
        public GitFixture Fx { get; } = fx;

        public string Answers { get; } = answers;

        public void Dispose() => Fx.Dispose();
    }

    private static ProjectRepo Project(Dictionary<string, string?> files)
    {
        var fx = new GitFixture();
        fx.Push("seed", files);
        foreach (var (k, v) in new[] { ("user.name", "scheduler"), ("user.email", "s@s"), ("commit.gpgsign", "false"), ("core.autocrlf", "false") })
        {
            GitFixture.Git(fx.Repo, "config", k, v);
        }

        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
        return new ProjectRepo(fx, Path.Combine(Path.GetDirectoryName(fx.Repo)!, "answers-jobs"));
    }

    private static MainModelsRepository Join(ProjectRepo p, ISchedulerLog log)
    {
        var repo = new MainModels(1).Join(new MainModelsJoin(p.Fx.Repo, "origin", "main", "jobs", TimeSpan.FromSeconds(15), false,
            new GitRunner(), log, new FakeClock(DateTimeOffset.Now)));
        Assert.True(repo.Loading.Wait(Wait), "load did not end");
        Assert.NotNull(repo.Model?.Snapshot);
        return repo;
    }

    private static RequestNumberStep Step(ProjectRepo p, MainModelsRepository repo, string name, ISchedulerLog log)
    {
        var committer = new AnswersCommitter(new GitRunner(), p.Fx.Repo, p.Answers, "origin", "main") { MainModel = () => repo.Model };
        return new RequestNumberStep(committer, "jobs", name, log) { Model = () => repo.Model };
    }

    private static string[] Tree(ProjectRepo p) =>
        GitFixture.Git(p.Fx.Bare, "ls-tree", "-r", "--name-only", "main").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string Show(ProjectRepo p, string path) => GitFixture.Git(p.Fx.Bare, "show", "main:" + path);
}
