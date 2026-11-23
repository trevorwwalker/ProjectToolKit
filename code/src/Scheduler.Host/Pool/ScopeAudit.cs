using Scheduler.Core.Scopes;
using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Pool;

/// <summary>
/// SC-7 (spec B3 Q8 as answered on card 330-B): besides the check at dispatch, what a worker pushed
/// is checked against its card's scope, and a card that changed files outside it is logged as out of
/// scope. Nothing else happens to the card.
/// </summary>
/// <param name="git">The git to ask for the changed files.</param>
public sealed class ScopeAudit(GitRunner git)
{
    private readonly Dictionary<string, MainObjects?> _objects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The files changed between two commits that the scope does not cover, in path order; one line
    /// is written for them when there are any. A folder in the scope covers everything inside it, a
    /// file only itself; the scope's except paths and add-only paths are not counted as outside.
    /// </summary>
    /// <param name="repo">The repository (or worktree).</param>
    /// <param name="card">The card id.</param>
    /// <param name="scope">The card's scope.</param>
    /// <param name="from">The commit before the card's work.</param>
    /// <param name="to">The commit it pushed.</param>
    /// <param name="write">Where the line goes (the scheduler's own log).</param>
    public IReadOnlyList<string> Check(string repo, string card, Scope scope, string from, string to, Action<string> write)
    {
        // 1065-B (MM-14): the two commits' trees are compared in process; git only when the reader cannot.
        var changed = ChangedInProcess(repo, from, to);
        if (changed is null)
        {
            var r = git.Run(repo, "-c", "core.quotepath=off", "diff", "--name-only", from, to);
            if (!r.Ok)
            {
                write($"scope: card {card} could not be checked, git diff {from}..{to} failed");
                return [];
            }

            changed = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var outside = changed
            .Where(f => !Covers(scope.Paths, f) || Covers(scope.Except, f))
            .Where(f => !Covers(scope.AddOnly, f))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (outside.Count > 0)
        {
            write($"scope: card {card} is out of scope, it changed {string.Join(", ", outside)}");
        }

        return outside;
    }

    /// <summary>
    /// The files <c>git diff --name-only from to</c> lists, read in process (MM-14); null when git must be asked
    /// (no reader for the repository, MM-2; a commit given by name rather than id; an object it cannot read; a
    /// possible inexact rename, which only git's rename detection can settle).
    /// </summary>
    public IReadOnlyList<string>? ChangedInProcess(string repo, string from, string to)
    {
        if (!ObjectId.TryParse(from, out var a) || !ObjectId.TryParse(to, out var b))
        {
            return null;
        }

        MainObjects? objects;
        lock (_objects)
        {
            if (!_objects.TryGetValue(repo, out objects))
            {
                objects = InProcessHistory.Open(repo);
                _objects[repo] = objects;
            }
        }

        if (objects is null)
        {
            return null;
        }

        try
        {
            using var s = objects.Begin();
            return InProcessHistory.NameOnly(InProcessHistory.Diff(s, s.TreeOf(a), s.TreeOf(b)));
        }
        catch (Exception e) when (InProcessHistory.IsReadFailure(e))
        {
            return null;
        }
    }

    private static bool Covers(IReadOnlyList<string> paths, string file) =>
        paths.Any(p => p.EndsWith('/') ? file.StartsWith(p, StringComparison.Ordinal) : file == p);
}
