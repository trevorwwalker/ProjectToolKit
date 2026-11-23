using Scheduler.Core.Tick;

namespace Scheduler.Host.Program;

/// <summary>
/// 982-B: why the loop woke. Several wakes before a tick merge into one kind: a full kind (poll, click,
/// worker exit) wins over a light one (move request, planner batch, health check), two different full
/// kinds are the poll, and two light ones stay light (the tick treats every light kind alike).
/// </summary>
public static class TickWakes
{
    /// <summary>True for the kinds that always run the full tick (981-A).</summary>
    public static bool IsFull(TickWake kind) => kind is TickWake.Poll or TickWake.Click or TickWake.WorkerExit;

    /// <summary>The one kind for <paramref name="pending"/> (none yet when null) and a new <paramref name="kind"/>.</summary>
    public static TickWake Merge(TickWake? pending, TickWake kind)
    {
        if (pending is not { } was || was == kind)
        {
            return kind;
        }

        return (IsFull(was), IsFull(kind)) switch
        {
            (true, true) => TickWake.Poll,
            (true, false) => was,
            (false, true) => kind,
            _ => was,
        };
    }
}
