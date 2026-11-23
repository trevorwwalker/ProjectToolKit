using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Moves;

namespace Scheduler.Host.PageActions;

/// <summary>
/// 1056-B: the developer's note from a Send back (or Resume) goes into the card's <c>## Dev</c> section as
/// <c>- yyyy-MM-dd HH:mm developer (from the page): text</c>, in the same edit as the move, so the
/// worker that resumes the card reads it and a worker's finish keeps it (<see cref="CardRequests.KeepDev"/>).
/// </summary>
public static class DevNote
{
    private static readonly Regex Runs = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>
    /// <paramref name="edit"/> with the note added to its text. White space runs (new lines too) become one
    /// space, so the note is one list item and a leading <c>#</c> cannot start a heading; nothing else is
    /// changed. An empty note says only that the card was sent back (<paramref name="fallback"/>), never an empty quote.
    /// </summary>
    public static CardEdit Add(CardEdit edit, string? note, string fallback, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!edit.Ok)
        {
            return edit;
        }

        var said = Runs.Replace(note ?? "", " ").Trim();
        var line = $"- {now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} developer (from the page): " +
            (said.Length > 0 ? said : fallback);
        var eol = edit.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = new List<string>(edit.Text.Split('\n').Select(l => l.TrimEnd('\r')));
        var dev = lines.FindIndex(l => Regex.IsMatch(l, @"^## Dev\b"));
        if (dev >= 0)
        {
            var end = dev + 1;
            while (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal))
            {
                end++;
            }

            var last = end;
            while (last - 1 > dev && string.IsNullOrWhiteSpace(lines[last - 1]))
            {
                last--;
            }

            lines.Insert(last, line);
            if (last == dev + 1)
            {
                lines.Insert(last, "");
            }
        }
        else
        {
            var at = lines.FindIndex(l => l.StartsWith("## Result", StringComparison.Ordinal));
            if (at < 0)
            {
                while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                at = lines.Count;
                lines.AddRange(["", "## Dev", "", line, ""]);
            }
            else
            {
                lines.InsertRange(at, ["## Dev", "", line, ""]);
            }
        }

        return edit with { Text = string.Join(eol, lines) };
    }
}
