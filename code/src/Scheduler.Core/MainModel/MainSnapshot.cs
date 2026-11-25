// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.MainModel;

/// <summary>
/// One repository's model of main at one commit (state-cache.md section 1): immutable, replaced
/// whole when main moves (MM-7), so a reader that keeps it for its step sees one commit
/// throughout. Built only by <see cref="MainSnapshotBuilder"/>. Every member answers from memory.
/// </summary>
public sealed class MainSnapshot
{
    private readonly Dictionary<string, IReadOnlyList<Card>> cardsByDir;
    private readonly Dictionary<string, IReadOnlyList<DoneCard>> doneByDir;
    private readonly Dictionary<string, IReadOnlyList<JobRequest>> requestsByDir;

    internal MainSnapshot(
        MainLayout layout,
        CommitInfo commit,
        Dictionary<string, string> files,
        Dictionary<string, Card> open,
        Dictionary<string, DoneCard> done,
        bool doneLoaded,
        Dictionary<string, JobRequest> requests,
        Dictionary<string, string> texts,
        HashSet<string> watched,
        IReadOnlyList<MainCommit> log)
    {
        Layout = layout;
        Commit = commit.Sha;
        Tree = commit.Tree;
        Time = commit.Time;
        Files = files;
        OpenByPath = open;
        DoneByPath = done;
        DoneLoaded = doneLoaded;
        RequestsByPath = requests;
        Texts = texts;
        Watched = watched;
        Log = log;
        Info = commit;

        cardsByDir = Group(layout, open.Values, c => c.Path);
        doneByDir = Group(layout, done.Values, c => c.Path);
        requestsByDir = Group(layout, requests.Values, r => r.Path);
    }

    /// <summary>The layout the snapshot was built for.</summary>
    public MainLayout Layout { get; }

    /// <summary>The commit of <c>&lt;remote&gt;/&lt;branch&gt;</c> the snapshot is at.</summary>
    public string Commit { get; }

    /// <summary>That commit's root tree.</summary>
    public string Tree { get; }

    /// <summary>That commit's time.</summary>
    public DateTimeOffset Time { get; }

    /// <summary>Path to blob id for every kept file: everything under the jobs folders, and watched paths that exist.</summary>
    public IReadOnlyDictionary<string, string> Files { get; }

    /// <summary>Whether phase 2 (done and archive headers) has run (MM-5).</summary>
    public bool DoneLoaded { get; }

    /// <summary>The paths watched (MM-6), present or not.</summary>
    public IReadOnlySet<string> Watched { get; }

    /// <summary>The commits since the model was loaded, oldest first, capped (MM-12).</summary>
    public IReadOnlyList<MainCommit> Log { get; }

    internal CommitInfo Info { get; }

    internal IReadOnlyDictionary<string, Card> OpenByPath { get; }

    internal IReadOnlyDictionary<string, DoneCard> DoneByPath { get; }

    internal IReadOnlyDictionary<string, JobRequest> RequestsByPath { get; }

    internal IReadOnlyDictionary<string, string> Texts { get; }

    /// <summary>The open cards (queue, active, review) of <paramref name="jobsDir"/>, in path order.</summary>
    public IReadOnlyList<Card> Cards(string jobsDir) => cardsByDir.GetValueOrDefault(jobsDir.Trim('/'), []);

    /// <summary>The done and archive headers of <paramref name="jobsDir"/>, in path order; empty until <see cref="DoneLoaded"/>.</summary>
    public IReadOnlyList<DoneCard> DoneCards(string jobsDir) => doneByDir.GetValueOrDefault(jobsDir.Trim('/'), []);

    /// <summary>The requests of <paramref name="jobsDir"/>, in name order (as <c>RequestFiles</c> lists them).</summary>
    public IReadOnlyList<JobRequest> Requests(string jobsDir) => requestsByDir.GetValueOrDefault(jobsDir.Trim('/'), []);

    /// <summary>The kept text of a settings file or watched path, or null when it is not on main or not kept.</summary>
    public string? Settings(string path) => Texts.GetValueOrDefault(path.Trim('/'));

    /// <summary>The kept files under <paramref name="folder"/>, as <c>ls-tree -r</c> lists them, in path order.</summary>
    public IReadOnlyList<TreeEntry> Entries(string folder)
    {
        var prefix = folder.Trim('/') + "/";
        return
        [
            .. Files
                .Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new TreeEntry { Path = f.Key, BlobId = f.Value }),
        ];
    }

    /// <summary>
    /// The commits after <paramref name="since"/> up to this snapshot, oldest first; all of the log
    /// when it is null; null when <paramref name="since"/> is not in the log (beyond the cap, or
    /// from before the model was loaded), so the caller falls back to git (MM-12, MM-14).
    /// </summary>
    public IReadOnlyList<MainCommit>? Commits(string? since)
    {
        if (since is null)
        {
            return Log;
        }

        if (since == Commit && (Log.Count == 0 || Log[^1].Sha == Commit))
        {
            return [];
        }

        // The commit the log starts from (the one loaded, or the parent the log restarted at).
        if (Log.Count > 0 && Log[0].Parent == since)
        {
            return Log;
        }

        for (var i = Log.Count - 1; i >= 0; i--)
        {
            if (Log[i].Sha == since)
            {
                return [.. Log.Skip(i + 1)];
            }
        }

        return null;
    }

    private static Dictionary<string, IReadOnlyList<T>> Group<T>(MainLayout layout, IEnumerable<T> items, Func<T, string> path) =>
        items
            .GroupBy(i => layout.JobsDirOf(path(i)) ?? "", StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<T>)[.. g.OrderBy(path, Text.SortComparer)],
                StringComparer.Ordinal);
}
