using System.Security.Cryptography;
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Core.Tests.MainModel;

/// <summary>
/// An in-memory object store: commits, trees and blobs made from dictionaries of path to text,
/// with content ids, and a count of every read, so tests can say what the model read (MM-7, MM-10).
/// </summary>
public sealed class FakeObjects : IObjectSource
{
    private readonly Dictionary<string, CommitInfo> commits = [];
    private readonly Dictionary<string, IReadOnlyList<TreeItem>> trees = [];
    private readonly Dictionary<string, string> blobs = [];

    /// <summary>Object ids that cannot be read (a corrupt or deleted pack).</summary>
    public HashSet<string> Broken { get; } = [];

    /// <summary>Every commit id read, in order.</summary>
    public List<string> CommitReads { get; } = [];

    /// <summary>Every tree id read, in order.</summary>
    public List<string> TreeReads { get; } = [];

    /// <summary>Every blob id read, in order.</summary>
    public List<string> BlobReads { get; } = [];

    /// <summary>All reads so far.</summary>
    public int Reads => CommitReads.Count + TreeReads.Count + BlobReads.Count;

    /// <summary>Forget the counts.</summary>
    public void ResetCounts()
    {
        CommitReads.Clear();
        TreeReads.Clear();
        BlobReads.Clear();
    }

    /// <summary>The blob id a text gets.</summary>
    public static string BlobId(string text) => Id("blob", text);

    /// <summary>A commit of exactly <paramref name="files"/> on <paramref name="parent"/>.</summary>
    public string Commit(IReadOnlyDictionary<string, string> files, string? parent, string subject, DateTimeOffset time)
    {
        var tree = Tree(files.Select(f => (f.Key.Split('/'), f.Value)).ToList(), 0);
        var sha = Id("commit", tree + "\n" + parent + "\n" + subject + "\n" + time.ToUnixTimeSeconds() + "\n" + commits.Count);
        commits[sha] = new CommitInfo(sha, tree, parent is null ? [] : [parent], time, subject);
        return sha;
    }

    /// <summary>The root tree id of a commit made here.</summary>
    public string TreeOf(string commit) => commits[commit].Tree;

    /// <summary>The id of the subtree at <paramref name="path"/> of a commit, or null.</summary>
    public string? SubtreeOf(string commit, string path)
    {
        var id = commits[commit].Tree;
        foreach (var part in path.Split('/'))
        {
            var item = trees[id].FirstOrDefault(i => i.Name == part && i.IsTree);
            if (item is null)
            {
                return null;
            }

            id = item.Id;
        }

        return id;
    }

    public CommitInfo? ReadCommit(string sha)
    {
        CommitReads.Add(sha);
        return Broken.Contains(sha) ? null : commits.GetValueOrDefault(sha);
    }

    public IReadOnlyList<TreeItem>? ReadTree(string treeId)
    {
        TreeReads.Add(treeId);
        return Broken.Contains(treeId) ? null : trees.GetValueOrDefault(treeId);
    }

    public string? ReadBlob(string blobId)
    {
        BlobReads.Add(blobId);
        return Broken.Contains(blobId) ? null : blobs.GetValueOrDefault(blobId);
    }

    private string Tree(List<(string[] Parts, string Text)> files, int depth)
    {
        var items = new List<TreeItem>();
        foreach (var group in files.GroupBy(f => f.Parts[depth], StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var leaf = group.FirstOrDefault(f => f.Parts.Length == depth + 1);
            if (leaf.Parts is not null)
            {
                var blob = BlobId(leaf.Text);
                blobs[blob] = leaf.Text;
                items.Add(new TreeItem(group.Key, blob, false));
            }
            else
            {
                items.Add(new TreeItem(group.Key, Tree([.. group], depth + 1), true));
            }
        }

        var id = Id("tree", string.Join("\n", items.Select(i => i.Name + " " + i.Id + " " + i.IsTree)));
        trees[id] = items;
        return id;
    }

    private static string Id(string kind, string content) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(kind + "\0" + content)));
}

/// <summary>
/// An <see cref="IMainModel"/> built from a dictionary of path to text (state-cache.md section 6's
/// test helper): a real <see cref="RepositoryModel"/> over <see cref="FakeObjects"/>, loaded in
/// both phases. <see cref="Push"/> moves main the way a worker's push does (the ref moves, the
/// model advances, MM-8); <see cref="Objects"/> counts every read. No git, no folder.
/// </summary>
public sealed class FakeMain : IMainModel
{
    private readonly Dictionary<string, string> files;
    private DateTimeOffset time = new(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);

    /// <summary>Main holding <paramref name="files"/>, with the jobs folders <paramref name="jobsDirs"/> (default <c>jobs</c>).</summary>
    public FakeMain(IReadOnlyDictionary<string, string> files, IEnumerable<string>? jobsDirs = null, IClock? clock = null)
    {
        this.files = new Dictionary<string, string>(files, StringComparer.Ordinal);
        Clock = clock ?? new FixedClock(time);
        Objects = new FakeObjects();
        Layout = new MainLayout(jobsDirs ?? ["jobs"]);
        Model = new RepositoryModel(Objects, Layout, Clock);
        Head = Objects.Commit(this.files, null, "root", time);
        if (!Model.Load(Head) || !Model.LoadDone())
        {
            throw new InvalidOperationException("FakeMain did not load: " + Model.LastError);
        }
    }

    public FakeObjects Objects { get; }

    public MainLayout Layout { get; }

    public RepositoryModel Model { get; }

    public IClock Clock { get; }

    /// <summary>The tip of the fake remote branch (the model may be behind it until a ref read).</summary>
    public string Head { get; private set; }

    /// <summary>The files at <see cref="Head"/>.</summary>
    public IReadOnlyDictionary<string, string> Files => files;

    /// <summary>
    /// A commit on <see cref="Head"/> with <paramref name="changes"/> (a null text deletes the
    /// path), not yet seen by the model: as the answers worktree's commit before its push.
    /// </summary>
    public string Stage(IReadOnlyDictionary<string, string?> changes, string subject = "change")
    {
        foreach (var (path, text) in changes)
        {
            if (text is null)
            {
                files.Remove(path);
            }
            else
            {
                files[path] = text;
            }
        }

        time = time.AddMinutes(1);
        Head = Objects.Commit(files, Head, subject, time);
        return Head;
    }

    /// <summary>A push from outside the scheduler: commit, then the ref read that sees it (MM-8).</summary>
    public string Push(IReadOnlyDictionary<string, string?> changes, string subject = "push")
    {
        var sha = Stage(changes, subject);
        Model.RefRead(sha);
        return sha;
    }

    /// <summary>A card moved between folders with its text unchanged.</summary>
    public string Move(string from, string to, string subject = "move")
    {
        var text = files[from];
        return Push(new Dictionary<string, string?> { [from] = null, [to] = text }, subject);
    }

    public MainSnapshot? Snapshot => Model.Snapshot;

    public string? Commit => Model.Commit;

    public bool DoneLoaded => Model.DoneLoaded;

    public IReadOnlyList<Card> Cards(string jobsDir) => Model.Cards(jobsDir);

    public IReadOnlyList<DoneCard> DoneCards(string jobsDir) => Model.DoneCards(jobsDir);

    public IReadOnlyList<JobRequest> Requests(string jobsDir) => Model.Requests(jobsDir);

    public string? Settings(string path) => Model.Settings(path);

    public string? Text(string path) => Model.Text(path);

    public IReadOnlyList<TreeEntry> Entries(string folder) => Model.Entries(folder);

    public IReadOnlyList<string>? Changed(string from, string to) => Model.Changed(from, to);

    public IReadOnlyList<MainCommit>? Commits(string? since) => Model.Commits(since);

    /// <summary>A clock that stands still unless moved.</summary>
    public sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
