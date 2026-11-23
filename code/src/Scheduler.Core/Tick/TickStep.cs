namespace Scheduler.Core.Tick;

/// <summary>The steps of a tick, in the order of spec A3 (<c>Tick</c> L2795).</summary>
public enum TickStep
{
    /// <summary>Sample the host's cpu and memory.</summary>
    SampleHost,

    /// <summary>Sample the model servers' metrics.</summary>
    SampleServers,

    /// <summary>Reap the health check.</summary>
    ReapHealthCheck,

    /// <summary>Reap finished workers and watch live ones.</summary>
    ReapWorkers,

    /// <summary>Index the worker logs.</summary>
    IndexLogs,

    /// <summary>Read the cards from main.</summary>
    ReadCards,

    /// <summary>Remove stale queue/ copies.</summary>
    FixDuplicates,

    /// <summary>The planner's outbox step.</summary>
    PlannerStep,

    /// <summary>
    /// Dynamic locks only (design <c>dynamic-locks.md</c>): the lock step, after the move step (in the
    /// planner step) so releases from finishes come first, and before the plan and dispatch.
    /// </summary>
    Locks,

    /// <summary>Read categories.json.</summary>
    ReadCategories,

    /// <summary>Read workers.json.</summary>
    ReadWorkers,

    /// <summary>Read models.json.</summary>
    ReadModels,

    /// <summary>Read the requests.</summary>
    ReadRequests,

    /// <summary>Plan: candidates and notes.</summary>
    Plan,

    /// <summary>Start the health check if one is wanted.</summary>
    StartHealthCheck,

    /// <summary>Start workers on free slots.</summary>
    Dispatch,

    /// <summary>Save the retry counts.</summary>
    SaveRetries,

    /// <summary>Write the status files.</summary>
    WriteStatus,
}
