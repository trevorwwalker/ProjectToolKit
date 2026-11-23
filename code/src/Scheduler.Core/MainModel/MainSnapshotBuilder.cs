using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.MainModel;

/// <summary>
/// The only code that makes a <see cref="MainSnapshot"/> (state-cache.md section 1): the load in
/// two phases (MM-5), the advance by tree diff (MM-7), the writer's direct apply (MM-11), a new
/// watched path (MM-6) and the commit log with its cap (MM-12). Pure but for the object source;
/// every step either returns a whole new snapshot or throws <see cref="MainModelReadException"/>,
/// leaving the snapshot it was given untouched (CARD-3).
/// </summary>
public static class MainSnapshotBuilder
{
    /// <summary>The log keeps at least this many commits (MM-12).</summary>
    public const int LogCommits = 10_000;

    /// <summary>The log keeps at least this much time (MM-12).</summary>
    public static readonly TimeSpan LogAge = TimeSpan.FromDays(30);

    /// <summary>
    /// The most commits one advance walks back looking for the snapshot's commit; past it (a force
    /// push to an unrelated commit, or a gap longer than any poll sees) the log restarts at the new
    /// commit, as section 4 says for a force push.
    /// </summary>
    public const int WalkLimit = 1_000;

    /// <summary>
    /// Phase 1 (MM-5): the jobs subtrees and settings files at <paramref name="commit"/>, open cards
    /// and requests parsed; done and archive cards listed by blob id but not read.
    /// </summary>
    public static MainSnapshot Load(IObjectSource source, MainLayout layout, string commit, IEnumerable<string>? watched = null)
    {
        var info = ReadCommit(source, commit);
        var watch = new HashSet<string>((watched ?? []).Select(w => w.Trim('/')), StringComparer.Ordinal);
        var draft = Draft.Empty(layout, watch);
        var changes = TreeDiff.Diff(source, null, info.Tree, layout.Filter(watch));
        draft.Apply(source, changes, previous: null);
        return draft.Freeze(info, []);
    }

    /// <summary>
    /// Phase 2 (MM-5): the header of every done and archive card of <paramref name="snapshot"/>,
    /// each blob read once; headers in <paramref name="known"/> (by blob id) are not read again.
    /// </summary>
    public static MainSnapshot LoadDone(
        IObjectSource source,
        MainSnapshot snapshot,
        IReadOnlyDictionary<string, DoneCard>? known = null)
    {
        var draft = Draft.From(snapshot);
        draft.DoneLoaded = true;
        foreach (var (path, blob) in snapshot.Files)
        {
            if (draft.Done.ContainsKey(path)
                || snapshot.Layout.JobsDirOf(path) is not { } dir
                || DoneHeaders.FromPath(dir, path) is not { } header)
            {
                continue;
            }

            draft.Done[path] = known is not null && known.TryGetValue(blob, out var seen)
                ? Moved(seen, header)
                : DoneHeaders.Parse(header, ReadBlob(source, blob), blob);
        }

        return draft.Freeze(snapshot.Info, snapshot.Log);
    }

    /// <summary>
    /// MM-7: <paramref name="snapshot"/> moved to <paramref name="commit"/> by diffing the two root
    /// trees along the jobs folders and watched paths only; blobs already known by id are not read
    /// again; the commits from <paramref name="commit"/> back to the snapshot's join the log.
    /// </summary>
    public static MainSnapshot Advance(IObjectSource source, MainSnapshot snapshot, string commit, DateTimeOffset now)
    {
        if (commit == snapshot.Commit)
        {
            return snapshot;
        }

        var info = ReadCommit(source, commit);
        var changes = TreeDiff.Diff(source, snapshot.Tree, info.Tree, snapshot.Layout.Filter(snapshot.Watched));
        var draft = Draft.From(snapshot);
        draft.Apply(source, changes, snapshot);
        return draft.Freeze(info, Cap(Walk(source, snapshot, info), now));
    }

    /// <summary>
    /// MM-11: the writer's pushed commit applied directly when its parent is the snapshot's commit:
    /// only the listed paths are looked up in the new commit's tree and their blobs parsed as
    /// committed. A parent newer than the snapshot is advanced to first (MM-7). The model never steps
    /// back: a parent that is not the snapshot's commit's descendant within <see cref="WalkLimit"/>
    /// first parents (the snapshot is already past the push, or the two cannot be related) leaves the
    /// snapshot as it is, for the next ref read (MM-8). A push that did not succeed changes nothing.
    /// </summary>
    public static MainSnapshot Apply(IObjectSource source, MainSnapshot snapshot, PushedCommit push, DateTimeOffset now)
    {
        if (!push.Pushed || push.Commit == snapshot.Commit)
        {
            return snapshot;
        }

        if (push.Parent != snapshot.Commit && !Descends(source, push.Parent, snapshot.Commit))
        {
            return snapshot;
        }

        var at = push.Parent == snapshot.Commit ? snapshot : Advance(source, snapshot, push.Parent, now);
        var info = ReadCommit(source, push.Commit);
        var logged = new List<PathChange>();
        foreach (var path in push.Paths.Select(p => p.Trim('/')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = at.Files.TryGetValue(path, out var kept) ? kept : TreeDiff.Lookup(source, at.Tree, path);
            var after = TreeDiff.Lookup(source, info.Tree, path);
            if (before != after)
            {
                logged.Add(new PathChange(path, before, after));
            }
        }

        var filter = at.Layout.Filter(at.Watched);
        var draft = Draft.From(at);
        draft.Apply(source, [.. logged.Where(c => filter.Keep(c.Path))], at);
        var entry = new MainCommit(info.Sha, info.Parents.FirstOrDefault(), info.Time, info.Subject, logged);
        return draft.Freeze(info, Cap([.. at.Log, entry], now));
    }

    /// <summary>
    /// MM-6: <paramref name="path"/> added to the watched set and resolved at the snapshot's commit
    /// (its text kept when it exists). The snapshot stays at its commit.
    /// </summary>
    public static MainSnapshot Watch(IObjectSource source, MainSnapshot snapshot, string path)
    {
        path = path.Trim('/');
        if (snapshot.Watched.Contains(path))
        {
            return snapshot;
        }

        var draft = Draft.From(snapshot);
        draft.Watched.Add(path);
        if (!snapshot.Files.ContainsKey(path) && TreeDiff.Lookup(source, snapshot.Tree, path) is { } blob)
        {
            draft.Apply(source, [new PathChange(path, null, blob)], snapshot);
        }
        else if (snapshot.Files.TryGetValue(path, out var known) && !draft.Texts.ContainsKey(path))
        {
            draft.Texts[path] = ReadBlob(source, known);
        }

        return draft.Freeze(snapshot.Info, snapshot.Log);
    }

    /// <summary>
    /// MM-12's cap: the oldest commits are dropped while the log is longer than
    /// <see cref="LogCommits"/> and the oldest is older than <see cref="LogAge"/>, so it keeps
    /// 10,000 commits or 30 days, whichever is more.
    /// </summary>
    public static IReadOnlyList<MainCommit> Cap(IReadOnlyList<MainCommit> log, DateTimeOffset now)
    {
        var drop = 0;
        while (log.Count - drop > LogCommits && log[drop].Time < now - LogAge)
        {
            drop++;
        }

        return drop == 0 ? log : [.. log.Skip(drop)];
    }

    // Whether `commit` reaches `ancestor` along its first parents within WalkLimit commits; a commit
    // that cannot be read, a root or the limit answers no.
    private static bool Descends(IObjectSource source, string? commit, string ancestor)
    {
        for (var i = 0; commit is not null && i < WalkLimit; i++)
        {
            if (commit == ancestor)
            {
                return true;
            }

            commit = source.ReadCommit(commit)?.Parents.FirstOrDefault();
        }

        return false;
    }

    // The commits from `to` back along first parents to the snapshot's commit, oldest first, each
    // with every path it changed. Not reached within WalkLimit (or a root): the log restarts at `to`.
    private static IReadOnlyList<MainCommit> Walk(IObjectSource source, MainSnapshot from, CommitInfo to)
    {
        var walked = new List<MainCommit>();
        var at = to;
        while (true)
        {
            var parentSha = at.Parents.FirstOrDefault();
            var parent = parentSha is null ? null : parentSha == from.Commit ? from.Info : ReadCommit(source, parentSha);
            walked.Add(new MainCommit(at.Sha, parentSha, at.Time, at.Subject, TreeDiff.Diff(source, parent?.Tree, at.Tree, PathFilter.All)));
            if (parentSha == from.Commit)
            {
                walked.Reverse();
                return [.. from.Log, .. walked];
            }

            if (parent is null || walked.Count >= WalkLimit)
            {
                return [walked[0]];
            }

            at = parent;
        }
    }

    private static Card Moved(Card parsed, Card at)
    {
        // CARD-13's Why depends on the folder; the only part that reads the text is a review
        // card's latest journal line, which the parsed card keeps (LatestJournal), so the moved
        // card is what a fresh parse gives without reading its blob.
        var (why, whyFrom) = CardWhy.For(at.Folder, parsed.Status, []);
        if (whyFrom.Length == 0 && at.Folder == CardFolder.Review && parsed.LatestJournal.Length > 0)
        {
            (why, whyFrom) = (parsed.LatestJournal, "result");
        }

        return parsed with
        {
            Folder = at.Folder,
            Path = at.Path,
            Name = at.Name,
            Num = at.Num,
            Category = at.Category,
            Id = at.Id,
            Why = why,
            WhyFrom = whyFrom,
        };
    }

    private static DoneCard Moved(DoneCard parsed, DoneCard at) => parsed with
    {
        Folder = at.Folder,
        Path = at.Path,
        Name = at.Name,
        Num = at.Num,
        Category = at.Category,
        Id = at.Id,
    };

    private static CommitInfo ReadCommit(IObjectSource source, string sha) =>
        source.ReadCommit(sha) ?? throw new MainModelReadException("commit " + sha + " cannot be read");

    private static string ReadBlob(IObjectSource source, string blob) =>
        source.ReadBlob(blob) ?? throw new MainModelReadException("blob " + blob + " cannot be read");

    // A mutable copy of a snapshot's parts, changed by a change set and frozen into a new snapshot.
    private sealed class Draft
    {
        private Draft(MainLayout layout)
        {
            Layout = layout;
        }

        public MainLayout Layout { get; }

        public Dictionary<string, string> Files { get; private init; } = [];

        public Dictionary<string, Card> Open { get; private init; } = [];

        public Dictionary<string, DoneCard> Done { get; private init; } = [];

        public Dictionary<string, JobRequest> Requests { get; private init; } = [];

        public Dictionary<string, string> Texts { get; private init; } = [];

        public HashSet<string> Watched { get; private init; } = [];

        public bool DoneLoaded { get; set; }

        public static Draft Empty(MainLayout layout, HashSet<string> watched) => new(layout) { Watched = watched };

        public static Draft From(MainSnapshot s) => new(s.Layout)
        {
            Files = new(s.Files, StringComparer.Ordinal),
            Open = new(s.OpenByPath, StringComparer.Ordinal),
            Done = new(s.DoneByPath, StringComparer.Ordinal),
            Requests = new(s.RequestsByPath, StringComparer.Ordinal),
            Texts = new(s.Texts, StringComparer.Ordinal),
            Watched = new(s.Watched, StringComparer.Ordinal),
            DoneLoaded = s.DoneLoaded,
        };

        public MainSnapshot Freeze(CommitInfo info, IReadOnlyList<MainCommit> log) =>
            new(Layout, info, Files, Open, Done, DoneLoaded, Requests, Texts, Watched, log);

        // Each changed path: dropped when deleted; otherwise parsed by what it is, reusing what
        // `previous` already parsed from the same blob (a moved card, MM-7).
        public void Apply(IObjectSource source, IReadOnlyList<PathChange> changes, MainSnapshot? previous)
        {
            Dictionary<string, Card>? openByBlob = null;
            Dictionary<string, DoneCard>? doneByBlob = null;

            foreach (var change in changes)
            {
                var path = change.Path;
                Open.Remove(path);
                Done.Remove(path);
                Requests.Remove(path);
                Texts.Remove(path);
                if (change.NewBlob is not { } blob)
                {
                    Files.Remove(path);
                    continue;
                }

                Files[path] = blob;
                string? text = null;
                string Read() => text ??= ReadBlob(source, blob);

                if (Layout.SettingsPaths.Contains(path) || Watched.Contains(path))
                {
                    Texts[path] = Read();
                }

                if (Layout.JobsDirOf(path) is not { } dir)
                {
                    continue;
                }

                if (DoneHeaders.FromPath(dir, path) is { } header)
                {
                    if (DoneLoaded)
                    {
                        openByBlob ??= ByBlob(previous?.OpenByPath.Values, c => c.BlobId);
                        doneByBlob ??= ByBlob(previous?.DoneByPath.Values, c => c.BlobId);
                        Done[path] = doneByBlob.TryGetValue(blob, out var seenDone) ? Moved(seenDone, header)
                            : openByBlob.TryGetValue(blob, out var seenOpen) ? DoneHeaders.From(header, seenOpen, blob)
                            : DoneHeaders.Parse(header, Read(), blob);
                    }
                }
                else if (CardFiles.FromPath(dir, path) is { } card)
                {
                    openByBlob ??= ByBlob(previous?.OpenByPath.Values, c => c.BlobId);
                    Open[path] = openByBlob.TryGetValue(blob, out var seen)
                        ? Moved(seen, card)
                        : CardFiles.ParseText(card, Read()) with { BlobId = blob };
                }
                else if (RequestFiles.Read(dir, [path], _ => Read()) is [var request])
                {
                    Requests[path] = request;
                }
            }
        }

        private static Dictionary<string, T> ByBlob<T>(IEnumerable<T>? items, Func<T, string> blob)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items ?? [])
            {
                map.TryAdd(blob(item), item);
            }

            return map;
        }
    }
}
