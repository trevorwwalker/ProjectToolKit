// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>What starting a worker does to the environment it inherits (WRK-8).</summary>
public sealed record WorkerEnvironment
{
    /// <summary>Variables another agent sets but this one does not: removed before the worker starts.</summary>
    public IReadOnlyList<string> Removed { get; init; } = [];

    /// <summary>
    /// Variables to set, with every placeholder filled in. A variable that needed the model
    /// server's context length and got no answer is absent, which means leave it unset.
    /// </summary>
    public IReadOnlyDictionary<string, string> Set { get; init; } = new Dictionary<string, string>(Text.Comparer);
}
