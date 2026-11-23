using System.Collections.Concurrent;

namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1 to MM-3): a repository's refs and objects read in process, read only. It follows a <c>.git</c> file
/// and a worktree's <c>commondir</c>; reads loose and packed refs, loose objects and pack entries through each
/// pack's <c>.idx</c> v2 (both delta kinds); ignores the multi-pack index, commit-graph, bitmaps, <c>.rev</c> and
/// <c>.mtimes</c>; never writes into the repository, never takes git's locks and computes no object ids.
/// A repository it cannot read (MM-2) is refused at <see cref="TryOpen"/> with the reason, for the CLI reader to take.
/// No file stays open between reads (MM-3): each <see cref="Begin"/> session opens the packs it needs and closes
/// them when disposed; only the <c>.idx</c> contents are kept, per pack file name, size and time.
/// Safe to share between threads; each session belongs to one.
/// </summary>
public sealed class MainObjects
{
    private readonly ConcurrentDictionary<string, (long Size, DateTime Time, PackIndex Index)> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private IReadOnlyList<Pack>? _packs;

    private MainObjects(GitFolder folder)
    {
        Folder = folder;
        Refs = new Refs(folder);
    }

    public GitFolder Folder { get; }

    public Refs Refs { get; }

    /// <summary>One pack the reader knows: the <c>.pack</c> file and its index.</summary>
    public sealed record Pack(string PackPath, PackIndex Index);

    /// <summary>
    /// The reader for <paramref name="repository"/>, or null with the reason it is refused (MM-2): another object
    /// format, reftable, a promisor remote or partial clone, alternates, a shallow clone, a format version above 1 or
    /// an extension it does not know, or no repository there.
    /// </summary>
    public static MainObjects? TryOpen(string repository, out string? refusal)
    {
        try
        {
            var (folder, why) = GitFolder.Find(repository);
            refusal = why;
            return folder is not null && why is null ? new MainObjects(folder) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            refusal = e.Message;
            return null;
        }
    }

    /// <summary>A session for one advance or one on-demand read; dispose it to close the files it opened.</summary>
    public ObjectReadSession Begin() => new(this);

    /// <summary>The object a ref points at (see <see cref="Refs.Resolve"/>), or null.</summary>
    public ObjectId? Resolve(string refName)
    {
        try
        {
            return Refs.Resolve(refName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GitObjectException($"ref {refName}: {e.Message}", e);
        }
    }

    public GitObject Read(ObjectId id)
    {
        using var s = Begin();
        return s.Read(id);
    }

    public string ReadBlobText(ObjectId id)
    {
        using var s = Begin();
        return s.ReadBlobText(id);
    }

    public IReadOnlyList<TreeEntry> ReadTree(ObjectId id)
    {
        using var s = Begin();
        return s.ReadTree(id);
    }

    public Commit ReadCommit(ObjectId id)
    {
        using var s = Begin();
        return s.ReadCommit(id);
    }

    /// <summary>The packs as last listed; with <paramref name="refresh"/>, the folder is listed again first (an id was not found: a fetch or gc may have made a new pack).</summary>
    public IReadOnlyList<Pack> Packs(bool refresh = false)
    {
        lock (_gate)
        {
            if (_packs is null || refresh)
            {
                _packs = ListPacks();
            }

            return _packs;
        }
    }

    private List<Pack> ListPacks()
    {
        var dir = Path.Combine(Folder.ObjectsDir, "pack");
        var packs = new List<(DateTime Time, Pack Pack)>();
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var idx in Directory.EnumerateFiles(dir, "pack-*.idx"))
        {
            var pack = Path.ChangeExtension(idx, ".pack");
            var info = new FileInfo(idx);
            if (!File.Exists(pack) || !info.Exists)
            {
                continue;
            }

            seen.Add(idx);
            try
            {
                var key = (info.Length, info.LastWriteTimeUtc);
                PackIndex index;
                if (_indexes.TryGetValue(idx, out var known) && known.Size == key.Length && known.Time == key.LastWriteTimeUtc)
                {
                    index = known.Index;
                }
                else
                {
                    index = PackIndex.Load(idx);
                    _indexes[idx] = (key.Length, key.LastWriteTimeUtc, index);
                }

                packs.Add((File.GetLastWriteTimeUtc(pack), new Pack(pack, index)));
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // deleted by a gc while the folder was listed
            }
        }

        foreach (var gone in _indexes.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _indexes.TryRemove(gone, out _);
        }

        // Newest first, as git looks: a fresh pack from a fetch holds the objects most reads ask for.
        return packs.OrderByDescending(p => p.Time).Select(p => p.Pack).ToList();
    }
}
