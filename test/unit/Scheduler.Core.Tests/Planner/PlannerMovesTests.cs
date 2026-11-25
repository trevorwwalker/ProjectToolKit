// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Planner;

namespace Scheduler.Core.Tests.Planner;

/// <summary>PLAN-5: moves and removals. Test plan: planner.md.</summary>
public class PlannerMovesTests
{
    private const string Queue = "docs/jobs/queue/010-A-x.md";
    private const string Active = "docs/jobs/active/010-A-x.md";
    private const string Done = "docs/jobs/done/010-A-x.md";

    private static PlannerPlan Run(PlannerFixture fx, params object[] ops) => fx.Batch("m", ops).Plan();

    private static object Move(string from, string to) => new { op = "move", from, to };

    private static object Remove(string path) => new { op = "remove", path };

    // PLAN-5
    [Fact]
    public void Planner_Moves_And_Removes()
    {
        // Same path: skipped.
        var plan = Run(new PlannerFixture().Main(Queue, "x\n"), Move(Queue, Queue));
        Assert.True(plan.Ok, plan.Error);
        Assert.True(plan.NothingToCommit);
        Assert.Equal($"move of {Queue} to itself skipped", plan.Note);

        // Source moved elsewhere (neither end is there): that file is moved.
        plan = Run(new PlannerFixture().Main(Active, "x\n"), Move(Queue, Done));
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal([new FileMove { From = Active, To = Done }], plan.Change!.Moves);
        Assert.Equal($"{Queue} had moved to {Active}: moved from there", plan.Note);

        // Target present and source absent: skipped.
        plan = Run(new PlannerFixture().Main(Done, "x\n"), Move(Queue, Done));
        Assert.True(plan.Ok, plan.Error);
        Assert.True(plan.NothingToCommit);
        Assert.Equal($"{Done} is already there: move skipped", plan.Note);

        // Both present: fails.
        plan = Run(new PlannerFixture().Main(Queue, "x\n").Main(Done, "y\n"), Move(Queue, Done));
        Assert.False(plan.Ok);
        Assert.Equal($"both {Queue} and {Done} are on main", plan.Error);

        // Source and target both absent, nowhere else: fails.
        plan = Run(new PlannerFixture().Main("docs/jobs/queue/020-B-y.md", "x\n"), Move(Queue, Done));
        Assert.False(plan.Ok);
        Assert.Equal($"{Queue} is not on main", plan.Error);

        // Normal move.
        plan = Run(new PlannerFixture().Main(Queue, "x\n"), Move(Queue, Active));
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal([new FileMove { From = Queue, To = Active }], plan.Change!.Moves);
        Assert.Equal("", plan.Note);

        // Remove: absent is skipped, present is removed.
        plan = Run(new PlannerFixture().Main(Active, "x\n"), Remove(Queue));
        Assert.True(plan.Ok, plan.Error);
        Assert.True(plan.NothingToCommit);
        Assert.Equal($"{Queue} was already gone", plan.Note);

        plan = Run(new PlannerFixture().Main(Queue, "x\n"), Remove(Queue));
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal([Queue], plan.Change!.Removals);
    }

    // PLAN-4, PLAN-5: writes come first, so a card written new can be moved in the same batch,
    // and a card written then moved ends up written at its final place.
    [Fact]
    public void Planner_Moves_FollowWrites()
    {
        var fx = new PlannerFixture().InBatch("f1.md", "new card\n");
        var plan = Run(fx, new { op = "write", path = Queue, @base = "new", file = "f1.md" }, Move(Queue, Active));

        Assert.True(plan.Ok, plan.Error);
        Assert.Empty(plan.Change!.Moves);
        Assert.Equal("new card\n", plan.Change.Writes[Active]);
        Assert.Single(plan.Change.Writes);
        Assert.Equal([new WrittenFile(Active, "new card\n")], plan.Written);

        var old = "old\n";
        fx = new PlannerFixture().Main(Queue, old).InBatch("f1.md", "edited\n");
        plan = Run(fx, new { op = "write", path = Queue, @base = fx.Blob(old), file = "f1.md" }, Move(Queue, Active));

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal([new FileMove { From = Queue, To = Active }], plan.Change!.Moves);
        Assert.Equal("edited\n", plan.Change.Writes[Active]);
        Assert.Equal([new WrittenFile(Active, "edited\n")], plan.Written);
    }
}
