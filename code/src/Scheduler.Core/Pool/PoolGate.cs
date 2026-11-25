// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;

namespace Scheduler.Core.Pool;

/// <summary>
/// Who may offer a card in the shared pool (MP-9, spec B3 Q11): a project whose main is red, or is
/// otherwise not running, offers nothing, and the others run on; a type whose agent is out of quota
/// starts nothing, and types of other agents still do, in every project (WT-9).
/// </summary>
public static class PoolGate
{
    /// <summary>The projects that dispatch: those whose state is running. A project with no state yet counts as running.</summary>
    public static IReadOnlyList<PoolProject> Eligible(
        IEnumerable<PoolProject> projects,
        IReadOnlyDictionary<string, SchedulerState> states) =>
        [.. projects.Where(p => !states.TryGetValue(p.Name, out var s) || s == SchedulerState.Running)];

    /// <summary>WT-9: the type's agent is out of quota (names match without case, G-1).</summary>
    public static bool AgentPaused(DispatchType type, AgentQuota quota) => quota.IsOut(type.Agent);
}
