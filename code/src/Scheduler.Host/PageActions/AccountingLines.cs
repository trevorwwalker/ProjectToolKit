// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;

namespace Scheduler.Host.PageActions;

/// <summary>
/// 1116-B (state-cache.md MM-15: a page refresh costs little): accounting.csv's lines as the page reads them for
/// the queue's time-box words (640-A), kept from one refresh to the next. The file only grows as runs finish, so a
/// longer file is read on from the end of the last whole line read before, starting with that line again to see
/// the file was not rewritten under it; an unchanged file (same length and last write) is not read at all, and a
/// shorter or rewritten one is read whole. A last line without its line end yet is shown as it stands and read
/// again once it is finished.
/// </summary>
public sealed class AccountingLines
{
    private const int MaxFiles = 8;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The bytes read from disk so far, over every file (what the tests measure the reads by).</summary>
    public long BytesRead { get; private set; }

    /// <summary>The file's lines, as <see cref="File.ReadAllLines(string)"/> gives them; throws as it does when the file cannot be read.</summary>
    public string[] Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("no accounting file", path);
        }

        var length = info.Length;
        var written = info.LastWriteTimeUtc;
        lock (_lock)
        {
            if (_files.TryGetValue(path, out var known) && known.Length == length && known.Written == written)
            {
                return known.Lines;
            }

            var entry = known is not null && length > known.Length && known.LastLine.Length > 0
                ? ReadOn(path, known)
                : null;
            entry ??= ReadWhole(path);
            entry.Written = written;
            if (!_files.ContainsKey(path) && _files.Count >= MaxFiles)
            {
                _files.Clear();
            }

            _files[path] = entry;
            return entry.Lines;
        }
    }

    // From the start of the last whole line read before; null when that line is no longer there (a rewrite).
    private Entry? ReadOn(string path, Entry known)
    {
        var from = known.Consumed - known.LastLine.Length;
        var bytes = ReadBytes(path, from);
        if (bytes.Length < known.LastLine.Length || !bytes.AsSpan(0, known.LastLine.Length).SequenceEqual(known.LastLine))
        {
            return null;
        }

        var entry = new Entry { Complete = [.. known.Complete], Consumed = known.Consumed, LastLine = known.LastLine, Length = from + bytes.Length };
        Take(entry, bytes, known.LastLine.Length);
        return entry;
    }

    private Entry ReadWhole(string path)
    {
        var bytes = ReadBytes(path, 0);
        var entry = new Entry { Length = bytes.Length };
        var start = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        entry.Consumed = start;
        Take(entry, bytes, start);
        return entry;
    }

    // The whole lines of bytes[start..] go on the entry's list; what follows the last line end is the tail.
    private static void Take(Entry entry, byte[] bytes, int start)
    {
        var last = Array.LastIndexOf(bytes, (byte)'\n');
        if (last >= start)
        {
            var whole = bytes.AsSpan(start, last + 1 - start);
            foreach (var line in Encoding.UTF8.GetString(whole).Split('\n'))
            {
                entry.Complete.Add(line.TrimEnd('\r'));
            }

            entry.Complete.RemoveAt(entry.Complete.Count - 1); // the empty piece after the last line end
            var previous = last > 0 ? Array.LastIndexOf(bytes, (byte)'\n', last - 1) : -1;
            entry.LastLine = bytes[(previous + 1)..(last + 1)];
            entry.Consumed += last + 1 - start;
            start = last + 1;
        }

        var tail = Encoding.UTF8.GetString(bytes, start, bytes.Length - start).TrimEnd('\r');
        entry.Lines = tail.Length > 0 ? [.. entry.Complete, tail] : [.. entry.Complete];
    }

    private byte[] ReadBytes(string path, long from)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        _ = stream.Seek(from, SeekOrigin.Begin);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        BytesRead += bytes.Length;
        return bytes;
    }

    private sealed class Entry
    {
        public List<string> Complete { get; init; } = [];

        public string[] Lines { get; set; } = [];

        public long Consumed { get; set; }

        public byte[] LastLine { get; set; } = [];

        public long Length { get; set; }

        public DateTime Written { get; set; }
    }
}
