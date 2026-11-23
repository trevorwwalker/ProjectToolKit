namespace Scheduler.Core.Contracts;

/// <summary>The dispatching state (DISP-14, HLTH, TIME-10, TIME-15, PAGE-8, PAGE-12).</summary>
public enum SchedulerState
{
    /// <summary><c>running</c>: cards are dispatched.</summary>
    Running,

    /// <summary><c>paused</c>: paused by the developer or <c>-Paused</c>.</summary>
    Paused,

    /// <summary><c>paused-red</c>: main is red.</summary>
    PausedRed,

    /// <summary><c>paused-quota</c>: the agent account is out of quota.</summary>
    PausedQuota,

    /// <summary><c>draining</c>: no new cards; the loop ends when no worker runs.</summary>
    Draining,
}
