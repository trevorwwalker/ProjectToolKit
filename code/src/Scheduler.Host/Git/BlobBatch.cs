// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;

namespace Scheduler.Host.Git;

/// <summary>
/// 1013-B: the input and output of <c>git cat-file --batch</c>. Each id is one input line; each answer is
/// <c>&lt;id&gt; &lt;type&gt; &lt;size&gt;\n&lt;size bytes&gt;\n</c>, or <c>&lt;id&gt; missing\n</c>.
/// </summary>
public static class BlobBatch
{
    /// <summary>The input for <paramref name="blobIds"/>: one id per line.</summary>
    public static byte[] Input(IEnumerable<string> blobIds) =>
        Encoding.ASCII.GetBytes(string.Concat(blobIds.Select(id => id + "\n")));

    /// <summary>The blobs in <paramref name="output"/>, by id, as UTF-8 text; a missing or malformed answer ends the read there.</summary>
    public static Dictionary<string, string> Parse(byte[] output)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var at = 0;
        while (at < output.Length)
        {
            var end = Array.IndexOf(output, (byte)'\n', at);
            if (end < 0)
            {
                break;
            }

            var head = Encoding.ASCII.GetString(output, at, end - at).TrimEnd('\r').Split(' ');
            at = end + 1;
            if (head.Length == 2 && head[1] == "missing")
            {
                continue;
            }

            if (head.Length != 3 || !int.TryParse(head[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size)
                || at + size > output.Length)
            {
                break;
            }

            if (head[1] == "blob")
            {
                texts[head[0]] = Encoding.UTF8.GetString(output, at, size);
            }

            at += size + 1; // the content and its closing newline
        }

        return texts;
    }
}
