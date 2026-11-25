// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Planner;

namespace Scheduler.Core.Tests.Planner;

/// <summary>PLAN-1 and PLAN-2: when the outbox exists, and which folders are batches. Test plan: planner.md.</summary>
public class PlannerSwitchTests
{
    // PLAN-1
    [Fact]
    public void Planner_Enabled_OnlyWithFetchRepoAndOutbox_OffByDefault()
    {
        Assert.Null(PlannerSettings.Create(null, null));
        Assert.Null(PlannerSettings.Create("C:\\Projects\\X", ""));
        Assert.Null(PlannerSettings.Create("", "docs\\jobs\\.planner-outbox"));
        Assert.Null(PlannerSettings.Create(null, "docs\\jobs\\.planner-outbox"));

        var both = PlannerSettings.Create("C:\\Projects\\X", "docs\\jobs\\.planner-outbox");
        Assert.NotNull(both);
        Assert.Equal("C:\\Projects\\X", both.FetchRepo);
        Assert.Equal(Path.Combine("C:\\Projects\\X", "docs\\jobs\\.planner-outbox"), both.Outbox);

        // Switched off until toggled: no state file, an empty or broken one, or push false.
        Assert.False(PlannerSwitch.Parse(null));
        Assert.False(PlannerSwitch.Parse(""));
        Assert.False(PlannerSwitch.Parse("not json"));
        Assert.False(PlannerSwitch.Parse("{}"));
        Assert.False(PlannerSwitch.Parse("{\"push\": false}"));

        // The toggle persists in planner-state.json across a restart.
        Assert.True(PlannerSwitch.Parse(PlannerSwitch.Serialize(true)));
        Assert.False(PlannerSwitch.Parse(PlannerSwitch.Serialize(false)));
        Assert.True(PlannerSwitch.Parse("{\n    \"push\":  true\n}"));
    }

    // PLAN-2
    [Fact]
    public void Planner_Batches_CompleteOnlyInNameOrder()
    {
        var folders = new[]
        {
            new OutboxFolder("002-b", true),
            new OutboxFolder("001-a", true),
            new OutboxFolder("003-c", false),
            new OutboxFolder("sent", true),
            new OutboxFolder("failed", true),
        };

        Assert.Equal(["001-a", "002-b"], PlannerBatches.Complete(folders));
        Assert.Empty(PlannerBatches.Complete([]));
    }
}
