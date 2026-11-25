// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// One worker of workers.json: <c>workers.&lt;n&gt;</c> (WRK-1). Kept for the migration only (WT-C10):
/// once a file has a <c>pool</c> its <c>slots</c> and <c>workers</c> are ignored and a
/// <see cref="WorkerType"/> describes what runs instead.
/// </summary>
public sealed record WorkerDefinition
{
    /// <summary>The slot number n.</summary>
    public required int Slot { get; init; }

    /// <summary>The worker's name.</summary>
    public string Name { get; init; } = "";

    /// <summary>The agent name, or "" for the default agent (CFG-11).</summary>
    public string Agent { get; init; } = "";

    /// <summary>The model server endpoint, or "" for none of its own (WRK-4).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>The model, or "" for the agent's or config's (WRK-4).</summary>
    public string Model { get; init; } = "";

    /// <summary>The metrics URL, or "" (WRK-4).</summary>
    public string MetricsUrl { get; init; } = "";

    /// <summary>Category letters it may take, upper-cased; "" means any category (WRK-1, DISP-17).</summary>
    public string Categories { get; init; } = "";
}
