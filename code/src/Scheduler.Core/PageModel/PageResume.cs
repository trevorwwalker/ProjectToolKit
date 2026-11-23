using Scheduler.Core.Contracts;

namespace Scheduler.Core.PageModel;

/// <summary>
/// The Control window's Pause/Resume button and what <c>/resume</c> does (PAGE-9; schedule.ps1
/// L3555 and L3855). The button is Pause while the scheduler runs and Resume in every other state;
/// the route only acts on the three paused states, so pressing Resume while <c>draining</c> does
/// nothing at all - draining is one-way (spec C6).
/// </summary>
public static class PageResume
{
    /// <summary>
    /// Whether the Control window shows Resume rather than Pause (L3555). It shows Resume in
    /// <c>draining</c> even though the route ignores it there (spec C6).
    /// </summary>
    public static bool ShowsResumeButton(SchedulerState state) => state != SchedulerState.Running;

    /// <summary>
    /// Whether <c>/resume</c> does anything (L3855, <c>$state -like 'paused*'</c>): only the states
    /// a person or the health check or the quota paused. Not <c>running</c>, and not
    /// <c>draining</c> - which is why the state, the retries and the tick all stay as they were.
    /// </summary>
    public static bool ActsOn(SchedulerState state) =>
        state is SchedulerState.Paused or SchedulerState.PausedRed or SchedulerState.PausedQuota;

    /// <summary>The state after <c>/resume</c>: <c>running</c> where the route acts, unchanged
    /// everywhere else (spec C6).</summary>
    public static SchedulerState Apply(SchedulerState state) => ActsOn(state) ? SchedulerState.Running : state;

    /// <summary>
    /// The page's Resume with worker types (WT-9): the state as <see cref="Apply(SchedulerState)"/> has
    /// it, and every agent's out-of-quota mark cleared - Resume is also how an out-of-quota agent is
    /// tried again, whatever the state was.
    /// </summary>
    public static SchedulerState Apply(SchedulerState state, Dispatch.AgentQuota quota)
    {
        ArgumentNullException.ThrowIfNull(quota);
        quota.ClearAll();
        return Apply(state);
    }
}
