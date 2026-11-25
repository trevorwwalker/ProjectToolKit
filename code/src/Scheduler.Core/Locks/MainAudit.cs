// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Locks;

/// <summary>
/// The audit of main (design <c>dynamic-locks.md</c>, DL-19), pure: the new commits that are not the
/// scheduler's own, checked against the held set each one's <c>Card:</c> trailer names, as it was when the
/// commit landed. It never reverts anything. Host runs it on every move of main, before the move step
/// applies finishes, so a stray's To: done can still be turned into To: review (<c>CardMoves.Stray</c>).
/// </summary>
public static class MainAudit
{
    /// <summary>DL-19 on <paramref name="commits"/> (oldest first), against <paramref name="table"/>.</summary>
    /// <param name="table">The repository's lock table.</param>
    /// <param name="commits">The commits since the table's last audited one, oldest first.</param>
    /// <param name="addOnly">categories.json's shared add-only paths (DL-15 a).</param>
    public static MainAuditResult Check(LockTable table, IReadOnlyList<MainAuditCommit> commits, IReadOnlyList<string> addOnly)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(addOnly);
        var strays = new List<MainAuditStray>();
        foreach (var commit in commits.Where(c => !c.Own))
        {
            if (commit.Card.Trim().Length == 0)
            {
                if (WorkersRan(table, commit.Landed))
                {
                    strays.Add(new MainAuditStray(commit.Sha, "", "", "commit has no Card trailer while workers ran"));
                }

                continue;
            }

            var held = HeldWhen(table.Find(commit.Card.Trim()), commit.Landed);
            foreach (var changed in commit.Paths)
            {
                var path = PushCheck.Normal(changed.Path);
                var kind = PushCheck.Kind(commit.Card.Trim(), path, changed.New, addOnly.Concat(held?.AddOnly ?? []));
                var stray = kind == PushPathKind.Jobs || (kind == PushPathKind.Locked && (held is null || !held.Covers(path)));
                if (stray)
                {
                    strays.Add(new MainAuditStray(commit.Sha, commit.Card.Trim(), path, $"changed {path} without its lock"));
                }
            }
        }

        var audited = commits.Count > 0 ? commits[^1].Sha : table.AuditedCommit;
        return new MainAuditResult { Strays = strays, Table = table with { AuditedCommit = audited } };
    }

    // The card's held set when the commit landed: entries granted after it are left out. A card that
    // does not hold its set holds nothing.
    private static CardLocks? HeldWhen(CardLocks? row, DateTimeOffset? landed)
    {
        if (row is not { Holds: true })
        {
            return null;
        }

        return landed is { } at ? row with { Held = [.. row.Held.Where(e => e.Granted <= at)] } : row;
    }

    // Workers ran when the commit landed: a card held its set then.
    private static bool WorkersRan(LockTable table, DateTimeOffset? landed) =>
        table.Holding.Any(c => landed is not { } at || (c.Held.Count > 0 && c.Held.Min(e => e.Granted) <= at));
}
