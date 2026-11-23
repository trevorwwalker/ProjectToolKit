using Scheduler.Core.Locks;

namespace Scheduler.Core.Tick;

/// <summary>
/// Dynamic locks (design <c>dynamic-locks.md</c>, DL-1, DL-20): a tick host that keeps the repository's
/// lock table. With <see cref="SchedulerTick.Locks"/> on, the tick records each card's held set in it
/// (starting at dispatch, held when its claim lands, dropped when the claim fails), runs the host's lock
/// step after the move step and before the plan, and gates dispatch on the held sets it reads back. A
/// host without it, or locks off, dispatches on declared scopes as before.
/// </summary>
public interface ILockingHost
{
    /// <summary>The repository's lock table as it is now (an empty one when it has none, DL-21).</summary>
    LockTable ReadLocks();

    /// <summary>Writes the table (tmp then rename, DL-20); only the scheduler writes it.</summary>
    void SaveLocks(LockTable table);

    /// <summary>
    /// The lock step: answer the <c>.lock</c> requests in the moves folder and re-check open waits
    /// (DL-5, DL-12, DL-13), writing the table then each reply.
    /// </summary>
    void LockStep();
}
