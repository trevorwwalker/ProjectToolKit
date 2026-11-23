using System.Net;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// TIME-10: Stop from the page asks one worker to stop with pageStopGraceMinutes (WT-P2: it no longer disables its
/// slot); Stop all asks every worker and drains the scheduler. test-plans/time-limits.md.
/// </summary>
public class PageStopTests
{
    [Fact]
    public async Task Time_PageStop_KeepsSlotEnabled_StopAllDrains()
    {
        using var harness = new PageActionHarness(project: "ptk-280b-stop");
        harness.Running(1);

        var stop = await harness.Post("/stop", "slot=1");
        Assert.Equal(HttpStatusCode.SeeOther, stop.StatusCode);

        var worker = harness.Tick.Running[1];
        Assert.Equal("stopped from the page", worker.StopWhy);
        Assert.Equal(5, worker.StopGraceMinutes);
        Assert.Equal(harness.Clock.Now, worker.StopAt);
        Assert.True(harness.Tick.Workers.Enabled.GetValueOrDefault(1, true));   // WT-P2: there is no slot to disable
        Assert.True(File.Exists(WorkerFiles.StopFlag(harness.LogDir, 1)), "the worker's stop flag was not written");

        // Stop all: both running workers get the page's grace and the scheduler drains (PAGE-12).
        harness.Running(2);
        var all = await harness.Post("/stopall");
        Assert.Equal(HttpStatusCode.SeeOther, all.StatusCode);
        Assert.Equal(SchedulerState.Draining, harness.Tick.State);
        foreach (var slot in new[] { 1, 2 })
        {
            var w = harness.Tick.Running[slot];
            Assert.Equal(5, w.StopGraceMinutes);
            Assert.NotNull(w.StopAt);
            Assert.Equal(slot == 1 ? "stopped from the page" : "stop all from the page", w.StopWhy);
        }

        Assert.Contains(harness.Log.Lines, line => line.Contains("stop all from the page"));
    }
}
