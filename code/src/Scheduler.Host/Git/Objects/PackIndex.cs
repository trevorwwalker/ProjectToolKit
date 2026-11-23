using System.Buffers.Binary;

namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1, MM-3): one pack's <c>.idx</c> (version 2), kept in memory whole: the fanout table, the sorted ids
/// and their 32- and 64-bit offsets. Read once per pack file (by name, size and time) and closed straight after, so
/// a lookup opens only the <c>.pack</c>.
/// </summary>
public sealed class PackIndex
{
    private const int Header = 8;
    private const int FanoutBytes = 256 * 4;
    private readonly byte[] _data;
    private readonly int _idsAt;
    private readonly int _offsetsAt;
    private readonly int _largeAt;

    private PackIndex(byte[] data, int count)
    {
        _data = data;
        Count = count;
        _idsAt = Header + FanoutBytes;
        _offsetsAt = _idsAt + count * ObjectId.Length + count * 4;
        _largeAt = _offsetsAt + count * 4;
    }

    public int Count { get; }

    public static PackIndex Load(string path) => Parse(SharedFile.ReadAll(path), path);

    public static PackIndex Parse(byte[] data, string name = "index")
    {
        if (data.Length < Header + FanoutBytes || data[0] != 0xff || data[1] != (byte)'t' || data[2] != (byte)'O' || data[3] != (byte)'c')
        {
            throw new GitObjectException($"{name}: not a version 2 pack index");
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        if (version != 2)
        {
            throw new GitObjectException($"{name}: pack index version {version}");
        }

        var count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(Header + 255 * 4)));
        var index = new PackIndex(data, count);
        if ((long)index._largeAt + 2 * ObjectId.Length > data.Length)
        {
            throw new GitObjectException($"{name}: pack index is cut short");
        }

        return index;
    }

    /// <summary>The offset of <paramref name="id"/> in the pack, or null when this pack does not hold it.</summary>
    public long? Find(ObjectId id)
    {
        var first = id.FirstByte;
        var lo = first == 0 ? 0 : (int)Fanout(first - 1);
        var hi = (int)Fanout(first) - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var c = IdAt(mid).CompareTo(id);
            if (c == 0)
            {
                return OffsetAt(mid);
            }

            if (c < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return null;
    }

    /// <summary>Every id this pack holds, in order.</summary>
    public IEnumerable<ObjectId> Ids()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return IdAt(i);
        }
    }

    private uint Fanout(int b) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(Header + b * 4));

    private ObjectId IdAt(int i) => ObjectId.FromBytes(_data.AsSpan(_idsAt + i * ObjectId.Length, ObjectId.Length));

    private long OffsetAt(int i)
    {
        var small = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_offsetsAt + i * 4));
        if ((small & 0x8000_0000u) == 0)
        {
            return small;
        }

        var at = _largeAt + (int)(small & 0x7fff_ffffu) * 8;
        if (at + 8 > _data.Length)
        {
            throw new GitObjectException("pack index: 64-bit offset out of range");
        }

        return checked((long)BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(at)));
    }
}
