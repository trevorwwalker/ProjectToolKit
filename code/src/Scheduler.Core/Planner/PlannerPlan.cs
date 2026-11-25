// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Planner;

/// <summary>What applying one batch to main comes to (PLAN-3 to PLAN-5): the change to commit, or why it fails.</summary>
public sealed record PlannerPlan
{
    /// <summary>The batch can be committed (an empty <see cref="Change"/> means everything was done already).</summary>
    public required bool Ok { get; init; }

    /// <summary>Why not, else "".</summary>
    public string Error { get; init; } = "";

    /// <summary>The batch's message, trimmed; "" until a valid one is read. The page and log show it.</summary>
    public string Message { get; init; } = "";

    /// <summary>The commit message: <see cref="Message"/>, a blank line and the trailers.</summary>
    public string CommitMessage { get; init; } = "";

    /// <summary>What to move, write and remove; Writes are keyed by their place after the moves.</summary>
    public JobsChange? Change { get; init; }

    /// <summary>What was skipped or followed, joined with "; " for the log; "" when the batch fails.</summary>
    public string Note { get; init; } = "";

    /// <summary>Every file the batch writes, where it ends up (for <see cref="PlannerRelease"/>).</summary>
    public IReadOnlyList<WrittenFile> Written { get; init; } = [];

    /// <summary>True when there is nothing to commit.</summary>
    public bool NothingToCommit => Change is null || (Change.Moves.Count == 0 && Change.Writes.Count == 0 && Change.Removals.Count == 0);

    internal static PlannerPlan Fail(string error, string message = "") => new() { Ok = false, Error = error, Message = message };
}
