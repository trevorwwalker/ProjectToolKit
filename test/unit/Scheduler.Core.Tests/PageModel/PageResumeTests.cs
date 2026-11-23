using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// What the Control window's Pause/Resume button does (PAGE-9; the routes of schedule.ps1 L3854-3857
/// and the button of L3555). <c>/resume</c> acts on the paused states only, so in
/// <c>draining</c> - where the page still offers the button - it does nothing (spec C6).
/// Test plan: scheduler-next/test-plans/page.md.
/// </summary>
public class PageResumeTests
{
    // PAGE-9 · Spec C6: today's behaviour, kept until the developer decides (003-D review).
    [Fact]
    public void Page_ResumeInDraining()
    {
        // The button: Pause while running, Resume in every other state, draining included (L3555).
        Assert.False(PageResume.ShowsResumeButton(SchedulerState.Running));
        Assert.True(PageResume.ShowsResumeButton(SchedulerState.Draining));
        Assert.True(PageResume.ShowsResumeButton(SchedulerState.Paused));
        Assert.True(PageResume.ShowsResumeButton(SchedulerState.PausedRed));
        Assert.True(PageResume.ShowsResumeButton(SchedulerState.PausedQuota));

        // The press: /resume only acts on paused* (L3855). Draining is one-way - the state, the
        // retries and the tick are all left exactly as they were.
        Assert.True(PageResume.ActsOn(SchedulerState.Paused));
        Assert.True(PageResume.ActsOn(SchedulerState.PausedRed));
        Assert.True(PageResume.ActsOn(SchedulerState.PausedQuota));
        Assert.False(PageResume.ActsOn(SchedulerState.Draining));
        Assert.False(PageResume.ActsOn(SchedulerState.Running));
        foreach (var paused in new[] { SchedulerState.Paused, SchedulerState.PausedRed, SchedulerState.PausedQuota })
        {
            Assert.Equal(SchedulerState.Running, PageResume.Apply(paused));
        }

        Assert.Equal(SchedulerState.Draining, PageResume.Apply(SchedulerState.Draining));
        Assert.Equal(SchedulerState.Running, PageResume.Apply(SchedulerState.Running));
    }
}
