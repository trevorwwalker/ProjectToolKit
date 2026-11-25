// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;
using static Scheduler.Core.Tests.Pool.PoolFakes;

namespace Scheduler.Core.Tests.Pool;

/// <summary>
/// Card 1029-B: each project has its own number of workspaces (slot folders); at most that many of its
/// workers run at once, while the pool's <c>maxWorkers</c> still bounds the total. Projects P (weight 70) and O (30).
/// </summary>
public class PoolWorkspacesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static Card Card(string name)
    {
        var parts = name.Split('-');
        return new Card
        {
            Folder = CardFolder.Queue,
            Path = $"jobs/queue/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Status = "ready",
            Prio = 1,
        };
    }

    private static PoolProject Project(string name, int weight, int? workspaces, int busy, params Card[] cards) =>
        new(name, weight, DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2)) { Workspaces = workspaces, Busy = busy };

    private static Card[] Cards(string prefix) =>
        [Card($"010-A-{prefix}1"), Card($"020-B-{prefix}2"), Card($"030-C-{prefix}3"), Card($"040-D-{prefix}4")];

    // A project whose workspaces are all busy is skipped, and another project's card starts though P is far below its share.
    [Fact]
    public void Workspaces_AllBusy_ProjectSkipped_OtherProjectStarts()
    {
        var p = Project("P", 70, workspaces: 2, busy: 2, Cards("p"));
        var o = Project("O", 30, workspaces: null, busy: 0, Cards("o"));

        var started = StartAll([p, o], [Type("w1")], Now, maxWorkers: 8, totalRunning: 2);

        Assert.NotEmpty(started);
        Assert.All(started, s => Assert.Equal("O", s.Project));
    }

    // Within one tick a project's own starts fill its workspaces: with 2 workspaces and 1 busy, one more of its cards starts.
    [Fact]
    public void Workspaces_StartsInOneTick_StopAtTheProjectsCount()
    {
        var p = Project("P", 70, workspaces: 2, busy: 1, Cards("p"));

        var started = StartAll([p], [Type("w1")], Now, maxWorkers: 8, totalRunning: 1);

        Assert.Single(started);
        Assert.Equal("P", started[0].Project);
    }

    // The total still stops at maxWorkers, however many workspaces the projects have.
    [Fact]
    public void Workspaces_TotalStillStopsAtMaxWorkers()
    {
        var p = Project("P", 70, workspaces: 4, busy: 0, Cards("p"));
        var o = Project("O", 30, workspaces: 4, busy: 0, Cards("o"));

        var started = StartAll([p, o], [Type("w1")], Now, maxWorkers: 3);

        Assert.Equal(3, started.Count);
        Assert.True(started.Count(s => s.Project == "P") <= 4 && started.Count(s => s.Project == "O") <= 4);
    }

    // A missing setting is the pool's maxWorkers, so today's pools are unchanged.
    [Fact]
    public void Workspaces_Missing_EqualsMaxWorkers()
    {
        Assert.Equal(8, PoolDispatcher.WorkspacesOf(null, 8));
        Assert.Equal(3, PoolDispatcher.WorkspacesOf(3, 8));

        var p = Project("P", 70, workspaces: null, busy: 0, Cards("p"));
        Assert.Equal(4, StartAll([p], [Type("w1")], Now, maxWorkers: 4).Count);
        Assert.Equal(3, StartAll([p with { Busy = 1 }], [Type("w1")], Now, maxWorkers: 4, totalRunning: 1).Count);
    }

    // A lowered count with more workers still running than it allows: none is added, and they are not stopped (they finish in place).
    [Fact]
    public void Workspaces_LoweredBelowRunning_StartsNothingMore()
    {
        var p = Project("P", 70, workspaces: 1, busy: 3, Cards("p"));

        Assert.Empty(StartAll([p], [Type("w1")], Now, maxWorkers: 8, totalRunning: 3));
        Assert.False(PoolDispatcher.HasFreeWorkspace(p, 3, 8));
    }

    // The Queue note of a project whose workspaces are all busy.
    [Fact]
    public void Workspaces_WaitNote()
    {
        Assert.Equal("next up; waits for a workspace (2 of 2 busy)", PoolDispatcher.WaitsForWorkspaceNote(2, 2));
    }
}
