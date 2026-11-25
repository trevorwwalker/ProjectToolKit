// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.IO.Compression;
using System.Text;

namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1, MM-3): one advance's or one on-demand read's access to the object store. Pack files it opens stay
/// open only until it is disposed; delta bases it inflates are kept for its length, by pack and offset. Not shared
/// between threads; each caller takes its own from <see cref="MainObjects.Begin"/>.
/// </summary>
public sealed class ObjectReadSession : IDisposable
{
    private const int MaxDeltaDepth = 10_000;
    private const long BaseCacheBytes = 32L * 1024 * 1024;
    private readonly MainObjects _objects;
    private readonly Dictionary<string, FileStream> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Pack, long Offset), GitObject> _bases = [];
    private long _baseBytes;

    internal ObjectReadSession(MainObjects objects) => _objects = objects;

    /// <summary>How many entries of each delta kind this session has undone (MM-1's parity tests check both kinds were met).</summary>
    public int OfsDeltas { get; private set; }

    public int RefDeltas { get; private set; }

    /// <summary>The object <paramref name="id"/>; throws <see cref="GitObjectException"/> when it is missing or cannot be read.</summary>
    public GitObject Read(ObjectId id)
    {
        try
        {
            return Read(id, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OverflowException)
        {
            throw new GitObjectException($"object {id}: {e.Message}", e);
        }
    }

    public GitObject Read(ObjectId id, GitObjectType expected)
    {
        var o = Read(id);
        return o.Type == expected ? o : throw new GitObjectException($"{id} is a {GitObject.TypeName(o.Type)}, not a {GitObject.TypeName(expected)}");
    }

    public byte[] ReadBlob(ObjectId id) => Read(id, GitObjectType.Blob).Data;

    /// <summary>A blob's text as the CLI reader gives it (GitRunner): UTF-8, a leading byte order mark taken off.</summary>
    public string ReadBlobText(ObjectId id)
    {
        var data = ReadBlob(id);
        var bom = Encoding.UTF8.Preamble;
        return Encoding.UTF8.GetString(data, data.AsSpan().StartsWith(bom) ? bom.Length : 0, data.Length - (data.AsSpan().StartsWith(bom) ? bom.Length : 0));
    }

    public IReadOnlyList<TreeEntry> ReadTree(ObjectId id) => TreeParse.Parse(Read(id, GitObjectType.Tree).Data);

    public Commit ReadCommit(ObjectId id) => CommitParse.Parse(Read(id, GitObjectType.Commit).Data);

    /// <summary>The tree of <paramref name="id"/>: a commit's tree, a tag's target peeled to a tree, or the tree itself.</summary>
    public ObjectId TreeOf(ObjectId id)
    {
        for (var depth = 0; depth < 20; depth++)
        {
            var o = Read(id);
            switch (o.Type)
            {
                case GitObjectType.Tree:
                    return id;
                case GitObjectType.Commit:
                    return CommitParse.Parse(o.Data).Tree;
                case GitObjectType.Tag:
                    id = CommitParse.TagTarget(o.Data);
                    break;
                default:
                    throw new GitObjectException($"{id} is a blob, not a tree");
            }
        }

        throw new GitObjectException($"{id}: tag chain too long");
    }

    /// <summary>The entry at <paramref name="path"/> (slash-separated) under the tree of <paramref name="root"/>, or null when there is none.</summary>
    public TreeEntry? FindPath(ObjectId root, string path)
    {
        var tree = TreeOf(root);
        TreeEntry? found = null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (found is { } f)
            {
                if (f.Kind != TreeEntryKind.Tree)
                {
                    return null;
                }

                tree = f.Id;
            }

            found = ReadTree(tree).FirstOrDefault(e => e.Name == part);
            if (found is null)
            {
                return null;
            }
        }

        return found ?? new TreeEntry("40000", "", tree);
    }

    private GitObject Read(ObjectId id, int depth)
    {
        if (depth > MaxDeltaDepth)
        {
            throw new GitObjectException($"{id}: delta chain too deep");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            foreach (var pack in _objects.Packs(refresh: attempt > 0))
            {
                if (pack.Index.Find(id) is { } offset)
                {
                    try
                    {
                        return ReadEntry(pack.PackPath, offset, depth);
                    }
                    catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                    {
                        // MM-3: a gc or repack deleted this pack after its index was read; the list is read again.
                        break;
                    }
                }
            }

            if (LooseObjects.TryRead(_objects.Folder.ObjectsDir, id) is { } loose)
            {
                return loose;
            }
        }

        throw new GitObjectException($"object {id} not found");
    }

    private GitObject ReadEntry(string packPath, long offset, int depth)
    {
        if (_bases.TryGetValue((packPath, offset), out var cached))
        {
            return cached;
        }

        var s = Stream(packPath);
        s.Position = offset;
        var c = ReadByte(s);
        var type = (c >> 4) & 7;
        long size = c & 0x0f;
        var shift = 4;
        while ((c & 0x80) != 0)
        {
            c = ReadByte(s);
            size |= (long)(c & 0x7f) << shift;
            shift += 7;
        }

        GitObject result;
        switch (type)
        {
            case 1 or 2 or 3 or 4:
                result = new GitObject((GitObjectType)type, Inflate(s, size, packPath, offset));
                break;
            case 6:
            {
                c = ReadByte(s);
                long back = c & 0x7f;
                while ((c & 0x80) != 0)
                {
                    c = ReadByte(s);
                    back = ((back + 1) << 7) | (long)(c & 0x7f);
                }

                var delta = Inflate(s, size, packPath, offset);
                var baseOffset = offset - back;
                if (back <= 0 || baseOffset < 12)
                {
                    throw new GitObjectException($"{Path.GetFileName(packPath)} at {offset}: delta base offset out of range");
                }

                var b = ReadEntry(packPath, baseOffset, depth + 1);
                Remember(packPath, baseOffset, b);
                result = new GitObject(b.Type, Delta.Apply(b.Data, delta));
                OfsDeltas++;
                break;
            }

            case 7:
            {
                Span<byte> raw = stackalloc byte[ObjectId.Length];
                s.ReadExactly(raw);
                var baseId = ObjectId.FromBytes(raw);
                var delta = Inflate(s, size, packPath, offset);
                var b = Read(baseId, depth + 1);
                result = new GitObject(b.Type, Delta.Apply(b.Data, delta));
                RefDeltas++;
                break;
            }

            default:
                throw new GitObjectException($"{Path.GetFileName(packPath)} at {offset}: unknown entry type {type}");
        }

        return result;
    }

    private void Remember(string pack, long offset, GitObject o)
    {
        if (_bases.ContainsKey((pack, offset)))
        {
            return;
        }

        if (_baseBytes + o.Data.Length > BaseCacheBytes)
        {
            _bases.Clear();
            _baseBytes = 0;
        }

        _bases[(pack, offset)] = o;
        _baseBytes += o.Data.Length;
    }

    private FileStream Stream(string packPath)
    {
        if (!_open.TryGetValue(packPath, out var s))
        {
            s = SharedFile.Open(packPath);
            _open[packPath] = s;
        }

        return s;
    }

    private static int ReadByte(Stream s) => s.ReadByte() is var b and >= 0 ? b : throw new GitObjectException("pack entry cut short");

    private static byte[] Inflate(Stream s, long size, string packPath, long offset)
    {
        var data = new byte[checked((int)size)];
        try
        {
            using var z = new ZLibStream(s, CompressionMode.Decompress, leaveOpen: true);
            z.ReadExactly(data);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            throw new GitObjectException($"{Path.GetFileName(packPath)} at {offset}: {e.Message}", e);
        }

        return data;
    }

    public void Dispose()
    {
        foreach (var s in _open.Values)
        {
            s.Dispose();
        }

        _open.Clear();
        _bases.Clear();
    }
}

/// <summary>1061-B (MM-1): an object in its own zlib file, <c>objects/&lt;2 hex&gt;/&lt;38 hex&gt;</c>, with its <c>&lt;type&gt; &lt;size&gt;\0</c> header.</summary>
public static class LooseObjects
{
    public static GitObject? TryRead(string objectsDir, ObjectId id)
    {
        var hex = id.ToString();
        var path = Path.Combine(objectsDir, hex[..2], hex[2..]);
        FileStream file;
        try
        {
            file = SharedFile.Open(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            using (file)
            using (var z = new ZLibStream(file, CompressionMode.Decompress))
            {
                var head = new StringBuilder();
                while (true)
                {
                    var b = z.ReadByte();
                    if (b < 0 || head.Length > 64)
                    {
                        throw new GitObjectException($"loose object {hex}: bad header");
                    }

                    if (b == 0)
                    {
                        break;
                    }

                    head.Append((char)b);
                }

                var parts = head.ToString().Split(' ');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var size) || size < 0)
                {
                    throw new GitObjectException($"loose object {hex}: bad header '{head}'");
                }

                var data = new byte[size];
                z.ReadExactly(data);
                return new GitObject(GitObject.ParseType(parts[0]), data);
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            throw new GitObjectException($"loose object {hex}: {e.Message}", e);
        }
    }
}
