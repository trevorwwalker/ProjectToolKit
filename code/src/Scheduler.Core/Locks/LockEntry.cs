// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Locks;

/// <summary>How an entry came into a card's held set (design <c>dynamic-locks.md</c>, DL-1, DL-4, DL-10).</summary>
public enum LockKind
{
    /// <summary>The card's scope when it started (CardScope: Scope line, else its category's default, plus Locks paths).</summary>
    Claim,

    /// <summary>Granted in an earlier run and remembered when that run ended without finishing (DL-10).</summary>
    Remembered,

    /// <summary>Granted during this run (DL-4, DL-5).</summary>
    Granted,
}

/// <summary>One entry of a card's held set: a scope entry (SC-8 syntax), how it came, and when.</summary>
/// <param name="Path">The entry: a file, a folder ending <c>/</c>, or a glob, relative to the repository.</param>
/// <param name="Kind">Claim, remembered or granted.</param>
/// <param name="Granted">When the card came to hold it.</param>
public sealed record LockEntry(string Path, LockKind Kind, DateTimeOffset Granted);
