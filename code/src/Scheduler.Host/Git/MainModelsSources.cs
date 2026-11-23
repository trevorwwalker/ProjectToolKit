using System.Text;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Git;

/// <summary>
/// 1062-B: where one repository's model reads its objects and its tracking ref: in process
/// (<see cref="MainObjectsSource"/>, MM-1 to MM-3) or through the git CLI (<see cref="CliObjectSource"/>, MM-2, MM-16).
/// </summary>
public interface IMainModelSource : IObjectSource, IDisposable
{
    /// <summary>"in process", or "git CLI (&lt;reason&gt;)" for the startup log line (MM-2).</summary>
    string Kind { get; }

    /// <summary>True for the CLI reader.</summary>
    bool IsCli { get; }

    /// <summary>The sha <paramref name="refName"/> points at, or null when it does not exist or cannot be read (MM-8).</summary>
    string? ReadRef(string refName);

    /// <summary>
    /// Keeps the files a step opens open until the result is disposed, so one advance or one load opens each pack
    /// once on this thread (MM-3); reads outside a session open and close their own.
    /// </summary>
    IDisposable Session();
}

/// <summary>
/// 1062-B: the model's objects read in process by <see cref="MainObjects"/> (1061-B). A read on a thread
/// with an open <see cref="Session"/> uses it; any other read opens and closes its own (MM-3: no handle across
/// ticks). Gitlinks are left out of trees, as <see cref="IObjectSource"/> asks.
/// </summary>
public sealed class MainObjectsSource : IMainModelSource
{
    private readonly MainObjects _objects;
    private readonly ThreadLocal<ObjectReadSession?> _session = new();

    public MainObjectsSource(MainObjects objects) => _objects = objects;

    /// <inheritdoc />
    public string Kind => "in process";

    /// <inheritdoc />
    public bool IsCli => false;

    /// <inheritdoc />
    public string? ReadRef(string refName) => _objects.Resolve(refName)?.ToString();

    /// <inheritdoc />
    public IDisposable Session()
    {
        if (_session.Value is not null)
        {
            return new Ended(null);
        }

        var s = _objects.Begin();
        _session.Value = s;
        return new Ended(() =>
        {
            _session.Value = null;
            s.Dispose();
        });
    }

    /// <inheritdoc />
    public CommitInfo? ReadCommit(string sha) => With(s =>
    {
        var c = s.ReadCommit(ObjectId.Parse(sha));
        return new CommitInfo(sha, c.Tree.ToString(), [.. c.Parents.Select(p => p.ToString())], c.CommitTime, c.Subject);
    });

    /// <inheritdoc />
    public IReadOnlyList<TreeItem>? ReadTree(string treeId) => With<IReadOnlyList<TreeItem>>(s =>
        [.. s.ReadTree(ObjectId.Parse(treeId))
            .Where(e => e.Kind != TreeEntryKind.Commit)
            .Select(e => new TreeItem(e.Name, e.Id.ToString(), e.Kind == TreeEntryKind.Tree))]);

    /// <inheritdoc />
    public string? ReadBlob(string blobId) => With(s => s.ReadBlobText(ObjectId.Parse(blobId)));

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();

    private T With<T>(Func<ObjectReadSession, T> read)
    {
        if (_session.Value is { } open)
        {
            return read(open);
        }

        using var s = _objects.Begin();
        return read(s);
    }

    private sealed class Ended(Action? end) : IDisposable
    {
        public void Dispose() => end?.Invoke();
    }
}

/// <summary>
/// 1062-B: the source of a folder that is not a git repository (a project not cloned yet, a test's plain folder):
/// no ref and no objects, and no git started for it on any tick; the model stays empty until a restart finds one.
/// </summary>
public sealed class AbsentSource(string reason) : IMainModelSource
{
    /// <summary><c>MainObjects.TryOpen</c>'s refusal for a folder with no <c>.git</c>.</summary>
    public const string NotARepository = "not a git repository";

    /// <summary><c>MainObjects.TryOpen</c>'s refusal for a <c>.git</c> file whose folder is gone.</summary>
    public const string NoGitFolder = "the git folder does not exist";

    /// <inheritdoc />
    public string Kind { get; } = $"none ({reason})";

    /// <inheritdoc />
    public bool IsCli => false;

    /// <inheritdoc />
    public string? ReadRef(string refName) => null;

    /// <inheritdoc />
    public IDisposable Session() => new MainObjectsSourceNoSession();

    /// <inheritdoc />
    public CommitInfo? ReadCommit(string sha) => null;

    /// <inheritdoc />
    public IReadOnlyList<TreeItem>? ReadTree(string treeId) => null;

    /// <inheritdoc />
    public string? ReadBlob(string blobId) => null;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

internal sealed class MainObjectsSourceNoSession : IDisposable
{
    public void Dispose()
    {
    }
}

/// <summary>
/// 1062-B (MM-2, MM-16), 1074-B: the model's objects read through the git CLI, for a repository the in-process
/// reader refuses, the <c>gitReader: "cli"</c> setting, or a repository whose in-process reads failed three times in a
/// row (state-cache.md section 4). Few processes, not one per object: the first read of a tree lists it and everything
/// under it with one <c>ls-tree -r -t</c> and keeps every subtree by id, and the first read of a blob that is not kept
/// reads the blobs of every tree asked for so far with one <c>cat-file --batch</c>. Objects are immutable by id, so
/// the blob texts are dropped when the outermost <see cref="Session"/> ends, the (small) tree lists when the source is disposed or past a size cap.
/// </summary>
public sealed class CliObjectSource : IMainModelSource
{
    private const int KeepAtMost = 50_000;

    private readonly GitRunner _git;
    private readonly string _repo;
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<TreeItem>> _trees = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _blobs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private int _sessions;

    /// <param name="git">The runner.</param>
    /// <param name="repo">The repository's folder.</param>
    /// <param name="reason">Why the CLI reads it, for <see cref="Kind"/>.</param>
    public CliObjectSource(GitRunner git, string repo, string reason)
    {
        _git = git;
        _repo = repo;
        Kind = $"git CLI ({reason})";
    }

    /// <inheritdoc />
    public string Kind { get; }

    /// <inheritdoc />
    public bool IsCli => true;

    /// <inheritdoc />
    public string? ReadRef(string refName)
    {
        var r = _git.Run(_repo, "rev-parse", "--verify", "-q", refName + "^{commit}");
        return r.Ok ? r.Stdout.Trim() : null;
    }

    /// <inheritdoc />
    public IDisposable Session()
    {
        lock (_gate)
        {
            _sessions++;
        }

        return new Ended(this);
    }

    /// <inheritdoc />
    public CommitInfo? ReadCommit(string sha)
    {
        var r = _git.Run(_repo, "cat-file", "commit", sha);
        if (!r.Ok)
        {
            return null;
        }

        var c = CommitParse.Parse(Encoding.UTF8.GetBytes(r.Stdout));
        return new CommitInfo(sha, c.Tree.ToString(), [.. c.Parents.Select(p => p.ToString())], c.CommitTime, c.Subject);
    }

    /// <inheritdoc />
    public IReadOnlyList<TreeItem>? ReadTree(string treeId)
    {
        lock (_gate)
        {
            if (_trees.TryGetValue(treeId, out var kept))
            {
                _asked.Add(treeId);
                return kept;
            }

            var r = _git.Run(_repo, "ls-tree", "-r", "-t", "-z", treeId);
            if (!r.Ok)
            {
                return null;
            }

            Keep(treeId, r.Stdout);
            _asked.Add(treeId);
            return _trees.GetValueOrDefault(treeId);
        }
    }

    /// <inheritdoc />
    public string? ReadBlob(string blobId)
    {
        lock (_gate)
        {
            if (_blobs.TryGetValue(blobId, out var kept))
            {
                return kept;
            }

            var want = new List<string> { blobId };
            var seen = new HashSet<string>(StringComparer.Ordinal) { blobId };
            foreach (var tree in _asked)
            {
                foreach (var item in _trees[tree])
                {
                    if (!item.IsTree && !_blobs.ContainsKey(item.Id) && !_missing.Contains(item.Id) && seen.Add(item.Id))
                    {
                        want.Add(item.Id);
                    }
                }
            }

            if (want.Count > 1)
            {
                var output = _git.RunBinary(_repo, BlobBatch.Input(want), "cat-file", "--batch");
                if (output is not null)
                {
                    foreach (var (id, text) in BlobBatch.Parse(output))
                    {
                        _blobs[id] = text;
                    }
                }

                if (_blobs.TryGetValue(blobId, out kept))
                {
                    return kept;
                }
            }

            var r = _git.Run(_repo, "cat-file", "blob", blobId);
            if (!r.Ok)
            {
                _missing.Add(blobId);
                return null;
            }

            _blobs[blobId] = r.Stdout;
            return r.Stdout;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            Clear();
        }
    }

    /// <summary>The recursive listing <paramref name="listing"/> of tree <paramref name="rootId"/>, kept as one entry list per tree id.</summary>
    private void Keep(string rootId, string listing)
    {
        if (_trees.Count + _blobs.Count > KeepAtMost)
        {
            Clear();
        }

        var idOfDir = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = rootId };
        var byDir = new Dictionary<string, List<TreeItem>>(StringComparer.Ordinal) { [""] = [] };
        foreach (var line in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> <type> <id>	<path>"
            var tab = line.IndexOf('	');
            var head = tab < 0 ? [] : line[..tab].Split(' ');
            if (head.Length != 3 || head[1] is not ("blob" or "tree"))
            {
                continue;
            }

            var path = line[(tab + 1)..];
            var slash = path.LastIndexOf('/');
            var dir = slash < 0 ? "" : path[..slash];
            var isTree = head[1] == "tree";
            if (byDir.TryGetValue(dir, out var into))
            {
                into.Add(new TreeItem(path[(slash + 1)..], head[2], isTree));
            }

            if (isTree)
            {
                idOfDir[path] = head[2];
                byDir[path] = [];
            }
        }

        foreach (var (dir, items) in byDir)
        {
            _trees[idOfDir[dir]] = items;
        }
    }

    private void Clear()
    {
        _trees.Clear();
        _blobs.Clear();
        _asked.Clear();
        _missing.Clear();
    }

    private sealed class Ended(CliObjectSource owner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
            {
                return;
            }

            lock (owner._gate)
            {
                if (--owner._sessions <= 0)
                {
                    owner._sessions = 0;
                    owner._blobs.Clear();
                    owner._missing.Clear();
                }
            }
        }
    }
}
