using Scheduler.Core.Contracts;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Locks;

/// <summary>Why a card's held set is released (DL-8).</summary>
public enum LockRelease
{
    /// <summary>(a) Its finish (To: done or review) is on main: the card and its remembered set leave the table.</summary>
    Finished,

    /// <summary>(b) Its park (To: active) or send-back (To: queue) is applied.</summary>
    Parked,

    /// <summary>(c) It yielded (DL-12, DL-13).</summary>
    Yielded,

    /// <summary>(d) Its run ended any other way: failed, killed, timed out, stopped, or a claim lost on restart.</summary>
    Ended,
}

/// <summary>
/// The lock table of one repository (design <c>dynamic-locks.md</c>, DL-20, DL-22): every running
/// card's held set, and the age and remembered set of cards that ran. Pure: each operation returns a
/// new table (DL-1, DL-5, DL-8, DL-10); Host writes it to disk (<see cref="LockTableText"/>).
/// </summary>
public sealed record LockTable
{
    /// <summary>The repository the table belongs to (DL-22).</summary>
    public string Repo { get; init; } = "";

    /// <summary>The cards, by name.</summary>
    public IReadOnlyList<CardLocks> Cards { get; init; } = [];

    /// <summary>The last main commit the audit (DL-19) has read, or empty.</summary>
    public string AuditedCommit { get; init; } = "";

    /// <summary>A table with no cards.</summary>
    public static LockTable Empty(string repo = "") => new() { Repo = repo };

    /// <summary>The cards that hold their sets (starting or running).</summary>
    public IEnumerable<CardLocks> Holding => Cards.Where(c => c.Holds);

    /// <summary>The card's row, or null.</summary>
    public CardLocks? Find(string card) => Cards.FirstOrDefault(c => Text.Same(c.Card, card));

    /// <summary>The table with <paramref name="row"/> in place of the card's row (or added).</summary>
    public LockTable With(CardLocks row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return this with { Cards = [.. Cards.Where(c => !Text.Same(c.Card, row.Card)).Append(row).OrderBy(c => c.Card, StringComparer.Ordinal)] };
    }

    /// <summary>The table without the card's row.</summary>
    public LockTable Without(string card) => this with { Cards = [.. Cards.Where(c => !Text.Same(c.Card, card))] };

    /// <summary>
    /// 1018-D: dispatch has posted the card's claim, which lands in the background (995-B). Until it
    /// does, the card is in the table as <see cref="CardLockState.Starting"/>: a slot, no held set, and
    /// only the age and remembered set an earlier run left.
    /// </summary>
    public LockTable Starting(string card, string project = "", int? slot = null)
    {
        var old = Find(card);
        if (old is { Holds: true })
        {
            return this;
        }

        return With(new CardLocks
        {
            Card = old?.Card ?? card,
            Project = project,
            Slot = slot,
            Age = old?.Age,
            State = CardLockState.Starting,
            Remembered = old?.Remembered ?? [],
        });
    }

    /// <summary>
    /// 1018-D: the card's claim failed (<c>ClaimState.Failed</c>): the starting entry is dropped, and
    /// the card is as an earlier run left it, or gone.
    /// </summary>
    public LockTable ClaimFailed(string card)
    {
        if (Find(card) is not { State: CardLockState.Starting } c)
        {
            return this;
        }

        return c.Age is null && c.Remembered.Count == 0
            ? Without(card)
            : With(c with { State = CardLockState.Released, Slot = null });
    }

    /// <summary>
    /// DL-1: the card's claim has landed (<c>ClaimState.Landed</c>, 1018-D): it holds its claim plus its
    /// remembered set. Its age is the one already in the table, if any (DL-11), else <paramref name="age"/>.
    /// </summary>
    public LockTable Start(string card, CardAge age, Scope claim, DateTimeOffset now, string project = "", int? slot = null)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var old = Find(card);
        var held = new List<LockEntry>();
        foreach (var p in claim.Paths.Distinct(StringComparer.Ordinal))
        {
            held.Add(new LockEntry(p, LockKind.Claim, now));
        }

        var carried = old is null ? [] : old.Holds ? old.Held.Where(e => e.Kind != LockKind.Claim).Select(e => e.Path) : old.Remembered;
        foreach (var p in carried.Distinct(StringComparer.Ordinal).Where(p => !held.Any(e => e.Path == p)))
        {
            held.Add(new LockEntry(p, LockKind.Remembered, now));
        }

        return With(new CardLocks
        {
            Card = old?.Card ?? card,
            Project = project,
            Slot = slot,
            Age = old?.Age ?? age,
            State = CardLockState.Running,
            Held = held,
            Except = claim.Except,
            AddOnly = claim.AddOnly,
        });
    }

    /// <summary>DL-5: the card now also holds <paramref name="paths"/>; its open request is closed.</summary>
    public LockTable Grant(string card, IEnumerable<string> paths, DateTimeOffset now)
    {
        if (Find(card) is not { Holds: true } c)
        {
            return this;
        }

        var held = c.Held.ToList();
        foreach (var p in paths.Distinct(StringComparer.Ordinal).Where(p => !held.Any(e => e.Path == p)))
        {
            held.Add(new LockEntry(p, LockKind.Granted, now));
        }

        return With(c with { Held = held, Open = null });
    }

    /// <summary>
    /// DL-8, DL-10: the card's whole held set is released at once. Finished, it leaves the table;
    /// otherwise its remembered set becomes every entry it held beyond its claim, plus what it asked
    /// for when it yielded, plus <paramref name="asked"/>.
    /// </summary>
    public LockTable Release(string card, LockRelease why, IEnumerable<string>? asked = null)
    {
        if (Find(card) is not { } c)
        {
            return this;
        }

        if (why == LockRelease.Finished)
        {
            return Without(card);
        }

        if (c.State == CardLockState.Starting)
        {
            return ClaimFailed(card);
        }

        if (!c.Holds)
        {
            return asked is null ? this : With(c with { Remembered = [.. c.Remembered.Concat(asked).Distinct(StringComparer.Ordinal)] });
        }

        var remembered = c.Held.Where(e => e.Kind != LockKind.Claim).Select(e => e.Path)
            .Concat(c.Remembered)
            .Concat(asked ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return With(c with
        {
            State = CardLockState.Released,
            Held = [],
            Except = [],
            AddOnly = [],
            Open = null,
            Remembered = remembered,
        });
    }

    /// <summary>
    /// DL-8 (a, b), DL-9: what a card move request does to the table. Not yet applied on main, it
    /// changes nothing: a finish that could not be pushed keeps its locks. Applied, To: done or review
    /// releases as finished, To: active (park, yield, stop) or To: queue (send back) as parked.
    /// </summary>
    public LockTable OnMove(string card, CardFolder to, bool applied)
    {
        if (!applied || Find(card) is null)
        {
            return this;
        }

        return Release(card, to is CardFolder.Done or CardFolder.Review ? LockRelease.Finished : LockRelease.Parked);
    }
}
