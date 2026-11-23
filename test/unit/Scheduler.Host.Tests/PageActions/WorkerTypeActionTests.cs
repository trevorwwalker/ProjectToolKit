using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>The page's worker type actions (WT-C7, WT-C11, WT-9). Test plan: worker-types.md.</summary>
public class WorkerTypeActionTests
{
    private const string Categories = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    // WT-C11: the first Save writes the pool form - no slots, no workers - and commits it as
    // "scheduler: workers - types ..."; a name Premium or only digits is refused and writes nothing.
    [Fact]
    public void WorkerTypes_Save_WritesPoolForm_DropsSlots()
    {
        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(categoriesJson: Categories, committer: committer, project: "ptk-880b-save");
        harness.Tick_Once();

        var bad = harness.Actions.SaveWorkerTypes(2, [new PageWorkerTypeView { Name = "Premium", Max = 1 }]);
        Assert.False(bad.Ok);
        Assert.Empty(committer.Changes);

        var saved = harness.Actions.SaveWorkerTypes(3,
        [
            new PageWorkerTypeView { Name = "worker", Max = 2 },
            new PageWorkerTypeView { Name = "Opus", Max = 1, Premium = true, Model = "opus" },
        ]);

        Assert.True(saved.Ok);
        var change = Assert.Single(committer.Changes);
        Assert.StartsWith("scheduler: workers - types worker 2, Opus 1 of 3", change.Message, StringComparison.Ordinal);
        var text = Assert.Single(change.Writes).Value;
        Assert.Contains("\"pool\"", text, StringComparison.Ordinal);
        Assert.Contains("\"maxWorkers\": 3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"slots\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"workers\"", text, StringComparison.Ordinal);
    }

    // WT-9: resuming one agent clears only that agent's quota mark; the page's Resume (paused state)
    // clears them all and sets the scheduler running.
    [Fact]
    public void WorkerTypes_AgentResume_ClearsOnlyThatAgent()
    {
        using var harness = new PageActionHarness(project: "ptk-880b-agent");
        harness.Tick.Quota.Mark("claude", harness.Clock.Now);
        harness.Tick.Quota.Mark("copilot", harness.Clock.Now);

        Assert.False(harness.Actions.ResumeAgent("gemini").Ok);
        Assert.True(harness.Actions.ResumeAgent("claude").Ok);
        Assert.False(harness.Tick.Quota.IsOut("claude"));
        Assert.True(harness.Tick.Quota.IsOut("copilot"));

        harness.Tick.State = SchedulerState.Paused;
        harness.Actions.Resume();

        Assert.Equal(SchedulerState.Running, harness.Tick.State);
        Assert.False(harness.Tick.Quota.IsOut("copilot"));
    }

    // WT-P3, WT-9: the snapshot lists the types in config order with how many run of each, and an
    // agent out of quota with the time it ran out; a running worker's slot view names its type.
    [Fact]
    public void Page_WorkerTypes_SnapshotRowsInOrder_WithQuota()
    {
        using var harness = new PageActionHarness(project: "ptk-880b-snap");
        harness.Running(1);
        var agent = Scheduler.Core.Config.Agents.AgentOf(harness.Config, "").Name;   // what a type with no agent runs
        harness.Tick.Adopt(harness.Tick.Running[1] with { Type = "worker", Agent = agent });
        harness.Tick.Quota.Mark(agent, harness.Clock.Now);

        var snapshot = Scheduler.Host.PageActions.HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        var row = Assert.Single(snapshot.WorkerTypes);
        Assert.Equal("worker", row.Name);
        Assert.Equal(1, row.Running);
        Assert.Equal(2, row.Max);
        Assert.Equal(harness.Clock.Now, row.OutOfQuotaSince);
        Assert.Equal("worker", snapshot.Slots.Single(s => s.Slot == 1).Type);
        Assert.Equal("", snapshot.Slots.Single(s => s.Slot == 2).Type);
    }
}
