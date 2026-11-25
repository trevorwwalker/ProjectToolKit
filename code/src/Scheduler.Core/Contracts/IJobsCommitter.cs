// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// Writes to main through the answers worktree: Host resets it to the remote branch, applies
/// the change, commits and pushes, retrying as the rule asks. Used by card changes from the page
/// (A10: claims, moves, answers, workers.json), the planner outbox (A11) and removing stale
/// queue copies (CARD-19).
/// </summary>
public interface IJobsCommitter
{
    /// <summary>Apply <paramref name="change"/> as one commit on main and push it; reports success or the error.</summary>
    CommitResult Apply(JobsChange change);
}
