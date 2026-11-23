using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Git;

/// <summary>
/// 1049-B: a card that is done no longer needs its <c>wip/&lt;number&gt;-&lt;letter&gt;</c> branch on the remote
/// once its work is on main. <see cref="Clean"/> checks each such branch and deletes, in one push, the ones
/// whose work is on main; a branch that still holds work main does not have is kept and reported.
/// </summary>
public sealed partial class WipBranches
{
    private readonly GitRunner _git;
    private readonly string _remote;
    private readonly string _main;
    private readonly ISchedulerLog? _log;

    /// <param name="git">Runs git.</param>
    /// <param name="remote">The remote, <c>origin</c>.</param>
    /// <param name="main">The main branch, <c>main</c>.</param>
    /// <param name="log">Where deletions and kept branches are logged, or null.</param>
    public WipBranches(GitRunner git, string remote, string main, ISchedulerLog? log = null)
    {
        _git = git;
        _remote = remote;
        _main = main;
        _log = log;
    }

    /// <summary>The branch of the card <paramref name="card"/> (<c>1049-B-slug</c> gives <c>wip/1049-B</c>), or "" when the name has no number and letter.</summary>
    public static string BranchOf(string card)
    {
        var m = CardId().Match(card);
        return m.Success ? "wip/" + m.Groups[1].Value : "";
    }

    /// <summary>
    /// Checks the wip branch of every card in <paramref name="cards"/> (cards now in <c>done/</c>) from the repository
    /// <paramref name="dir"/> and deletes the ones whose work is on main with a single push. Returns, per card, the
    /// files the branch holds that main does not have; a card absent from the result had no branch or had it deleted.
    /// </summary>
    /// <param name="dir">The repository.</param>
    /// <param name="cards">Cards now in <c>done/</c>.</param>
    /// <param name="existing">1053-B: the remote's wip branches from one listing (<see cref="ListRemote"/>); null asks git per card.</param>
    /// <param name="logKept">1053-B: whether a kept branch is logged this time (given the branch); null logs always.</param>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Clean(string dir, IEnumerable<string> cards,
        IReadOnlySet<string>? existing = null, Func<string, bool>? logKept = null)
    {
        var kept = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var delete = new List<(string Card, string Branch)>();
        foreach (var card in cards.Distinct(StringComparer.Ordinal))
        {
            var branch = BranchOf(card);
            if (branch.Length == 0 || !(existing is null ? Exists(dir, branch) : existing.Contains(branch)))
            {
                continue;
            }

            var fetch = _git.Run(dir, "fetch", "-q", _remote, $"+refs/heads/{_main}:refs/remotes/{_remote}/{_main}", $"+refs/heads/{branch}:refs/remotes/{_remote}/{branch}");
            if (!fetch.Ok)
            {
                _log?.Write($"{branch} left alone: fetch failed: {fetch.Stderr.Trim()}");
                continue;
            }

            var files = NotOnMainInProcess(dir, branch) ?? NotOnMain(dir, $"{_remote}/{branch}", $"{_remote}/{_main}");
            if (files is null)
            {
                _log?.Write($"{branch} left alone: could not compare it with {_main}");
            }
            else if (files.Count > 0)
            {
                kept[card] = files;
                if (logKept?.Invoke(branch) ?? true)
                {
                    _log?.Write($"{branch} kept: holds work not on {_main}: {string.Join(", ", files)}");
                }
            }
            else
            {
                delete.Add((card, branch));
            }
        }

        if (delete.Count > 0)
        {
            var args = new List<string> { "push", "-q", _remote, "--delete" };
            args.AddRange(delete.Select(d => d.Branch));
            var push = _git.Run(dir, [.. args]);
            _log?.Write(push.Ok
                ? $"deleted {string.Join(", ", delete.Select(d => d.Branch))} on {_remote}: its work is on {_main}"
                : $"could not delete {string.Join(", ", delete.Select(d => d.Branch))}: {push.Stderr.Trim()}");
        }

        return kept;
    }

    /// <summary>1053-B: the remote's <c>wip/*</c> branches, in one <c>ls-remote</c>; null when git fails.</summary>
    public IReadOnlySet<string>? ListRemote(string dir)
    {
        var r = _git.Run(dir, "ls-remote", "--heads", _remote, "wip/*");
        if (!r.Ok)
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            var name = (tab < 0 ? line : line[(tab + 1)..]).Trim();
            if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                _ = set.Add(name["refs/heads/".Length..]);
            }
        }

        return set;
    }

    private bool Exists(string dir, string branch) =>
        _git.Run(dir, "ls-remote", "--heads", _remote, $"refs/heads/{branch}") is { Ok: true } r && r.Stdout.Trim().Length > 0;

    /// <summary>Files the branch changed since the merge base where main still has the base's version but the branch differs; null when git cannot say.</summary>
    public List<string>? NotOnMain(string dir, string branch, string main)
    {
        var bas = _git.Run(dir, "merge-base", branch, main);
        if (!bas.Ok)
        {
            return null;
        }

        var m = bas.Stdout.Trim();
        var diff = _git.Run(dir, "diff", "--name-only", "--no-renames", "-z", m, branch);
        if (!diff.Ok)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var file in diff.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var theirs = Blob(dir, branch, file);
            var ours = Blob(dir, main, file);
            if (theirs == ours || ours != Blob(dir, m, file))
            {
                continue; // on main as it is, or main changed it later
            }

            result.Add(file);
        }

        return result;
    }

    /// <summary>
    /// What <see cref="NotOnMain"/> answers, read in process after the fetch (MM-14): the merge base, the files the
    /// wip branch changed since it, and each file's blob on the branch, on main and at the base. Null when git must
    /// be asked: no reader for the repository (MM-2), a ref or object it cannot read, more than one best merge base,
    /// or none (git's own answer then decides, as before).
    /// </summary>
    public List<string>? NotOnMainInProcess(string dir, string branch)
    {
        var objects = InProcessHistory.Open(dir);
        if (objects is null)
        {
            return null;
        }

        try
        {
            if (objects.Resolve($"refs/remotes/{_remote}/{branch}") is not { } theirsAt
                || objects.Resolve($"refs/remotes/{_remote}/{_main}") is not { } oursAt)
            {
                return null;
            }

            using var s = objects.Begin();
            var (known, bas) = InProcessHistory.MergeBase(s, theirsAt, oursAt);
            if (!known || bas is not { } m)
            {
                return null;
            }

            var result = new List<string>();
            foreach (var change in InProcessHistory.Diff(s, s.TreeOf(m), s.TreeOf(theirsAt)))
            {
                var theirs = BlobAt(s, theirsAt, change.Path);
                var ours = BlobAt(s, oursAt, change.Path);
                if (theirs == ours || ours != BlobAt(s, m, change.Path))
                {
                    continue; // on main as it is, or main changed it later
                }

                result.Add(change.Path);
            }

            return result;
        }
        catch (Exception e) when (InProcessHistory.IsReadFailure(e))
        {
            return null;
        }
    }

    // What `git rev-parse --verify -q <rev>:<file>` prints: the id at that path, or "" when there is none.
    private static string BlobAt(ObjectReadSession s, ObjectId commit, string file) =>
        s.FindPath(commit, file)?.Id.ToString() ?? "";

    private string Blob(string dir, string rev, string file) =>
        _git.Run(dir, "rev-parse", "--verify", "-q", $"{rev}:{file}").Stdout.Trim();

    [GeneratedRegex(@"^(\d+-[A-Za-z])(?:-|$)")]
    private static partial Regex CardId();
}
