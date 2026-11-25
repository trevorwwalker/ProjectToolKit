// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Categories;

/// <summary>What a slot's worker really is once agent and config are applied (WRK-4, <c>Effective-Worker</c> L2695).</summary>
public sealed record EffectiveWorker
{
    /// <summary>The worker's name, or "".</summary>
    public string Name { get; init; } = "";

    /// <summary>The model server endpoint; "" for none (the worker's agent runs its own service).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>The model.</summary>
    public string Model { get; init; } = "";

    /// <summary>The metrics URL; "" for none.</summary>
    public string MetricsUrl { get; init; } = "";

    /// <summary>The category letters it may take, upper-cased; "" for any.</summary>
    public string Categories { get; init; } = "";

    /// <summary>The agent's name.</summary>
    public string Agent { get; init; } = "";

    /// <summary>The agent's command.</summary>
    public string Cli { get; init; } = "";
}
