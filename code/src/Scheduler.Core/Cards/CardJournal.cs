// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// The <c>## Result</c> journal: which entry is the newest, and how a line of it is shortened for
/// the page (CARD-14). A faithful port of <c>Latest-Journal</c> and <c>Short-Text</c>
/// (schedule.ps1 L568-589).
/// </summary>
public static class CardJournal
{
    // A journal line the page wrote itself (CardText.AddJournal), as opposed to one a worker wrote.
    private const string PageLine = @"^\s*(?:[-*]\s*)?\**\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2})?\s*:?\s*developer:";
    /// <summary>
    /// The newest journal entry, shortened (CARD-14). An entry starts with an optional
    /// <c>-</c>/<c>*</c> bullet and optional <c>**</c> and then a <c>yyyy-MM-dd</c>; an
    /// <c>H:mm</c>/<c>HH:mm</c> within the line's first 40 characters joins the key so that the
    /// time counts. Lines after an entry that are neither blank nor a new entry continue it. The
    /// greatest key wins and, on a tie, the later entry in the file. With no dated entry the loose
    /// text before the first one is used.
    /// </summary>
    public static string Latest(IReadOnlyList<string> lines)
    {
        var (best, loose) = Entries(lines, skipPageLines: false);
        return ShortText(best is null ? loose : best.Value.Text);
    }

    /// <summary>
    /// The newest dated journal entry as its key - <c>yyyy-MM-dd</c>, plus <c>HH:mm</c> when the
    /// entry carries a time - or null when the card has no dated entry. With
    /// <paramref name="skipPageLines"/> the lines the page wrote itself (<c>- yyyy-MM-dd HH:mm
    /// developer: ...</c>, <see cref="Moves.CardText.AddJournal"/>) are left out, which is how
    /// MOVE-2 (948-A) finds the moment a worker handed the card over rather than a line the page
    /// added while the card was leaving its waiting state.
    /// </summary>
    internal static string? LatestKey(IReadOnlyList<string> lines, bool skipPageLines) =>
        Entries(lines, skipPageLines).Best?.Key;

    private static (Entry? Best, string Loose) Entries(IReadOnlyList<string> lines, bool skipPageLines)
    {
        var entries = new List<Entry>();
        var loose = "";

        foreach (var line in lines)
        {
            // L577: the entry pattern, not an anchored one - the bullet and ** are optional.
            if (Text.Match(line, @"^\s*(?:[-*]\s*)?\**(\d{4}-\d{2}-\d{2})") is not { Success: true } m)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                // L582-583: after the first entry every other line continues it; before it, loose text.
                if (entries.Count > 0)
                {
                    var last = entries[^1];
                    entries[^1] = last with { Text = last.Text + " " + trimmed };
                }
                else
                {
                    loose += " " + trimmed;
                }

                continue;
            }

            if (skipPageLines && Text.IsMatch(line, PageLine))
            {
                continue;
            }

            var key = m.Groups[1].Value;

            // L578-579: a time in the first 40 characters of the line joins the key, hours as two
            // digits, so "9:05" and "09:05" compare as one.
            var head = line[..Math.Min(40, line.Length)];
            if (Text.Match(head, @"\b(\d{1,2}):(\d{2})\b") is { Success: true } t)
            {
                var hour = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
                key += " " + hour.ToString("D2", CultureInfo.InvariantCulture) + ":" + t.Groups[2].Value;
            }

            entries.Add(new Entry(key, line.Trim()));
        }

        Entry? best = null;
        foreach (var e in entries)
        {
            // L585: -ge, so an entry with the same key as the best so far replaces it.
            if (best is null || Text.SortComparer.Compare(e.Key, best.Value.Key) >= 0)
            {
                best = e;
            }
        }

        return (best, loose);
    }

    /// <summary>
    /// <c>Short-Text</c> (L568-574): <c>**</c> and backticks removed, every run of whitespace
    /// collapsed to one space, trimmed, and cut to <paramref name="max"/> characters (TrimEnd-ed)
    /// with <c>...</c> when it was longer.
    /// </summary>
    public static string ShortText(string s, int max = 300)
    {
        var text = Regex.Replace(s, @"\*\*|`", "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > max)
        {
            text = text[..max].TrimEnd() + "...";
        }

        return text;
    }

    private readonly record struct Entry(string Key, string Text);
}
