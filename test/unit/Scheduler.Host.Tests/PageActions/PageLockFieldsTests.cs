// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Core.PageModel;
using Scheduler.Core.Scopes;
using Scheduler.Host.PageActions;
using Scheduler.Host.State;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>1188-B (DL-24 to DL-26): the page's lock fields are filled from the lock table.</summary>
public class PageLockFieldsTests
{
    private const string Categories = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    private const string Workers = """
        {"slots":2,"workers":{"1":{"name":"w1","model":"claude-opus-5-5","premium":true},"2":{"name":"w2","model":"qwen"}}}
        """;

    // DL-24: a running card's window lists what it holds; DL-25: a yielded queued card says what it waits for.
    [Fact]
    public void Snapshot_FillsHoldsAndYielded_FromTheLockTable()
    {
        using var harness = new PageActionHarness(categoriesJson: Categories, workersJson: Workers, project: "ptk-1188b");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Active, 10, "A"));
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "B"));
        harness.Tick_Once();
        harness.Tick.Adopt(harness.Running(1, "010-A-x"));
        var t0 = harness.Clock.Now.AddMinutes(-10);
        var table = LockTable.Empty("r")
            .Start("010-A-x", new CardAge(1, 10), Scope.Of("src/a/"), t0, slot: 1)
            .Grant("010-A-x", ["docs/known_gaps.md"], t0)
            .Start("020-B-x", new CardAge(1, 20), Scope.Of("src/b/"), t0)
            .Release("020-B-x", LockRelease.Yielded, ["docs/known_gaps.md"]);
        var sources = new PageSnapshotSources { Locks = () => table };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);

        var holds = snapshot.Slots[0].Holds;
        Assert.NotNull(holds);
        Assert.Contains("src/a/", holds.Claim.Select(c => c.TrimEnd('*', '/') + "/"));
        Assert.Contains(holds.Also, e => e.Path == "docs/known_gaps.md");
        Assert.Null(snapshot.Slots[1].Holds);
        Assert.Equal("yielded: waits for `docs/known_gaps.md` held by `010-A-x`", snapshot.Queue.Single(q => q.Name == "020-B-x").Yielded);
    }

    // Locks off: no source, no fields.
    [Fact]
    public void Snapshot_WithoutLocks_LeavesTheFieldsEmpty()
    {
        using var harness = new PageActionHarness(categoriesJson: Categories, workersJson: Workers, project: "ptk-1188b-off");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "B"));
        harness.Tick_Once();
        harness.Tick.Adopt(harness.Running(1, "010-A-x"));

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        Assert.Null(snapshot.Slots[0].Holds);
        Assert.All(snapshot.Queue, q => Assert.Equal("", q.Yielded));
    }

    // DL-26: the pool page's project row carries today's counts, from the project's events.
    [Fact]
    public void Pool_FillsLockCounts_PerProject()
    {
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var pool = new Scheduler.Core.WebContract.PoolPageSnapshot
        {
            Built = now,
            Projects = [new Scheduler.Core.WebContract.PoolProjectView { Name = "p", Repo = "r" }, new Scheduler.Core.WebContract.PoolProjectView { Name = "q", Repo = "r2" }],
        };
        var events = new Scheduler.Host.Locks.LockEventLog();
        events.Add(LockEventKind.Grant, now);
        events.Add(LockEventKind.Grant, now);
        events.Add(LockEventKind.Yield, now);
        events.Add(LockEventKind.Grant, now.AddDays(-1));
        var data = new PoolPageData(clock, Path.GetTempPath(), () => [], () => pool,
            (name, at) => name == "p" ? PageLocks.Counts(events.Snapshot(), DateOnly.FromDateTime(at.DateTime)) : null);

        var projects = data.PoolSnapshot().Projects;

        Assert.Equal(2, projects[0].LockCounts!.Grants);
        Assert.Equal(1, projects[0].LockCounts!.Yields);
        Assert.Null(projects[1].LockCounts);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }

    private static Card Card(CardFolder folder, int num, string category) => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{num:000}-{category}-x.md",
        Name = $"{num:000}-{category}-x",
        Num = num,
        Category = category,
        Id = $"{num:000}-{category}",
        Title = $"card {num}",
        Status = "ready",
    };
}
