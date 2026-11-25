// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Dispatch;

/// <summary>
/// Which agents are out of quota, and since when (WT-9). An out-of-quota result marks the run's
/// agent; the types using it do not start, in every project, and other agents' types run on. It
/// replaces the project-wide <c>paused-quota</c> state. Agent names match without case (G-1).
/// </summary>
public sealed class AgentQuota
{
    private readonly Dictionary<string, DateTimeOffset> _since = new(Text.Comparer);

    /// <summary>The agents out of quota.</summary>
    public IReadOnlyCollection<string> Agents => _since.Keys;

    /// <summary>Mark <paramref name="agent"/> out of quota at <paramref name="now"/>; a mark already there keeps its first time.</summary>
    public void Mark(string agent, DateTimeOffset now) => _since.TryAdd(agent ?? "", now);

    /// <summary>The agent is out of quota.</summary>
    public bool IsOut(string agent) => _since.ContainsKey(agent ?? "");

    /// <summary>Since when the agent is out of quota, or null.</summary>
    public DateTimeOffset? Since(string agent) => _since.TryGetValue(agent ?? "", out var t) ? t : null;

    /// <summary>The Worker types panel's Resume for one agent: clears only that agent's mark (WT-9).</summary>
    public bool Clear(string agent) => _since.Remove(agent ?? "");

    /// <summary>The page's Resume: clears every agent's mark (WT-9).</summary>
    public void ClearAll() => _since.Clear();
}
