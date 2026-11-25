// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Estimates;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Program;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.State;

/// <summary>
/// 1373-B: the review stays fed from the tick (EST-17, EST-19, EST-29), over a fixture repository and its model of main:
/// the feed <see cref="ProjectParts"/> wires to each tick's planner step, with the estimates file on the stays' trigger.
/// </summary>
public sealed class ReviewStaysFeedTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);
    private readonly List<IDisposable> _owned = [];
    private readonly FakeClock _clock = new(DateTimeOffset.Now);

    public void Dispose()
    {
        foreach (var d in _owned)
        {
            d.Dispose();
        }
    }

    // One project: its repository (a card through review to done, a second card in review), log folder and parts.
    private sealed class Project
    {
        public required GitFixture Fx { get; init; }
        public required MainModelsRepository Model { get; init; }
        public required GitRunner Git { get; init; }
        public required ReviewStaysFile Stays { get; init; }
        public required EstimatesFile Estimates { get; init; }
        public required ReviewStaysFeed Feed { get; init; }
        public required ListLog Log { get; init; }
        public required string Logs { get; init; }
    }

    private Project Make(string name, string card1, string card2, EstimatesPool? pool = null,
        Func<string?, string, IReadOnlyList<MainCommit>?>? inProcess = null)
    {
        var fx = new GitFixture();
        _owned.Add(fx);
        fx.Push("start", new Dictionary<string, string?> { ["README.md"] = "x", [$"jobs/active/{card1}.md"] = "a" });
        fx.Push($"{card1} to review", new Dictionary<string, string?> { [$"jobs/active/{card1}.md"] = null, [$"jobs/review/{card1}.md"] = "a" });
        fx.Push("code", new Dictionary<string, string?> { ["src/x.cs"] = "x" });
        fx.Push($"{card1} done", new Dictionary<string, string?> { [$"jobs/review/{card1}.md"] = null, [$"jobs/done/{card1}.md"] = "a" });
        fx.Push($"{card2} to review", new Dictionary<string, string?> { [$"jobs/review/{card2}.md"] = "b" });
        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");

        var log = new ListLog();
        var model = new MainModels(2).Join(new MainModelsJoin(fx.Repo, "origin", "main", "jobs", TimeSpan.FromSeconds(15), false,
            new GitRunner(), log, _clock));
        Assert.True(model.Loading.Wait(Wait), "the model did not load");
        Assert.True(model.Model?.Snapshot?.DoneLoaded, "phase 2 did not run");

        var logs = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "logs-" + name);
        Directory.CreateDirectory(logs);
        var git = new GitRunner();
        var stays = new ReviewStaysFile(logs, fx.Repo, "jobs", git, log, inProcess);
        var estimates = new EstimatesFile(name, logs, Path.Combine(fx.Repo, "jobs", ".logs", "estimates.json"), _clock, "9.9.9", log, pool,
            () => stays.Open);
        stays.Changed += estimates.Changed;
        estimates.Changed();   // start-up, as ProjectParts does
        var feed = new ReviewStaysFeed(stays, () => model.Model?.Snapshot, log);
        return new Project { Fx = fx, Model = model, Git = git, Stays = stays, Estimates = estimates, Feed = feed, Log = log, Logs = logs };
    }

    private static void Ticked(Project p)
    {
        Assert.True(p.Feed.Tick());
        Assert.True(Ended(p.Feed.Advancing), "the advance did not end");
    }

    private static bool Ended(Task task) => task.Wait(Wait);

    private static string[] Lines(Project p) => File.ReadAllLines(p.Stays.Path);

    private static EstimatesReport Report(Project p) => EstimatesReport.FromJson(File.ReadAllText(p.Estimates.Path))!;

    [Fact]
    public void AfterAStartAndOneTick_TheStaysFileHoldsTheFixturesStays_AndTheReportsReviewTableHasThem()
    {
        var p = Make("P", "1-A-a", "2-B-b");

        Ticked(p);

        Assert.Equal(2, Lines(p).Length);   // the header and 1-A-a's closed stay
        Assert.Equal(("1-A-a", "done"), (p.Stays.Closed[0].Card, p.Stays.Closed[0].To));
        Assert.Equal("2-B-b", Assert.Single(p.Stays.Open).Card);
        Assert.Equal(p.Model.Model!.Snapshot!.Commit, p.Stays.Head);
        var report = Report(p);
        Assert.Equal(1, report.Review.Single(r => r.Kind == "all").ToDone);
        Assert.Equal(1, report.ReviewOpen.Count);
    }

    [Fact]
    public void EST_17_ASecondTickWithNoNewCommit_StartsNoProcess_AndWritesNoFile()
    {
        var p = Make("P", "1-A-a", "2-B-b");
        Ticked(p);
        var started = p.Git.Started;
        var walks = p.Stays.Walks;
        var writes = p.Estimates.Writes;
        var staysTime = File.GetLastWriteTimeUtc(p.Stays.Path);
        var reportTime = File.GetLastWriteTimeUtc(p.Estimates.Path);

        for (var i = 0; i < 3; i++)
        {
            _ = p.Model.Tick();   // the tick's own ref read, in process
            Assert.False(p.Feed.Tick());
        }

        Assert.Equal(1, p.Feed.Started);
        Assert.Equal(started, p.Git.Started);
        Assert.Equal(walks, p.Stays.Walks);
        Assert.Equal(writes, p.Estimates.Writes);
        Assert.Equal(staysTime, File.GetLastWriteTimeUtc(p.Stays.Path));
        Assert.Equal(reportTime, File.GetLastWriteTimeUtc(p.Estimates.Path));
    }

    [Fact]
    public void EST_29_ACommitMovingACardOutOfReview_AppendsOneStay_AndRewritesTheReportOnce()
    {
        var p = Make("P", "1-A-a", "2-B-b");
        Ticked(p);
        var lines = Lines(p).Length;
        var writes = p.Estimates.Writes;

        p.Fx.Push("2-B-b done", new Dictionary<string, string?> { ["jobs/review/2-B-b.md"] = null, ["jobs/done/2-B-b.md"] = "b" });
        GitFixture.Git(p.Fx.Repo, "fetch", "-q", "origin");
        Assert.True(p.Model.Tick());
        Ticked(p);

        Assert.Equal(lines + 1, Lines(p).Length);
        Assert.Equal(("2-B-b", "done"), (p.Stays.Closed[^1].Card, p.Stays.Closed[^1].To));
        Assert.Empty(p.Stays.Open);
        Assert.Equal(writes + 1, p.Estimates.Writes);
        Assert.Equal(2, Report(p).Review.Single(r => r.Kind == "all").ToDone);
        Assert.Equal(1, p.Stays.Walks);   // the model served the new commit
    }

    [Fact]
    public void EST_19_TheFirstWalk_RunsOffTheTicksThread_AndATickNeverWaitsForIt()
    {
        var tickThread = Environment.CurrentManagedThreadId;
        int? walkThread = null;
        using var release = new ManualResetEventSlim(false);
        var p = Make("P", "1-A-a", "2-B-b", inProcess: (_, _) =>
        {
            walkThread = Environment.CurrentManagedThreadId;
            Assert.True(release.Wait(Wait));
            return null;   // git answers
        });

        Assert.True(p.Feed.Tick());   // returns while the walk is held
        Assert.False(p.Feed.Advancing.IsCompleted);
        Assert.False(p.Feed.Tick());  // a walk still running: the tick skips
        Assert.Equal(1, p.Feed.Started);

        release.Set();
        Assert.True(Ended(p.Feed.Advancing));
        Assert.NotNull(walkThread);
        Assert.NotEqual(tickThread, walkThread);
        Assert.Equal("2-B-b", Assert.Single(p.Stays.Open).Card);
    }

    [Fact]
    public void AFailedWalk_IsLoggedOnce_IsNotRetriedOnTheSameCommit_AndTheReportIsStillWritten()
    {
        var p = Make("P", "1-A-a", "2-B-b", inProcess: (_, _) => throw new InvalidOperationException("history unreadable"));
        var writes = p.Estimates.Writes;

        Ticked(p);
        Assert.False(p.Feed.Tick());

        Assert.Single(p.Log.Lines, l => l.Contains("history unreadable", StringComparison.Ordinal));
        Assert.Equal(1, p.Feed.Started);
        Assert.Equal(writes, p.Estimates.Writes);
        Assert.DoesNotContain(Report(p).Review, r => r.ToDone > 0);   // written at start-up, without the stays
    }

    [Fact]
    public void APoolOfTwo_GivesEachMemberItsOwnStays_AndThePoolsReportHasBoth()
    {
        var pool = new EstimatesPool(_clock, "9.9.9");
        var one = Make("one", "1-A-a", "2-B-b", pool);
        var two = Make("two", "7-C-c", "8-D-d", pool);

        Ticked(one);
        Ticked(two);

        Assert.Equal(["1-A-a"], one.Stays.Closed.Select(s => s.Card));
        Assert.Equal(["7-C-c"], two.Stays.Closed.Select(s => s.Card));
        Assert.Equal("2-B-b", Assert.Single(one.Stays.Open).Card);
        Assert.Equal("8-D-d", Assert.Single(two.Stays.Open).Card);
        var poolReport = EstimatesReport.FromJson(pool.Json!)!;
        Assert.Equal(2, poolReport.Review.Single(r => r.Kind == "all").ToDone);
        Assert.Equal(2, poolReport.ReviewOpen.Count);
    }
}
