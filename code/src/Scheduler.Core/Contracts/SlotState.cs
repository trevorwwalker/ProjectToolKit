namespace Scheduler.Core.Contracts;

/// <summary>A worker slot (A5): its definition, whether it is enabled, and what it runs.</summary>
public sealed record SlotState
{
    /// <summary>The slot number, from 1.</summary>
    public required int Slot { get; init; }

    /// <summary>Enabled; kept in workers-state.json, missing means enabled (WRK-3).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Its workers.json entry, or null when it has none.</summary>
    public WorkerDefinition? Worker { get; init; }

    /// <summary>The worker running in it, or null when idle.</summary>
    public RunningWorker? Running { get; init; }
}
