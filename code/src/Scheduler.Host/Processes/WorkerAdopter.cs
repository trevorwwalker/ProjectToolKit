// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Host.Processes;

/// <summary>What start-up adopted: the workers, each with its type (WT-W5).</summary>
public sealed record AdoptedWorkers
{
    public required IReadOnlyList<RunningEntry> Entries { get; init; }
}

/// <summary>
/// ADOPT-1 to ADOPT-4 at start-up: a worker in running.json is adopted when its process is still the
/// same cmd process. WT-W5: it keeps its folder number even above maxWorkers (the count is not raised;
/// WT-1 and WT-2 count it), and an entry written before types gets the type of its slot, or none.
/// </summary>
public sealed class WorkerAdopter(IStateFiles files, IWorkerProcesses processes, IClock clock, ISchedulerLog log)
{
    private const double SameProcessMinutes = 2;

    public AdoptedWorkers Adopt(WorkerPool pool)
    {
        var adopted = new List<RunningEntry>();
        foreach (var entry in RunningJson.Read(files.Read(StateFile.Running)))
        {
            var w = entry.Worker;
            if (processes.CmdStartTime(w.Pid) is not { } procStart)
            {
                continue; // not running, or not cmd
            }
            if (Math.Abs((procStart - w.Start).TotalMinutes) > SameProcessMinutes)
            {
                log.Write($"not adopting pid {w.Pid} for {w.Card}: it is a different process now");
                continue;
            }
            var size = processes.LogSize(w.RawLog);
            var typed = Adoption.AdoptTyped(w, pool, clock.Now, size ?? 0, size is null ? null : processes.LogLastWrite(w.RawLog));
            adopted.Add(entry with { Worker = typed });
            log.Write($"adopted slot {w.Slot}, still running {w.Card} (pid {w.Pid})");
        }
        return new AdoptedWorkers { Entries = adopted };
    }
}
