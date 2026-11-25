// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Moves;

/// <summary>
/// The line edits the page makes to a card's text (MOVE-3, MOVE-5), kept with the text's own line
/// endings: CRLF when the text has any, else LF (L1095, L1231).
/// </summary>
public static class CardText
{
    /// <summary>
    /// MOVE-3: the first <c>Status:</c> line becomes <c>Status: <paramref name="status"/></c> and
    /// the indented continuation lines under it (two or more spaces, then text) are dropped
    /// (L1124-1131, L1243-1249). A text without a Status line is returned unchanged.
    /// </summary>
    public static string SetStatus(string text, string status)
    {
        var lines = Split(text);
        SetStatus(lines, status);
        return string.Join(Eol(text), lines);
    }

    /// <summary>
    /// MOVE-5: trailing blank lines dropped, then a blank line,
    /// <c>- yyyy-MM-dd HH:mm developer: <paramref name="line"/></c> and a blank line (L1250-1254),
    /// so the text ends with one line ending.
    /// </summary>
    public static string AddJournal(string text, string line, DateTimeOffset now)
    {
        var lines = Split(text);
        AddJournal(lines, line, now);
        return string.Join(Eol(text), lines);
    }

    /// <summary>The line ending the text uses.</summary>
    internal static string Eol(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>The text's lines, as <c>-split '\r?\n'</c>.</summary>
    internal static List<string> Split(string text) => [.. Regex.Split(text, "\r?\n")];

    /// <summary>The page's time stamp, <c>yyyy-MM-dd HH:mm</c>.</summary>
    internal static string Stamp(DateTimeOffset now) => now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary><see cref="SetStatus(string, string)"/> on lines.</summary>
    internal static void SetStatus(List<string> lines, string status)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!Text.IsMatch(lines[i], "^Status:"))
            {
                continue;
            }

            lines[i] = $"Status: {status}";
            while (i + 1 < lines.Count && Text.IsMatch(lines[i + 1], @"^\s{2,}\S"))
            {
                lines.RemoveAt(i + 1);
            }

            return;
        }
    }

    /// <summary><see cref="AddJournal(string, string, DateTimeOffset)"/> on lines.</summary>
    internal static void AddJournal(List<string> lines, string line, DateTimeOffset now)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add("");
        lines.Add($"- {Stamp(now)} developer: {line}");
        lines.Add("");
    }
}
