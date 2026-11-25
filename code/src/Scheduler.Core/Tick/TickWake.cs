// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Tick;

/// <summary>
/// Why a tick runs (981-A). The poll, a click and a finished worker do the full tick; a move request,
/// a planner batch, a lock request and the health check's end do a light one when main has not moved and nothing
/// finished (see <see cref="SchedulerTick.Run(TickWake)"/>). A wake whose cause is not known is a poll.
/// </summary>
public enum TickWake
{
    /// <summary>The tick every pollSeconds, or a wake whose cause is not known: the full tick.</summary>
    Poll,

    /// <summary>An action on the page or tray (it may change the state, the pool or main): the full tick.</summary>
    Click,

    /// <summary>A worker's process ended: the full tick, so its slot is filled.</summary>
    WorkerExit,

    /// <summary>A move request was written into the moves folder.</summary>
    MoveRequest,

    /// <summary>A planner batch was written into the planner's outbox.</summary>
    PlannerBatch,

    /// <summary>The health check's process ended.</summary>
    HealthCheck,

    /// <summary>
    /// A lock request (<c>.lock</c>) was written into the moves folder (design <c>dynamic-locks.md</c>,
    /// section 3): a light tick, whose lock step answers it in seconds.
    /// </summary>
    LockRequest,
}
