// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;

namespace Scheduler.Web.Server;

/// <summary>
/// Reading a worker's log the way the page wants it: the ANSI marks stripped, the last 500 rendered
/// lines of the run and the line still being written (PAGE-3, numbered by 610-C), or the last two
/// thousand lines for the <c>/log</c> route (PAGE-9).
/// </summary>
public static partial class LiveLog
{
    /// <summary>How many finished lines a live slot keeps (PAGE-3, 610-C).</summary>
    public const int HistoryLines = 500;

    /// <summary>How many lines the <c>/log</c> route shows (PAGE-9).</summary>
    public const int LogLines = 2000;

    private static readonly Regex Ansi = AnsiPattern();

    private static readonly Regex Runs = WhitespacePattern();

    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary>Text with the console's ANSI escape sequences taken out, as <c>Plain</c> did.</summary>
    public static string Plain(string text) => Ansi.Replace(text, string.Empty);

    /// <summary>The line being written: inner runs of whitespace closed up and the ends trimmed.</summary>
    public static string Partial(string? text) => text is null ? string.Empty : Runs.Replace(text, " ").Trim();

    /// <summary>The marker the live view puts in front of the line being written (PAGE-3).</summary>
    public const string PartialMarker = "  ~ ";

    /// <summary>
    /// The last <paramref name="maxLines"/> rendered lines of a run, oldest first (PAGE-3, 610-C).
    /// They are the window the page's cursor points into: the lines are numbered from the start of
    /// the run, so the page asks for the lines after the number it has already been given.
    /// </summary>
    public static IReadOnlyList<string> History(IReadOnlyList<string> lines, int maxLines)
    {
        var shown = new List<string>(Math.Max(0, Math.Min(maxLines, lines.Count)));
        for (var index = Math.Max(0, lines.Count - maxLines); index < lines.Count; index++)
        {
            shown.Add(Plain(lines[index]));
        }

        return shown;
    }

    /// <summary>The line being written, with the live view's marker in front of it, or empty when
    /// the worker has finished its last line (PAGE-3). It is sent apart from the finished lines so
    /// the page keeps only the finished ones in its 500 (610-C).</summary>
    public static string Marker(string? partial)
    {
        var line = Partial(partial);
        return line.Length == 0 ? string.Empty : PartialMarker + line;
    }

    /// <summary>
    /// A log file split into lines, with the last line kept apart when the worker has not finished
    /// writing it. A file that cannot be read at all is empty rather than an error: a worker is
    /// writing it, and the page asks again a moment later.
    /// </summary>
    public static (IReadOnlyList<string> Lines, string? Partial) ReadFile(string path)
    {
        string text;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return ([], null);
        }
        catch (UnauthorizedAccessException)
        {
            return ([], null);
        }

        if (text.Length == 0)
        {
            return ([], null);
        }

        var parts = text.Split('\n');
        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = parts[index].EndsWith('\r') ? parts[index][..^1] : parts[index];
        }

        if (text.EndsWith('\n'))
        {
            return (parts[..^1], null);
        }

        return (parts[..^1], parts[^1]);
    }

    /// <summary>The last <paramref name="maxLines"/> lines of a log, the partial one included.</summary>
    public static IReadOnlyList<string> TailFile(string path, int maxLines)
    {
        var (lines, partial) = ReadFile(path);
        if (partial is { Length: > 0 })
        {
            lines = [.. lines, partial];
        }

        return lines.Count <= maxLines ? lines : lines.Skip(lines.Count - maxLines).ToList();
    }
}
