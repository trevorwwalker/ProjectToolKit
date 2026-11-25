// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Locks;

/// <summary>One new commit on main, as the audit (design <c>dynamic-locks.md</c>, DL-19) reads it from git.</summary>
public sealed record MainAuditCommit
{
    /// <summary>The commit.</summary>
    public required string Sha { get; init; }

    /// <summary>Its <c>Card:</c> trailer (the commit-msg hook's, DL-18), or empty.</summary>
    public string Card { get; init; } = "";

    /// <summary>The paths it changes.</summary>
    public IReadOnlyList<PushCheckPath> Paths { get; init; } = [];

    /// <summary>True for the scheduler's own commits (card moves, claims): never audited.</summary>
    public bool Own { get; init; }

    /// <summary>
    /// When it landed on main (when the scheduler saw main move to it), or null for now: the held set
    /// is read as it was then, its entries granted later left out.
    /// </summary>
    public DateTimeOffset? Landed { get; init; }
}
