// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.PageModel;

namespace Scheduler.Host.Locks;

/// <summary>
/// The lock events the page counts per day (DL-26): grants, waits, yields and refusals the request step
/// answered. Kept in memory for the last two days; the page line of a restarted scheduler starts from zero.
/// </summary>
public sealed class LockEventLog
{
    private readonly object _gate = new();
    private readonly List<(DateTimeOffset At, LockEventKind Kind)> _events = [];

    /// <summary>Records one event, and forgets those older than two days.</summary>
    public void Add(LockEventKind kind, DateTimeOffset at)
    {
        lock (_gate)
        {
            _events.Add((at, kind));
            _events.RemoveAll(e => e.At < at.AddDays(-2));
        }
    }

    /// <summary>A copy of the events recorded.</summary>
    public IReadOnlyList<(DateTimeOffset At, LockEventKind Kind)> Snapshot()
    {
        lock (_gate)
        {
            return [.. _events];
        }
    }
}
