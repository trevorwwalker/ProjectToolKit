using Scheduler.Core.Contracts;

namespace Scheduler.Core.Locks;

/// <summary>What one audit of main found (design <c>dynamic-locks.md</c>, DL-19).</summary>
public sealed record MainAuditResult
{
    /// <summary>The strays, in commit order.</summary>
    public IReadOnlyList<MainAuditStray> Strays { get; init; } = [];

    /// <summary>The lock table with its last audited commit moved to the newest commit read.</summary>
    public required LockTable Table { get; init; }

    /// <summary>The paths <paramref name="card"/> changed without holding them, each once, for <c>CardMoves.Stray</c>.</summary>
    public IReadOnlyList<string> StrayPaths(string card) =>
        [.. Strays.Where(s => s.Path.Length > 0 && Text.Same(s.Card, card)).Select(s => s.Path).Distinct(Text.Comparer)];
}
