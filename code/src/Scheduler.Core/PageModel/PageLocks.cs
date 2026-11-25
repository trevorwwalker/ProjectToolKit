// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.PageModel;

/// <summary>A lock event counted per day on the pool page (design <c>dynamic-locks.md</c>, DL-26).</summary>
public enum LockEventKind
{
    /// <summary>A request granted.</summary>
    Grant,
    /// <summary>A request answered waiting.</summary>
    Wait,
    /// <summary>A request answered yield.</summary>
    Yield,
    /// <summary>A request refused.</summary>
    Refusal,
    /// <summary>A push the pre-push hook refused.</summary>
    PushRefused,
    /// <summary>A stray the audit of main found.</summary>
    Stray,
}

/// <summary>
/// The pool page's lock lines, from the lock table (design <c>dynamic-locks.md</c>, DL-24 to DL-26). Pure:
/// the table and the time come in, the page's records go out; the renderer (category C) draws them.
/// </summary>
public static class PageLocks
{
    /// <summary>DL-24, DL-25: what a running card holds, and its open wait; null when it holds nothing.</summary>
    public static PageHolds? Holds(LockTable table, string card, DateTimeOffset now, IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Find(card) is not { Holds: true } row)
        {
            return null;
        }

        var claim = row.Held.Where(e => e.Kind == LockKind.Claim).ToList();
        return new PageHolds
        {
            Claim = [.. claim.Select(e => e.Path)],
            ClaimMinutes = claim.Count == 0 ? 0 : Minutes(claim.Min(e => e.Granted), now),
            Also = [.. row.Held.Where(e => e.Kind != LockKind.Claim).OrderBy(e => e.Granted).Select(e => new PageHeldEntry(e.Path, Minutes(e.Granted, now)))],
            Wait = WaitText(table, row, addOnly),
        };
    }

    /// <summary>DL-25: "waits for lock on `path` held by `card` since 10:22, gives up at 10:42", or "" with no open wait.</summary>
    public static string WaitText(LockTable table, CardLocks row, IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(row);
        if (row.Open is not { } open)
        {
            return "";
        }

        var (path, holder) = Holder(table, row.Card, open.Paths, addOnly);
        var held = holder.Length > 0 ? $" held by `{holder}`" : "";
        return $"waits for lock on `{path}`{held} since {Clock(open.Asked)}, gives up at {Clock(open.Deadline)}";
    }

    /// <summary>
    /// DL-25: a yielded (released) card's Queue note: "yielded: waits for `path` held by `card`" while an
    /// entry of its remembered set is held by a running card; "" otherwise.
    /// </summary>
    public static string YieldedNote(LockTable table, string card, IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Find(card) is not { State: CardLockState.Released } row || row.Remembered.Count == 0)
        {
            return "";
        }

        var (path, holder) = Holder(table, row.Card, row.Remembered, addOnly);
        return holder.Length == 0 ? "" : $"yielded: waits for `{path}` held by `{holder}`";
    }

    /// <summary>DL-26: the events of <paramref name="day"/> counted by kind.</summary>
    public static PageLockCounts Counts(IEnumerable<(DateTimeOffset At, LockEventKind Kind)> events, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(events);
        var of = events.Where(e => DateOnly.FromDateTime(e.At.DateTime) == day).GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        return new PageLockCounts
        {
            Day = day,
            Grants = of.GetValueOrDefault(LockEventKind.Grant),
            Waits = of.GetValueOrDefault(LockEventKind.Wait),
            Yields = of.GetValueOrDefault(LockEventKind.Yield),
            Refusals = of.GetValueOrDefault(LockEventKind.Refusal),
            PushesRefused = of.GetValueOrDefault(LockEventKind.PushRefused),
            Strays = of.GetValueOrDefault(LockEventKind.Stray),
        };
    }

    /// <summary>The first of <paramref name="paths"/> another running card holds, and that card; the first path and "" when none does.</summary>
    private static (string Path, string Holder) Holder(LockTable table, string card, IReadOnlyList<string> paths, IReadOnlyList<string>? addOnly)
    {
        foreach (var path in paths)
        {
            var holder = table.Holding.FirstOrDefault(h => !Contracts.Text.Same(h.Card, card)
                && ScopeOverlap.Shared(Scope.Of(path), h.HeldScope, addOnly) is not null);
            if (holder is not null)
            {
                return (path, holder.Card);
            }
        }

        return (paths.Count > 0 ? paths[0] : "", "");
    }

    private static int Minutes(DateTimeOffset since, DateTimeOffset now) => Math.Max(0, (int)(now - since).TotalMinutes);

    private static string Clock(DateTimeOffset at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);
}
