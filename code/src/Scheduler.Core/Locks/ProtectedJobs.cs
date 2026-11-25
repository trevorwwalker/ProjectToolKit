// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Locks;

/// <summary>
/// The paths under <c>jobs/</c> that stay protected, refused at push and strays in the audit even when held
/// (design <c>dynamic-locks.md</c> DL-15, developer 2026-11-21): other cards' card files (card moves are the
/// scheduler's), plans and the archive (the planner's), the scheduler's own folders, and
/// <c>jobs/requests/answered/</c>. Every other path under <c>jobs/</c> is an ordinary path. One place for
/// <see cref="PushCheck"/> and <see cref="MainAudit"/>.
/// </summary>
internal static class ProtectedJobs
{
    /// <summary>The refusal text for a protected path.</summary>
    public const string Rule = "card files in jobs/queue, active, review, done, needs-human and waiting are the scheduler's (ask with a move request), jobs/plans, jobs/archive and jobs/requests/answered the planner's (ask with a request in jobs/requests/), jobs/.logs and jobs/.planner-outbox the scheduler's own";

    private static readonly string[] CardFolders = ["queue", "active", "review", "done", "needs-human", "waiting"];

    private static readonly string[] OtherFolders = ["plans", "archive", ".logs", ".planner-outbox"];

    /// <summary>True when <paramref name="folder"/> is one of the card folders under <c>jobs/</c>.</summary>
    public static bool IsCardFolder(string folder) => CardFolders.Any(f => Same(f, folder));

    /// <summary>True when the normalised repository <paramref name="path"/> is protected.</summary>
    public static bool IsProtected(string path)
    {
        var parts = path.Split('/');
        if (parts.Length < 3 || !Same(parts[0], "jobs"))
        {
            return false;
        }

        return IsCardFolder(parts[1])
            || OtherFolders.Any(f => Same(f, parts[1]))
            || (Same(parts[1], "requests") && Same(parts[2], "answered") && parts.Length >= 4);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
