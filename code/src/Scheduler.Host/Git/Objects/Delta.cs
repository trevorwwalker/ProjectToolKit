namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1): git's delta format, the body of an <c>OFS_DELTA</c> or <c>REF_DELTA</c> pack entry: the base's
/// size, the result's size, then copy instructions (from the base) and insert instructions (literal bytes).
/// </summary>
public static class Delta
{
    public static byte[] Apply(ReadOnlySpan<byte> baseData, ReadOnlySpan<byte> delta)
    {
        var at = 0;
        var baseSize = ReadSize(delta, ref at);
        if (baseSize != baseData.Length)
        {
            throw new GitObjectException($"delta: base is {baseData.Length} bytes, the delta expects {baseSize}");
        }

        var size = ReadSize(delta, ref at);
        var result = new byte[checked((int)size)];
        var outAt = 0;
        while (at < delta.Length)
        {
            var op = delta[at++];
            if ((op & 0x80) != 0)
            {
                long offset = 0;
                long length = 0;
                for (var i = 0; i < 4; i++)
                {
                    if ((op & (1 << i)) != 0)
                    {
                        offset |= (long)Next(delta, ref at) << (8 * i);
                    }
                }

                for (var i = 0; i < 3; i++)
                {
                    if ((op & (0x10 << i)) != 0)
                    {
                        length |= (long)Next(delta, ref at) << (8 * i);
                    }
                }

                if (length == 0)
                {
                    length = 0x10000;
                }

                if (offset + length > baseData.Length || outAt + length > result.Length)
                {
                    throw new GitObjectException("delta: copy out of range");
                }

                baseData.Slice((int)offset, (int)length).CopyTo(result.AsSpan(outAt));
                outAt += (int)length;
            }
            else if (op != 0)
            {
                if (at + op > delta.Length || outAt + op > result.Length)
                {
                    throw new GitObjectException("delta: insert out of range");
                }

                delta.Slice(at, op).CopyTo(result.AsSpan(outAt));
                at += op;
                outAt += op;
            }
            else
            {
                throw new GitObjectException("delta: reserved instruction 0");
            }
        }

        if (outAt != result.Length)
        {
            throw new GitObjectException($"delta: made {outAt} bytes, expected {result.Length}");
        }

        return result;
    }

    private static long ReadSize(ReadOnlySpan<byte> delta, ref int at)
    {
        long size = 0;
        var shift = 0;
        byte b;
        do
        {
            b = Next(delta, ref at);
            size |= (long)(b & 0x7f) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0 && shift < 64);

        return size;
    }

    private static byte Next(ReadOnlySpan<byte> delta, ref int at) =>
        at < delta.Length ? delta[at++] : throw new GitObjectException("delta: cut short");
}
