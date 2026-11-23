using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// Taking over a worker that was still running when the scheduler stopped (ADOPT-2,
/// <c>Adopt-Running</c> L2967). Whether an entry may be adopted at all - a live <c>cmd</c> process
/// whose start time still matches the recorded one - is the host's to decide; what happens to an
/// entry it has accepted is here.
/// </summary>
public static class Adoption
{
    /// <summary>
    /// The worker as the restarted scheduler runs it, and the slot count after it. A slot above the
    /// count raises the count and starts enabled (L2985). The stop deadline carries over from before
    /// the restart, and quiet time counts from the log's last write - which the caller reads and
    /// passes in, as Core does not touch the disk (L2987-2993).
    /// </summary>
    /// <param name="logSize">The log's size, or -1 when there is no log.</param>
    /// <param name="logLastWrite">When the log was last written, or null when there is no log; quiet
    /// time then starts at the restart.</param>
    public static AdoptionResult Adopt(
        RunningWorker entry,
        int slots,
        DateTimeOffset now,
        long logSize,
        DateTimeOffset? logLastWrite)
    {
        var worker = entry with
        {
            LastSize = logLastWrite is null ? -1 : logSize,
            LastProgress = logLastWrite ?? now,
        };

        var newlyEnabled = new List<int>();
        if (worker.Slot > slots)
        {
            slots = worker.Slot;
            newlyEnabled.Add(worker.Slot);
        }

        return new AdoptionResult { Worker = worker, Slots = slots, NewlyEnabled = newlyEnabled };
    }

    /// <summary>
    /// Adoption with types (WT-W5): as <see cref="Adopt"/>, but the worker keeps its folder number
    /// even above <c>maxWorkers</c> - the count is not raised; WT-1 and WT-2 count it instead. An
    /// entry with no <c>type</c> gets the type its slot was migrated from (WT-C10.5), or, when there
    /// is none, stays untyped and counts toward the total only.
    /// </summary>
    public static RunningWorker AdoptTyped(
        RunningWorker entry,
        WorkerPool pool,
        DateTimeOffset now,
        long logSize,
        DateTimeOffset? logLastWrite)
    {
        var worker = Adopt(entry, pool.MaxWorkers, now, logSize, logLastWrite).Worker;
        return worker.Type.Length > 0 ? worker : worker with { Type = TypeOfSlot(pool, worker.Slot) };
    }

    /// <summary>
    /// Dynamic locks at start (design <c>dynamic-locks.md</c>, DL-21): the lock table read before adoption,
    /// with the adopted workers' cards keeping their held sets and open requests (the request's deadline
    /// carries over, as the stop deadline does) in the slot they were adopted into. Every other card is
    /// released as a run that ended (DL-8 d): its granted entries become its remembered set; a card that
    /// was starting (its claim not landed, and background claims are not kept across a restart) is dropped.
    /// </summary>
    public static Locks.LockTable KeepLocks(Locks.LockTable table, IEnumerable<RunningWorker> adopted)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(adopted);
        var kept = new Dictionary<string, RunningWorker>(Text.Comparer);
        foreach (var w in adopted)
        {
            kept.TryAdd(w.Card, w);
        }

        foreach (var row in table.Cards.ToList())
        {
            if (row.Holds && kept.TryGetValue(row.Card, out var w))
            {
                table = table.With(row with { Slot = w.Slot });
            }
            else if (row.State != Locks.CardLockState.Released)
            {
                table = table.Release(row.Card, Locks.LockRelease.Ended);
            }
        }

        return table;
    }

    /// <summary>The name of the type migrated from <paramref name="slot"/>, or "" (WT-W5).</summary>
    public static string TypeOfSlot(WorkerPool pool, int slot) =>
        pool.Types.FirstOrDefault(t => t.Slots.Contains(slot))?.Name ?? "";

    /// <summary>
    /// When a worker that was asked to stop must be killed: the deadline it had, measured from the
    /// request rather than from now (TIME-5, TIME-9). Null when it was never asked to stop.
    /// </summary>
    public static DateTimeOffset? StopDeadline(RunningWorker worker) =>
        worker.StopAt is null ? null : worker.StopAt.Value.AddMinutes(worker.StopGraceMinutes);
}
