// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>One entry a running card holds beyond its claim (DL-24): the path and how long it has held it.</summary>
/// <param name="Path">The entry, as asked (a file, a folder ending <c>/</c>, or a glob).</param>
/// <param name="Minutes">Whole minutes since it was granted (or carried over from a remembered set).</param>
public sealed record PageHeldEntry(string Path, int Minutes);

/// <summary>
/// A running card's locks on the pool page (design <c>dynamic-locks.md</c>, DL-24, DL-25): its claim on one
/// line, what else it holds on another, and its open wait. Absent when locks are off.
/// </summary>
public sealed record PageHolds
{
    /// <summary>The initial claim's entries.</summary>
    public IReadOnlyList<string> Claim { get; init; } = [];

    /// <summary>How long it has held its claim, in whole minutes.</summary>
    public int ClaimMinutes { get; init; }

    /// <summary>Remembered and granted entries ("also holds: ..."), oldest first.</summary>
    public IReadOnlyList<PageHeldEntry> Also { get; init; } = [];

    /// <summary>DL-25: "waits for lock on `path` held by `card` since 10:22, gives up at 10:42", or "".</summary>
    public string Wait { get; init; } = "";
}

/// <summary>
/// DL-26: one day's lock events for a project (SC-6), in the Projects window, so the developer can see
/// whether yields are rare and whether planner scopes could be narrower.
/// </summary>
public sealed record PageLockCounts
{
    /// <summary>The day counted.</summary>
    public DateOnly Day { get; init; }

    /// <summary>Requests granted.</summary>
    public int Grants { get; init; }

    /// <summary>Requests answered waiting.</summary>
    public int Waits { get; init; }

    /// <summary>Requests answered yield.</summary>
    public int Yields { get; init; }

    /// <summary>Requests refused (too broad, unknown card).</summary>
    public int Refusals { get; init; }

    /// <summary>Pushes the pre-push hook refused (DL-18).</summary>
    public int PushesRefused { get; init; }

    /// <summary>Strays the audit of main found (DL-19).</summary>
    public int Strays { get; init; }

    /// <summary>True when nothing happened that day.</summary>
    public bool None => Grants + Waits + Yields + Refusals + PushesRefused + Strays == 0;
}
