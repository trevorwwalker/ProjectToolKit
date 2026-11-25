// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// One commit's worth of changes to files on main (A10, A11, CARD-19, MOVE-10): moves first,
/// then writes, then removals.
/// </summary>
public sealed record JobsChange
{
    /// <summary>Files moved with <c>git mv</c>.</summary>
    public IReadOnlyList<FileMove> Moves { get; init; } = [];

    /// <summary>Files written: path in the repository to its whole new text.</summary>
    public IReadOnlyDictionary<string, string> Writes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Files removed with <c>git rm</c>.</summary>
    public IReadOnlyList<string> Removals { get; init; } = [];

    /// <summary>The commit message.</summary>
    public required string Message { get; init; }
}
