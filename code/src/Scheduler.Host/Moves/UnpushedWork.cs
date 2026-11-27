// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Host.Git;

namespace Scheduler.Host.Moves;

/// <summary>
/// CM-23 (card 1237-B): a card is not settled done or review while its work is not on main. When a worker's run
/// ends with a waiting <c>To: done</c> or <c>To: review</c> request, the commits made since the run started that
/// main does not have - on the slot worktree's HEAD or on a <c>wip/&lt;card&gt;</c> branch - are counted; any at all
/// send the card back to wait on the planner (<c>To: queue</c>, <c>waiting: planner (&lt;reason&gt;)</c>; card 1300-B). The slot worktrees
/// share the repository's refs, so the remote's main is the newest any worker or the scheduler has seen; no fetch.
/// Costs one git start per finished card that asks for done or review; only when commits are found, one
/// <c>for-each-ref</c> and, per ref, the one rule of card 1492-B (<see cref="WipBranches.Unmerged"/>: a rebased branch
/// whose patches are on main is landed), and the Status names the commits left to apply (at most three).
/// </summary>
/// <param name="git">Runs git.</param>
/// <param name="remote">The remote, <c>origin</c>.</param>
/// <param name="main">The main branch, <c>main</c>.</param>
public sealed class UnpushedWork(GitRunner git, string remote, string main)
{
    /// <summary>
    /// The reason the card <paramref name="card"/>, whose run in <paramref name="slotDir"/> started at
    /// <paramref name="since"/>, is not settled ("finished, but its work is not on main: wip/1228-B"); "" when every
    /// commit of the run is on main or it made none, and null when git cannot say (the card is settled as before).
    /// </summary>
    public string? Reason(string slotDir, string card, DateTimeOffset since)
    {
        if (!Directory.Exists(slotDir))
        {
            return null;
        }

        var branch = WipBranches.BranchOf(card);
        var args = new List<string>
        {
            "rev-list", "--count",
            "--since=" + since.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " +0000",
            "HEAD",
        };
        if (branch.Length > 0)
        {
            args.Add($"--glob=refs/remotes/{remote}/{branch}*");
            args.Add($"--glob=refs/heads/{branch}*");
        }

        args.Add($"^refs/remotes/{remote}/{main}");
        var r = git.Run(slotDir, [.. args]);
        if (!r.Ok || !int.TryParse(r.Stdout.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        if (n == 0)
        {
            return "";
        }

        // 1492-B: commits that are not ancestors of main may still be on it (a rebase): the one rule decides
        var refs = new List<string> { "HEAD" };
        if (branch.Length > 0)
        {
            var listed = Refs(slotDir, branch);
            if (listed is null)
            {
                return null;
            }

            refs.AddRange(listed);
        }

        var left = Left(slotDir, refs, since);
        if (left.Where.Count == 0)
        {
            return "";
        }

        var where = left.Where.Contains("HEAD") && left.Where.Count == 1
            ? $"only in the worktree {Path.GetFileName(slotDir.TrimEnd('/', '\\'))}"
            : string.Join(", ", left.Where.Where(w => w != "HEAD"));
        return Words(main, Named(where, left.Commits));
    }

    /// <summary>
    /// 1492-B: <paramref name="where"/> followed by the commits left to apply, at most three named and then
    /// "and &lt;n&gt; more": "wip/1471-W: c9e8337 fix it; 0abc123 test it".
    /// </summary>
    public static string Named(string where, IReadOnlyList<string> commits) =>
        commits.Count == 0 ? where
        : where + ": " + string.Join("; ", commits.Take(3)) + (commits.Count > 3 ? $"; and {commits.Count - 3} more" : "");

    /// <summary>The reason in words for work not on <paramref name="main"/>, kept in <paramref name="where"/>.</summary>
    public static string Words(string main, string where) => $"finished, but its work is not on {main}: {where}";

    /// <summary>CM-23, CM-24 (card 1300-B): the Status of a card held for <paramref name="reason"/>, which waits on the planner.</summary>
    public static string Waiting(string reason) => $"waiting: planner ({reason})";

    /// <summary>
    /// CM-23, CM-24: <paramref name="text"/> with <c>- yyyy-MM-dd HH:mm scheduler: &lt;line&gt;</c> added at its end (the
    /// Result section), in the text's own line endings.
    /// </summary>
    public static string Journal(string text, string line, DateTimeOffset now)
    {
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return text.TrimEnd() + nl + "- " + now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " scheduler: " + line + nl;
    }

    /// <summary>
    /// CM-24 (card 1300-B): the card's <c>wip/&lt;n&gt;-&lt;L&gt;</c> and <c>wip/&lt;n&gt;-&lt;L&gt;-*</c> branches, remote or local,
    /// that hold work <c>main</c> lacks, as "wip/1273-D"; "" when there are none or all their work is on main, and null
    /// when git cannot say. One <c>rev-list --count</c>; only when it finds commits, one <c>for-each-ref</c> and a file
    /// comparison per branch (as the wip cleanup judges it, so a branch whose work reached main rebased does not count).
    /// </summary>
    /// <param name="dir">A worktree of the repository (the answers worktree).</param>
    /// <param name="card">The card's name.</param>
    public string? Wip(string dir, string card)
    {
        var branch = WipBranches.BranchOf(card);
        if (branch.Length == 0 || !Directory.Exists(dir))
        {
            return branch.Length == 0 ? "" : null;
        }

        var mainRef = $"refs/remotes/{remote}/{main}";
        var r = git.Run(dir, "rev-list", "--count", $"--glob=refs/remotes/{remote}/{branch}*", $"--glob=refs/heads/{branch}*", "^" + mainRef);
        if (!r.Ok || !int.TryParse(r.Stdout.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        if (n == 0)
        {
            return "";
        }

        var refs = Refs(dir, branch);
        if (refs is null)
        {
            return null;
        }

        var left = Left(dir, refs, null);
        return left.Where.Count == 0 ? "" : Named(string.Join(", ", left.Where), left.Commits);
    }

    // The card's wip refs, remote first, as full ref names; null when git fails.
    private List<string>? Refs(string dir, string branch)
    {
        var r = git.Run(dir, "for-each-ref", "--format=%(refname)",
            $"refs/remotes/{remote}/{branch}", $"refs/remotes/{remote}/{branch}-*", $"refs/heads/{branch}", $"refs/heads/{branch}-*");
        return r.Ok ? [.. r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : null;
    }

    // Each ref judged by the one rule (WipBranches.Unmerged): the short names of those with work main lacks ("HEAD"
    // for the worktree's), and their commits left to apply, each once. A ref git cannot judge counts as not landed.
    private (List<string> Where, List<string> Commits) Left(string dir, IEnumerable<string> refs, DateTimeOffset? since)
    {
        var mainRef = $"refs/remotes/{remote}/{main}";
        var wip = new WipBranches(git, remote, main);
        var where = new List<string>();
        var commits = new List<string>();
        foreach (var full in refs)
        {
            var name = full.StartsWith($"refs/remotes/{remote}/", StringComparison.Ordinal) ? full[$"refs/remotes/{remote}/".Length..]
                : full.StartsWith("refs/heads/", StringComparison.Ordinal) ? full["refs/heads/".Length..]
                : full == "HEAD" ? "HEAD"
                : "";
            if (name.Length == 0)
            {
                continue;
            }

            var unmerged = wip.Unmerged(dir, full, mainRef, since);
            if (unmerged is { Count: 0 })
            {
                continue;
            }

            if (!where.Contains(name, StringComparer.Ordinal))
            {
                where.Add(name); // work main lacks, or git cannot compare it: the planner judges it
            }

            foreach (var c in unmerged ?? [])
            {
                var sha = c.Split(' ', 2)[0];
                if (!commits.Any(x => x.Split(' ', 2)[0] == sha))
                {
                    commits.Add(c);
                }
            }
        }

        return (where, commits);
    }
}
