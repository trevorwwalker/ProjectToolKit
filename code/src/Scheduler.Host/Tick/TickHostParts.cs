using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tick;

/// <summary>The collaborators <see cref="TickHost"/> is made of, so a test can give it a different clock or process port.</summary>
public sealed class TickHostParts
{
    /// <summary>The configuration.</summary>
    public required SchedulerConfig Config { get; init; }

    /// <summary>The clock.</summary>
    public required IClock Clock { get; init; }

    /// <summary>The scheduler log.</summary>
    public required ISchedulerLog Log { get; init; }

    /// <summary>The main branch (040-B).</summary>
    public required IMainRepository Main { get; init; }

    /// <summary>Reads the cards from main.</summary>
    public required CardReader Reader { get; init; }

    /// <summary>Commits changes to main (duplicate removal).</summary>
    public required IJobsCommitter Committer { get; init; }

    /// <summary>The slots' worktrees.</summary>
    public required SlotWorktrees Worktrees { get; init; }

    /// <summary>Clears the slot files and starts the process.</summary>
    public required SlotWorkerStarter Starter { get; init; }

    /// <summary>The bookkeeping after a worker.</summary>
    public required WorkerFinisher Finisher { get; init; }

    /// <summary>Catches a run that moved its card without updating it (395-B); none when not wired.</summary>
    public FinishedCardCheck? FinishedCards { get; init; }

    /// <summary>Worker processes.</summary>
    public required IWorkerProcesses Processes { get; init; }

    /// <summary>The state files (running.json, status.txt, retries).</summary>
    public required IStateFiles Files { get; init; }

    /// <summary>Is the model server up (TIME-16).</summary>
    public required IServerProbe Probe { get; init; }

    /// <summary>Threads the machine has, for WRK-7.</summary>
    public int Cores { get; init; } = Environment.ProcessorCount;

    /// <summary>Called with the pid of each worker started, to watch it exit (TIME-21).</summary>
    public Action<int>? Started { get; init; }

    /// <summary>Called with the pid of each worker finished.</summary>
    public Action<int>? Ended { get; init; }

    /// <summary>Called when a planner batch was dealt with and a tick is wanted at once (PLAN-2).</summary>
    public Action? TickNow { get; init; }
}
