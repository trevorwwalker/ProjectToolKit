using Scheduler.Core.Planner;

namespace Scheduler.Core.Tests.Planner;

/// <summary>PLAN-4: writes, line endings, following a moved file, merging. Test plan: planner.md.</summary>
public class PlannerWriteTests
{
    private const string Queue = "docs/jobs/queue/010-A-x.md";
    private const string Active = "docs/jobs/active/010-A-x.md";

    private static object Write(string path, string @base, string file = "f1.md") =>
        new { op = "write", path, @base, file };

    // PLAN-4
    [Fact]
    public void Planner_Write_New()
    {
        // Absent: written.
        var fx = new PlannerFixture().InBatch("f1.md", "# New\n").Batch("m", Write(Queue, "new"));
        var plan = fx.Plan();
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("# New\n", plan.Change!.Writes[Queue]);
        Assert.Equal("", plan.Note);

        // Present with the same text apart from CRLF and trailing whitespace: noted, skipped.
        fx = new PlannerFixture().Main(Queue, "# New\r\n  \r\n").InBatch("f1.md", "# New\n").Batch("m", Write(Queue, "new"));
        plan = fx.Plan();
        Assert.True(plan.Ok, plan.Error);
        Assert.Empty(plan.Change!.Writes);
        Assert.Equal($"{Queue} was already on main as written", plan.Note);

        // Present and different: the batch fails.
        fx = new PlannerFixture().Main(Queue, "# Other\n").InBatch("f1.md", "# New\n").Batch("m", Write(Queue, "new"));
        plan = fx.Plan();
        Assert.False(plan.Ok);
        Assert.Equal($"{Queue} is already on main (the batch writes it as new)", plan.Error);
    }

    // PLAN-4
    [Fact]
    public void Planner_Write_BaseMatchesBlob_KeepsLineEndings()
    {
        var current = "# T\r\nStatus: ready\r\n";
        var fx = new PlannerFixture().Main(Queue, current).InBatch("f1.md", "# T\nStatus: done\n");
        fx.Batch("m", Write(Queue, fx.Blob(current)));

        var plan = fx.Plan();

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("# T\r\nStatus: done\r\n", plan.Change!.Writes[Queue]);
    }

    // PLAN-4
    [Fact]
    public void Planner_Write_MovedSinceBase_FollowsSameName()
    {
        var current = "# T\nStatus: active\n";
        var fx = new PlannerFixture().Main(Active, current).InBatch("f1.md", "# T\nStatus: done\n");
        fx.Batch("m", Write(Queue, fx.Blob(current)));

        var plan = fx.Plan();

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("# T\nStatus: done\n", plan.Change!.Writes[Active]);
        Assert.Single(plan.Change.Writes);
        Assert.Equal($"{Queue} had moved to {Active}: written there", plan.Note);
        Assert.Equal([new WrittenFile(Active, "# T\nStatus: done\n")], plan.Written);
    }

    // PLAN-4
    [Fact]
    public void Planner_Write_Merge_AppendOrFail()
    {
        // Base older than main, non-overlapping change: merged three ways.
        var fx = new PlannerFixture();
        var old = "l1\nl2\nl3\nl4\nl5\n";
        fx.Main(Queue, "L1\nl2\nl3\nl4\nl5\n").InBatch("f1.md", "l1\nl2\nl3\nl4\nL5\n").Batch("m", Write(Queue, fx.Blob(old)));
        var plan = fx.Plan();
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("L1\nl2\nl3\nl4\nL5\n", plan.Change!.Writes[Queue]);
        Assert.Equal($"{Queue} merged with main's newer copy", plan.Note);

        // Both add at the end (overlapping), the batch only appends to its base: appended.
        fx = new PlannerFixture();
        old = "A\nB\n";
        fx.Main(Queue, "A\nB\nmain-added\n").InBatch("f1.md", "A\nB\nplanner-added\n").Batch("m", Write(Queue, fx.Blob(old)));
        plan = fx.Plan();
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("A\nB\nmain-added\n\nplanner-added\n", plan.Change!.Writes[Queue]);

        // Appending keeps main's line endings.
        fx = new PlannerFixture();
        fx.Main(Queue, "A\r\nB\r\nmain-added\r\n").InBatch("f1.md", "A\nB\nplanner-added\n").Batch("m", Write(Queue, fx.Blob(old)));
        plan = fx.Plan();
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("A\r\nB\r\nmain-added\r\n\r\nplanner-added\r\n", plan.Change!.Writes[Queue]);

        // A real conflict: fails.
        fx = new PlannerFixture();
        fx.Main(Queue, "A\nX\n").InBatch("f1.md", "A\nY\n").Batch("m", Write(Queue, fx.Blob(old)));
        plan = fx.Plan();
        Assert.False(plan.Ok);
        Assert.Equal($"{Queue} changed on main since the planner read it, and the changes conflict", plan.Error);

        // Both made the same change: nothing to merge.
        fx = new PlannerFixture();
        fx.Main(Queue, "A\nX\n").InBatch("f1.md", "A\nX\n").Batch("m", Write(Queue, fx.Blob(old)));
        plan = fx.Plan();
        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("A\nX\n", plan.Change!.Writes[Queue]);

        // Path gone with no file of that name anywhere: fails.
        fx = new PlannerFixture();
        fx.Main("docs/jobs/queue/020-B-y.md", "other\n").InBatch("f1.md", "A\nY\n").Batch("m", Write(Queue, fx.Blob(old)));
        plan = fx.Plan();
        Assert.False(plan.Ok);
        Assert.Equal($"{Queue} is no longer on main (moved or removed since the planner read it)", plan.Error);
    }

    // PLAN-4: base 'new' for a path that is not there is never followed elsewhere.
    [Fact]
    public void Planner_Write_New_IsNotFollowedToAnotherFolder()
    {
        var fx = new PlannerFixture().Main(Active, "x\n").InBatch("f1.md", "y\n").Batch("m", Write(Queue, "new"));

        var plan = fx.Plan();

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("y\n", plan.Change!.Writes[Queue]);
    }
}
