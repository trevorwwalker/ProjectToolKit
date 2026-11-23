using Scheduler.Core.Tick;

namespace Scheduler.Host.Observe;

/// <summary>One observe tick (roadmap item 1): mirror the old scheduler, run the tick, record its decisions.</summary>
public sealed class ObserveTicker(SchedulerTick tick, OldSchedulerMirror mirror, ObserveRecorder recorder)
{
    /// <summary>Run one observe tick.</summary>
    public void Run()
    {
        mirror.Sync(tick);
        tick.Run();
        recorder.Record(tick);
    }
}
