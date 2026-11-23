using Scheduler.Core.Scopes;

namespace Scheduler.Core.Locks;

/// <summary>Whether a card in the lock table holds its set (design <c>dynamic-locks.md</c>, DL-1, DL-8; 1018-D).</summary>
public enum CardLockState
{
    /// <summary>
    /// Posted by dispatch, its claim not yet landed on main (995-B, 1018-D): it has a slot but no held set
    /// and (unless an earlier run fixed it) no age, and cannot ask for locks. Its scope counts at dispatch
    /// through the slot's stand-in, not in this table.
    /// </summary>
    Starting,

    /// <summary>Its claim has landed and its worker runs: it holds its set.</summary>
    Running,

    /// <summary>Not running: it holds nothing, and keeps only its age and remembered set (DL-10, DL-11).</summary>
    Released,
}

/// <summary>A card's one open lock request (DL-12, DL-13): what it waits for, and until when.</summary>
/// <param name="FileName">The request file that opened it; its reply is named after it.</param>
/// <param name="Paths">Every entry asked for, merged across requests (one open request per card).</param>
/// <param name="Why">The worker's one-line reason.</param>
/// <param name="Asked">When it was first asked.</param>
/// <param name="Deadline">When the wait turns to yield: the wait limit, or the card's warn time if sooner.</param>
public sealed record OpenLockRequest(string FileName, IReadOnlyList<string> Paths, string Why, DateTimeOffset Asked, DateTimeOffset Deadline);

/// <summary>
/// One card's row in the lock table (design <c>dynamic-locks.md</c>, DL-20): its age, held set,
/// remembered set and open request.
/// </summary>
public sealed record CardLocks
{
    /// <summary>The card's file name without <c>.md</c>.</summary>
    public required string Card { get; init; }

    /// <summary>The project the card is in (several projects may share one repository, DL-22), or empty.</summary>
    public string Project { get; init; } = "";

    /// <summary>The slot its worker runs in, or null; a request is checked against it (DL-22).</summary>
    public int? Slot { get; init; }

    /// <summary>The age fixed when its first claim landed (DL-11, 1018-D), or null for a card whose first claim has not landed.</summary>
    public CardAge? Age { get; init; }

    /// <summary>Starting, running or released.</summary>
    public CardLockState State { get; init; }

    /// <summary>The held set: claim, remembered and granted entries. Empty when released.</summary>
    public IReadOnlyList<LockEntry> Held { get; init; } = [];

    /// <summary>The claim's except entries (a category default's <c>except</c> list).</summary>
    public IReadOnlyList<string> Except { get; init; } = [];

    /// <summary>The claim's add-only entries (SC-5).</summary>
    public IReadOnlyList<string> AddOnly { get; init; } = [];

    /// <summary>
    /// Released: the entries it gets back in its next initial claim (DL-10). Running: what it asked
    /// for when it was told to yield (DL-12), added to the remembered set when it is released.
    /// </summary>
    public IReadOnlyList<string> Remembered { get; init; } = [];

    /// <summary>The open request, or null.</summary>
    public OpenLockRequest? Open { get; init; }

    /// <summary>True when it holds its set: running.</summary>
    public bool Holds => State == CardLockState.Running;

    /// <summary>The held set as a scope, for <see cref="ScopeOverlap.Shared"/>.</summary>
    public Scope HeldScope => new([.. Held.Select(e => e.Path)], Except) { AddOnly = AddOnly };

    /// <summary>
    /// True when the held set surely covers <paramref name="path"/>: an entry covers it and no except
    /// entry may reach into it. When unsure it says no, and the path is checked against the others.
    /// </summary>
    public bool Covers(string path) =>
        Held.Any(e => PathGlob.Covers(e.Path, path)) && !Except.Any(x => PathGlob.MayOverlap(x, path));
}
