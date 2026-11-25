// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;

namespace Scheduler.Core.Pool;

/// <summary>
/// One project of the shared pool as the pool's choice sees it (MP-1, MP-4): its name, its weight
/// and its own dispatch plan, made as today by <see cref="DispatchPlanner.Plan"/> (MP-7).
/// </summary>
/// <param name="Name">The project's name in the top-level config.</param>
/// <param name="Weight">Its weight (MP-4), e.g. 70.</param>
/// <param name="Plan">Its candidates in the order the pool starts them (DISP-14).</param>
public sealed record PoolProject(string Name, int Weight, DispatchPlan Plan)
{
    /// <summary>
    /// The agents the project's scheduler.json defines (WT-C9); null = not restricted. A type whose
    /// non-empty agent is not in it does not fit this project's cards. "" (the <c>worker</c> section) always exists.
    /// </summary>
    public IReadOnlySet<string>? Agents { get; init; }

    /// <summary>1029-B: the project's workspaces (slot folders) from the pool file; null = the pool's <c>maxWorkers</c>.</summary>
    public int? Workspaces { get; init; }

    /// <summary>1029-B: the project's workers running now (adopted and starting ones included), each on one of its workspaces.</summary>
    public int Busy { get; init; }

    /// <summary>WT-C9: the project defines the type's agent.</summary>
    public bool DefinesAgent(DispatchType type) =>
        Agents is null || type.Agent.Length == 0 || Agents.Any(a => Text.Same(a, type.Agent));
}
