using Scheduler.Core.Contracts;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Locks;

/// <summary>The answer to a lock request (design <c>dynamic-locks.md</c>, section 3).</summary>
public enum LockAnswer
{
    /// <summary>Every entry is the card's: it may change them.</summary>
    Granted,

    /// <summary>An entry is held by younger cards only: the request stays open (DL-12).</summary>
    Waiting,

    /// <summary>An entry is held by an older card, or the wait ran out: the card hands itself back (DL-12, DL-13).</summary>
    Yield,

    /// <summary>The request cannot be granted as asked: too broad, unreadable, or not a running card (DL-6, DL-22).</summary>
    Refused,
}

/// <summary>The settings the arbiter needs, from categories.json, scheduler.json and git.</summary>
public sealed record LockRules
{
    /// <summary>categories.json's shared add-only paths (DL-15, SC-5): never conflict.</summary>
    public IReadOnlyList<string> AddOnly { get; init; } = [];

    /// <summary>Each category's default scope paths (DL-6): an entry that covers all of one is too broad.</summary>
    public IReadOnlyList<IReadOnlyList<string>> CategoryDefaults { get; init; } = [];

    /// <summary>The repository's tracked files, or null when not known (then the file count is not checked).</summary>
    public IReadOnlyList<string>? TrackedFiles { get; init; }

    /// <summary>More tracked files than this under one entry is too broad (DL-6).</summary>
    public int MaxFiles { get; init; } = 200;

    /// <summary>scheduler.json <c>limits.lockWaitMinutes</c> (DL-13), 20 by default.</summary>
    public TimeSpan WaitLimit { get; init; } = TimeSpan.FromMinutes(20);
}

/// <summary>What the arbiter decided about one request, and the table after it.</summary>
public sealed record LockDecision
{
    /// <summary>The card that asked.</summary>
    public string Card { get; init; } = "";

    /// <summary>The request file the reply answers.</summary>
    public string FileName { get; init; } = "";

    /// <summary>Granted, waiting, yield or refused.</summary>
    public LockAnswer Answer { get; init; }

    /// <summary>The entry the answer is about (held by another, or too broad), or empty.</summary>
    public string Path { get; init; } = "";

    /// <summary>The card that holds <see cref="Path"/>, or empty.</summary>
    public string Holder { get; init; } = "";

    /// <summary>When a waiting request turns to yield.</summary>
    public DateTimeOffset? GivesUpAt { get; init; }

    /// <summary>Why it was refused, or why it must yield (<c>older</c> or <c>wait limit</c>).</summary>
    public string Reason { get; init; } = "";

    /// <summary>The lock table after the decision.</summary>
    public required LockTable Table { get; init; }
}

/// <summary>
/// The lock arbiter (design <c>dynamic-locks.md</c>, DL-4 to DL-6, DL-12, DL-13): a request, the
/// table and the time give granted, waiting, yield or refused, and the new table. Pure.
/// </summary>
public static class LockArbiter
{
    /// <summary>The refusal text for an entry that is too broad (DL-6).</summary>
    public const string TooBroad = "too broad: ask for the files or the folder you need";

    /// <summary>
    /// Answers <paramref name="request"/>. A card with an open request has the new entries merged into
    /// it (one open request per card), keeping its deadline. <paramref name="warnAt"/> is the card's
    /// warn time: a wait never runs past it (DL-13).
    /// </summary>
    public static LockDecision Ask(LockTable table, LockRequest request, DateTimeOffset now, LockRules rules, DateTimeOffset? warnAt = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rules);
        LockDecision Refuse(string reason, string path = "") =>
            new() { Card = request.Card, FileName = request.FileName, Answer = LockAnswer.Refused, Reason = reason, Path = path, Table = table };

        if (!request.Ok)
        {
            return Refuse(request.Error);
        }

        if (table.Find(request.Card) is not { Holds: true } card)
        {
            return Refuse($"unknown card: {request.Card} is not running");
        }

        if (request.Paths.Count == 0)
        {
            return Refuse("no paths");
        }

        foreach (var p in request.Paths)
        {
            if (PathGlob.HasUnsupportedSyntax(p))
            {
                return Refuse($"cannot match {p}: use *, ? or ** only", p);
            }

            if (IsTooBroad(p, rules))
            {
                return Refuse($"{TooBroad} ({p})", p);
            }
        }

        var open = card.Open;
        var paths = (open?.Paths ?? []).Concat(request.Paths).Distinct(StringComparer.Ordinal).ToList();
        return Decide(table, card, paths, open?.FileName ?? request.FileName, open?.Why ?? request.Why, open?.Asked ?? now, open?.Deadline, now, rules, warnAt);
    }

    /// <summary>
    /// Every open request asked again, oldest card first (DL-12: on every tick and every release),
    /// each against the table the one before it left. Returns the table and the decisions.
    /// </summary>
    public static (LockTable Table, IReadOnlyList<LockDecision> Decisions) Recheck(LockTable table, DateTimeOffset now, LockRules rules)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rules);
        var decisions = new List<LockDecision>();
        var names = table.Holding.Where(c => c.Open is not null)
            .OrderBy(c => c.Age)
            .ThenBy(c => c.Card, StringComparer.Ordinal)
            .Select(c => c.Card)
            .ToList();
        foreach (var name in names)
        {
            if (table.Find(name) is not { Holds: true, Open: { } open } card)
            {
                continue;
            }

            var d = Decide(table, card, open.Paths, open.FileName, open.Why, open.Asked, open.Deadline, now, rules, null);
            decisions.Add(d);
            table = d.Table;
        }

        return (table, decisions);
    }

    /// <summary>
    /// DL-6: the repository root, <c>**</c> or another entry of wildcards only, an entry covering the
    /// whole of a category's default, or one covering more than <see cref="LockRules.MaxFiles"/>
    /// tracked files.
    /// </summary>
    public static bool IsTooBroad(string entry, LockRules rules)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rules);
        var e = entry.Replace('\\', '/').Trim();
        if (e.StartsWith("./", StringComparison.Ordinal))
        {
            e = e[2..];
        }

        if (e.Length == 0 || e == "." || e.All(c => c is '*' or '?' or '/'))
        {
            return true;
        }

        if (rules.CategoryDefaults.Any(d => d.Count > 0 && d.All(p => PathGlob.Covers(e, p))))
        {
            return true;
        }

        return rules.TrackedFiles is { } files && files.Count(f => PathGlob.Covers(e, f)) > rules.MaxFiles;
    }

    private static LockDecision Decide(
        LockTable table,
        CardLocks card,
        IReadOnlyList<string> paths,
        string fileName,
        string why,
        DateTimeOffset asked,
        DateTimeOffset? deadline,
        DateTimeOffset now,
        LockRules rules,
        DateTimeOffset? warnAt)
    {
        // DL-4: entries the held set already covers are granted at once.
        var need = paths.Where(p => !card.Covers(p)).ToList();
        if (need.Count == 0)
        {
            var same = card.Open is null ? table : table.With(card with { Open = null });
            return new() { Card = card.Card, FileName = fileName, Answer = LockAnswer.Granted, Table = same };
        }

        // DL-5: all or nothing, against every other card that holds its set.
        var conflicts = new List<(string Path, CardLocks Holder)>();
        foreach (var p in need)
        {
            var one = Scope.Of(p);
            foreach (var h in table.Holding.Where(h => !Text.Same(h.Card, card.Card)))
            {
                if (ScopeOverlap.Shared(one, h.HeldScope, rules.AddOnly) is not null)
                {
                    conflicts.Add((p, h));
                }
            }
        }

        if (conflicts.Count == 0)
        {
            return new() { Card = card.Card, FileName = fileName, Answer = LockAnswer.Granted, Table = table.Grant(card.Card, need, now) };
        }

        LockDecision Yield((string Path, CardLocks Holder) on, string reason) => new()
        {
            Card = card.Card,
            FileName = fileName,
            Answer = LockAnswer.Yield,
            Path = on.Path,
            Holder = on.Holder.Card,
            Reason = reason,
            Table = table.With(card with { Open = null, Remembered = [.. card.Remembered.Concat(need).Distinct(StringComparer.Ordinal)] }),
        };

        // DL-12 wait-die: not older than some conflicting holder, it dies.
        var blocker = conflicts.FirstOrDefault(c => !(card.Age is { } mine && c.Holder.Age is { } theirs && mine.IsOlderThan(theirs)));
        if (blocker.Holder is not null)
        {
            return Yield(blocker, "older");
        }

        // Older than every holder, it waits: until the wait limit, or its warn time if sooner (DL-13).
        var limit = now + rules.WaitLimit;
        var until = deadline ?? (warnAt is { } w && w < limit ? w : limit);
        if (now >= until)
        {
            return Yield(conflicts[0], "wait limit");
        }

        return new()
        {
            Card = card.Card,
            FileName = fileName,
            Answer = LockAnswer.Waiting,
            Path = conflicts[0].Path,
            Holder = conflicts[0].Holder.Card,
            GivesUpAt = until,
            Table = table.With(card with { Open = new OpenLockRequest(fileName, paths, why, asked, until) }),
        };
    }
}
