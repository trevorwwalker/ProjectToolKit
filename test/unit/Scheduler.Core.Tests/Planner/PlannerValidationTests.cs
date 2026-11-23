namespace Scheduler.Core.Tests.Planner;

/// <summary>PLAN-3: a batch that does not validate fails whole, with nothing written. Test plan: planner.md.</summary>
public class PlannerValidationTests
{
    private static PlannerFixture Fx() => new PlannerFixture()
        .Main("docs/jobs/queue/010-A-x.md", "old\n")
        .InBatch("f1.md", "new\n");

    private static string One(string op) => "{\"message\": \"m\", \"ops\": [" + op + "]}";

    // PLAN-3
    [Theory]
    [InlineData("{ not json", "batch.json is not valid JSON")]
    [InlineData("", "batch.json is not valid JSON")]
    [InlineData("{\"message\": \"  \", \"ops\": [{\"op\":\"remove\",\"path\":\"docs/jobs/queue/010-A-x.md\"}]}", "batch.json has no message")]
    [InlineData("{\"ops\": [{\"op\":\"remove\",\"path\":\"docs/jobs/queue/010-A-x.md\"}]}", "batch.json has no message")]
    [InlineData("{\"message\": \"m\", \"ops\": []}", "batch.json has no ops")]
    [InlineData("{\"message\": \"m\"}", "batch.json has no ops")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"scheduler/x.md\",\"base\":\"new\",\"file\":\"f1.md\"}]}", "write to scheduler/x.md: only files under docs/jobs/ can be written")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/../x.md\",\"base\":\"new\",\"file\":\"f1.md\"}]}", "write to docs/jobs/../x.md: only files under docs/jobs/ can be written")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/queue\\\\x.md\",\"base\":\"new\",\"file\":\"f1.md\"}]}", "write to docs/jobs/queue\\x.md: only files under docs/jobs/ can be written")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/c:x.md\",\"base\":\"new\",\"file\":\"f1.md\"}]}", "write to docs/jobs/c:x.md: only files under docs/jobs/ can be written")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/queue/n.md\",\"base\":\"new\",\"file\":\"missing.md\"}]}", "write to docs/jobs/queue/n.md: its file missing.md is not in the batch")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/queue/n.md\",\"base\":\"new\"}]}", "write to docs/jobs/queue/n.md: its file  is not in the batch")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/queue/n.md\",\"base\":\"new\",\"file\":\"../f1.md\"}]}", "write to docs/jobs/queue/n.md: its file ../f1.md is not in the batch")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"write\",\"path\":\"docs/jobs/queue/n.md\",\"file\":\"f1.md\"}]}", "write to docs/jobs/queue/n.md: no base")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"move\",\"from\":\"docs/jobs/queue/a.md\",\"to\":\"other/a.md\"}]}", "move docs/jobs/queue/a.md to other/a.md: only files under docs/jobs/ can be moved")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"remove\",\"path\":\"/etc/x\"}]}", "remove /etc/x: only files under docs/jobs/ can be removed")]
    [InlineData("{\"message\": \"m\", \"ops\": [{\"op\":\"copy\",\"path\":\"docs/jobs/queue/a.md\"}]}", "unknown op 'copy'")]
    public void Planner_Validation_Failures(string batchJson, string error)
    {
        var fx = Fx();
        fx.BatchJsonText = batchJson;

        var plan = fx.Plan();

        Assert.False(plan.Ok);
        Assert.Equal(error, plan.Error);
        Assert.Null(plan.Change);
        Assert.Equal("", plan.Note);
    }

    // PLAN-3: one bad op fails the whole batch, even after good ones.
    [Fact]
    public void Planner_Validation_OneBadOpFailsTheBatch()
    {
        var fx = Fx().Batch("m",
            new { op = "write", path = "docs/jobs/queue/n.md", @base = "new", file = "f1.md" },
            new { op = "copy", path = "docs/jobs/queue/a.md" });

        var plan = fx.Plan();

        Assert.False(plan.Ok);
        Assert.Null(plan.Change);
        Assert.Equal("m", plan.Message);
    }

    // PLAN-3, PLAN-6: the message loses its double quotes, trailers follow after a blank line.
    [Fact]
    public void Planner_Validation_MessageAndTrailers()
    {
        var fx = Fx().Batch("  jobs: \"close\" 1  ", new { op = "remove", path = "docs/jobs/queue/010-A-x.md" });

        var plan = fx.Plan();

        Assert.True(plan.Ok, plan.Error);
        Assert.Equal("jobs: 'close' 1", plan.Message);
        Assert.Equal("jobs: 'close' 1\n\nCo-Authored-By: P <p@x>", plan.CommitMessage);
    }
}
