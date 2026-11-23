using System.Buffers.Binary;
using System.Globalization;

namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1): a SHA-1 object id, held as three numbers so it compares and hashes by value with no array.
/// Ordering is the bytes' order, the order of a pack index's sorted ids. The reader never computes one; it only looks them up.
/// </summary>
public readonly record struct ObjectId(ulong Hi, ulong Mid, uint Lo) : IComparable<ObjectId>
{
    public const int Length = 20;

    public static ObjectId FromBytes(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < Length)
        {
            throw new GitObjectException("object id shorter than 20 bytes");
        }

        return new(BinaryPrimitives.ReadUInt64BigEndian(raw), BinaryPrimitives.ReadUInt64BigEndian(raw[8..]), BinaryPrimitives.ReadUInt32BigEndian(raw[16..]));
    }

    /// <summary>The id written as 40 hex digits (either case); anything else is not an id.</summary>
    public static bool TryParse(ReadOnlySpan<char> hex, out ObjectId id)
    {
        id = default;
        if (hex.Length != Length * 2)
        {
            return false;
        }

        if (!ulong.TryParse(hex[..16], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hi)
            || !ulong.TryParse(hex[16..32], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var mid)
            || !uint.TryParse(hex[32..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var lo))
        {
            return false;
        }

        id = new(hi, mid, lo);
        return true;
    }

    public static ObjectId Parse(string hex) =>
        TryParse(hex, out var id) ? id : throw new GitObjectException($"not an object id: '{hex}'");

    /// <summary>The first byte, the index into a pack index's fanout table.</summary>
    public int FirstByte => (int)(Hi >> 56);

    public void WriteTo(Span<byte> raw)
    {
        BinaryPrimitives.WriteUInt64BigEndian(raw, Hi);
        BinaryPrimitives.WriteUInt64BigEndian(raw[8..], Mid);
        BinaryPrimitives.WriteUInt32BigEndian(raw[16..], Lo);
    }

    public int CompareTo(ObjectId other)
    {
        var c = Hi.CompareTo(other.Hi);
        if (c != 0)
        {
            return c;
        }

        c = Mid.CompareTo(other.Mid);
        return c != 0 ? c : Lo.CompareTo(other.Lo);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Hi:x16}{Mid:x16}{Lo:x8}");
}
