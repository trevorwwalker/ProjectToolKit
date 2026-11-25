// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;

namespace Scheduler.Tray;

/// <summary>
/// The scheduler's dated log files in <c>&lt;repo&gt;/jobs/.logs</c> (<c>scheduler-YYYYMMDD.log</c>), read
/// by a tray that attached to a scheduler it did not start (975-B): it has no console lines of its own, so
/// its log window follows the file instead. A new day's file is picked up when it appears.
/// </summary>
public sealed class SchedulerLogTail(string folder)
{
    private const string Pattern = "scheduler-*.log";

    /// <summary>How many older files are searched for the start line.</summary>
    private const int StartLineFiles = 7;

    private string? _file;
    private long _offset;
    private string _partial = "";

    /// <summary>The folder for <paramref name="repo"/>: <c>&lt;repo&gt;/jobs/.logs</c>.</summary>
    public static string FolderFor(string repo) => Path.Combine(repo, "jobs", ".logs");

    /// <summary>
    /// Start following: the newest file's last <paramref name="recent"/> lines, and the scheduler's last start
    /// line (which gives the page address), looked for in the newest files. Later lines come from <see cref="ReadNew"/>.
    /// </summary>
    public (string? StartLine, IReadOnlyList<string> Recent) Open(int recent)
    {
        var files = Files();
        string? start = null;
        IReadOnlyList<string> tail = [];
        for (var i = 0; i < files.Count && i < StartLineFiles; i++)
        {
            var lines = ReadLines(files[i], out var length);
            if (i == 0)
            {
                _file = files[i];
                _offset = length;
                tail = lines.Skip(Math.Max(0, lines.Count - recent)).ToList();
            }

            start ??= lines.LastOrDefault(l => StartLine.TryParse(l, out _, out _));
            if (start is not null)
            {
                break;
            }
        }

        return (start, tail);
    }

    /// <summary>The whole lines written since the last look.</summary>
    public IReadOnlyList<string> ReadNew()
    {
        var newest = Files().FirstOrDefault();
        if (newest is null)
        {
            return [];
        }

        if (!string.Equals(newest, _file, StringComparison.OrdinalIgnoreCase))
        {
            _file = newest;
            _offset = 0;
            _partial = "";
        }

        string text;
        try
        {
            using var stream = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _offset)
            {
                _offset = 0;
            }

            stream.Seek(_offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
            _offset = stream.Length;
        }
        catch (IOException)
        {
            return [];
        }

        var all = (_partial + text).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        _partial = all[^1];
        return all[..^1];
    }

    private List<string> Files()
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.GetFiles(folder, Pattern).OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)];
    }

    private static List<string> ReadLines(string file, out long length)
    {
        length = 0;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            length = stream.Length;
            return [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => l.Length > 0)];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
