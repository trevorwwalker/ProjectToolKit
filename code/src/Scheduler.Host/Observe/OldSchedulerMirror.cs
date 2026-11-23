using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.Processes;
using Scheduler.Host.State;

namespace Scheduler.Host.Observe;

/// <summary>
/// Before each observe tick, the old scheduler's state is copied in, read-only (roadmap item 1), so
/// the observe run decides from the same facts: its live workers are adopted as ADOPT-1 adopts them
/// from running.json (alive by pid and start time, TIME-13), its retry counts replace the run's own
/// (TIME-22), and its accepted categories (LANE-2) and enabled slots (WRK-3) are copied into the observe
/// run's own folder. A newly mirrored worker is logged as <c>mirror slot N: card (pid P)</c>.
/// </summary>
public sealed class OldSchedulerMirror(string oldLogDir, IStateFiles own, IWorkerProcesses processes, IClock clock, ISchedulerLog log)
{
    private static readonly StateFile[] Copied = [StateFile.CategoriesAccepted, StateFile.WorkersState];
    private readonly StateFiles _old = new(oldLogDir);

    /// <summary>Copy the old scheduler's state into <paramref name="tick"/> and the own folder.</summary>
    public void Sync(SchedulerTick tick)
    {
        foreach (var file in Copied)
        {
            if (_old.Read(file) is { } text)
            {
                own.Write(file, text);
            }
        }

        var retries = RetriesFile.Load(_old);
        tick.Retries.Clear();
        foreach (var (id, count) in retries)
        {
            tick.Retries[id] = count;
        }

        var adopter = new WorkerAdopter(_old, processes, clock, Quiet.Log);
        foreach (var entry in adopter.Adopt(tick.Workers.Pool).Entries)
        {
            var w = entry.Worker;
            if (tick.Running.TryGetValue(w.Slot, out var current) && current.Pid == w.Pid)
            {
                continue;
            }

            tick.Adopt(w);
            log.Write(ObserveLine.Format(clock.Now, $"mirror slot {w.Slot}: {w.Card} (pid {w.Pid})"));
        }
    }

    /// <summary>The adopter's own lines would repeat every tick; the mirror logs what is new.</summary>
    private sealed class Quiet : ISchedulerLog
    {
        public static Quiet Log { get; } = new();

        public void Write(string line)
        {
        }
    }
}
