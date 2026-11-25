// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Locks;

/// <summary>
/// One lock request, as a worker's <c>lock</c> command wrote it into the moves folder (design
/// <c>dynamic-locks.md</c>, section 3, DL-4).
/// </summary>
public sealed record LockRequest
{
    /// <summary>The request file's name, which orders it and names its reply.</summary>
    public string FileName { get; init; } = "";

    /// <summary>Why the request cannot be read or used, or empty.</summary>
    public string Error { get; init; } = "";

    /// <summary>True when the request could be read.</summary>
    public bool Ok => Error.Length == 0;

    /// <summary>The card's file name without <c>.md</c>.</summary>
    public string Card { get; init; } = "";

    /// <summary>The slot that asked, or null.</summary>
    public int? Slot { get; init; }

    /// <summary>The entries asked for: files, folders ending <c>/</c>, globs.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>The worker's one-line reason.</summary>
    public string Why { get; init; } = "";

    /// <summary>A request that cannot be read.</summary>
    public static LockRequest Failed(string fileName, string error) => new() { FileName = fileName, Error = error };
}
