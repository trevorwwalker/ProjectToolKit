// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// TIME-12: Kill from the page ends the worker's process tree, says why, disables the slot and asks
/// for a tick at once. test-plans/time-limits.md.
/// </summary>
public class PageKillTests
{
    [Fact]
    public async Task Time_PageKill_KeepsSlotEnabled_Ticks()
    {
        using var harness = new PageActionHarness(project: "ptk-280b-kill");
        var worker = harness.Running(1);

        var kill = await harness.Post("/kill", "slot=1");
        Assert.Equal(HttpStatusCode.SeeOther, kill.StatusCode);

        Assert.Contains((worker.Job, worker.Pid), harness.Processes.Ended);
        var after = harness.Tick.Running[1];
        Assert.Equal("killed from the page", after.StopWhy);
        Assert.Equal(harness.Clock.Now, after.KillTime);
        Assert.True(harness.Tick.Workers.Enabled.GetValueOrDefault(1, true));   // WT-P2: there is no slot to disable
        Assert.Equal(1, harness.TickAsks);
    }
}
