// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Accounting;

/// <summary>
/// The control window's figures: the runs that finished in the last 24 hours, by card rather than
/// by run, following each card to where it is now (ACCT-6, <c>Card-Stats</c> L891,
/// <c>Stats-Text-Of</c> L913, <c>Run-Stats-Html</c> L929). A run that never started, hit a quota
/// or found a down model server is nobody's doing and is left out.
/// </summary>
public static class CardStats
{
    private static readonly string[] NotTheCardsDoing = ["no log", "out of quota", "model server down"];

    /// <summary>The window the figures cover.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The key of the range the page shows until the reader picks another (1095-C).</summary>
    public const string DefaultRangeKey = "24h";

    /// <summary>
    /// 1095-C: the time ranges the runs summary and the Trends chart offer, shortest first. The key
    /// is what the page remembers, the label ends the summary's "Last 6 h", the name is the
    /// dropdown's, and the bucket is the width of one bar in the Trends chart.
    /// </summary>
    public static readonly IReadOnlyList<StatsRange> Ranges =
    [
        new("1h", "1 h", "1 hour", TimeSpan.FromHours(1), TimeSpan.FromMinutes(5)),
        new("3h", "3 h", "3 hours", TimeSpan.FromHours(3), TimeSpan.FromMinutes(15)),
        new("6h", "6 h", "6 hours", TimeSpan.FromHours(6), TimeSpan.FromMinutes(30)),
        new("12h", "12 h", "12 hours", TimeSpan.FromHours(12), TimeSpan.FromHours(1)),
        new("24h", "24 h", "24 hours", TimeSpan.FromHours(24), TimeSpan.FromHours(1)),
        new("3d", "3 days", "3 days", TimeSpan.FromDays(3), TimeSpan.FromHours(6)),
        new("1w", "1 week", "1 week", TimeSpan.FromDays(7), TimeSpan.FromDays(1)),
        new("2w", "2 weeks", "2 weeks", TimeSpan.FromDays(14), TimeSpan.FromDays(1)),
    ];

    /// <summary>The range with this key, or null.</summary>
    public static StatsRange? RangeOf(string key) =>
        Ranges.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    // The summary's label for a window: the range's own, or its hours when it is none of them.
    private static string LabelOf(TimeSpan window) =>
        Ranges.FirstOrDefault(r => r.Window == window)?.Label
        ?? string.Format(CultureInfo.InvariantCulture, "{0:0.##} h", window.TotalHours);

    /// <summary>
    /// The cards a run in the window worked on, each with its runs, its minutes, the model of its
    /// latest run and where it is now. Runs are counted per card; runs whose result is <c>no
    /// log</c>, <c>out of quota</c> or <c>model server down</c>, and runs with no card, are left
    /// out. Entries come back in the order the cards were first seen.
    /// </summary>
    public static IReadOnlyList<CardRunEntry> GroupByCard(
        IReadOnlyList<RunStat> runs,
        IReadOnlyList<Card> cards,
        DateTimeOffset now,
        TimeSpan? window = null)
    {
        var from = now - (window ?? Window);
        var seen = new List<string>();
        var tally = new Dictionary<string, (int Runs, int Minutes, string Model, DateTimeOffset Last)>(Text.Comparer);
        foreach (var run in runs)
        {
            if (run.Finished < from || run.Card.Length == 0 || NotTheCardsDoing.Contains(run.Result, Text.Comparer))
            {
                continue;
            }

            if (!tally.TryGetValue(run.Card, out var e))
            {
                e = (0, 0, "", DateTimeOffset.MinValue);
                seen.Add(run.Card);
            }

            if (run.Finished >= e.Last)
            {
                e = (e.Runs, e.Minutes, run.Model, run.Finished);
            }

            tally[run.Card] = (e.Runs + 1, e.Minutes + run.Minutes, e.Model, e.Last);
        }

        // Where each card is now. A card read twice (queue and active) is the copy that is not in the queue.
        var current = new Dictionary<string, Card>(Text.Comparer);
        foreach (var card in cards)
        {
            if (!current.ContainsKey(card.Id) || card.Folder != CardFolder.Queue)
            {
                current[card.Id] = card;
            }
        }

        var entries = new List<CardRunEntry>(seen.Count);
        foreach (var id in seen)
        {
            current.TryGetValue(id, out var card);
            var e = tally[id];
            entries.Add(new CardRunEntry
            {
                Card = id,
                Runs = e.Runs,
                Minutes = e.Minutes,
                Model = e.Model,
                State = StateOf(card),
            });
        }

        return entries;
    }

    /// <summary>The line the window shows for these cards (ACCT-6), or "" when there are none.</summary>
    public static string StatsText(IReadOnlyList<CardRunEntry> entries, TimeSpan? window = null)
    {
        if (entries.Count == 0)
        {
            return "";
        }

        var done = entries.Count(e => e.State == CardRunState.Done);
        var review = entries.Count(e => e.State == CardRunState.Review);
        var questions = entries.Count(e => e.State == CardRunState.Questions);
        var open = entries.Count(e => e.State == CardRunState.Open);
        var runs = entries.Sum(e => e.Runs);
        var finished = done + review;
        // ACCT-6 says rate = (done+review)/24 and does not round it. schedule.ps1 (Stats-Text-Of
        // L913) rounds to one decimal; the developer chose the test plan's two decimals (130-C Q1).
        var rate = Math.Round(finished / (window ?? Window).TotalHours, 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture);

        // ACCT-6's wording changed on the developer's decision (520-A, 2026-11-18): the done count
        // no longer carries a percentage, so the line reads "45 cards (360 runs): done 45, ...".
        // The counts say the same thing, and the percentage of a 24 h window read as one number
        // was misread as the scheduler's health. Recorded in docs/known_gaps.md.
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} cards ({1} runs): done {2}, {3} in review, {4} waiting on questions, {5} open; {6} cards/hr",
            entries.Count,
            runs,
            done,
            review,
            questions,
            open,
            rate);
    }

    /// <summary>All the run time on these cards, as "5 h 15 min" (<c>Run-Time-Of</c> L922).</summary>
    public static string RunTimeOf(IReadOnlyList<CardRunEntry> entries)
    {
        var total = entries.Sum(e => e.Minutes);
        return total >= 60
            ? string.Format(CultureInfo.InvariantCulture, "{0} h {1} min", total / 60, total % 60)
            : string.Format(CultureInfo.InvariantCulture, "{0} min", total);
    }

    /// <summary>
    /// The window's lines (ACCT-6, <c>Run-Stats-Html</c> L929): the last 24 hours in one line,
    /// and one indented line per model when more than one model ran. <paramref name="models"/>
    /// only supplies the short names the lines show.
    /// </summary>
    public static IReadOnlyList<string> Summary(
        IReadOnlyList<RunStat> runs,
        IReadOnlyList<Card> cards,
        DateTimeOffset now,
        IReadOnlyList<ModelEntry>? models = null,
        TimeSpan? window = null)
    {
        var span = window ?? Window;
        var label = LabelOf(span);
        var all = GroupByCard(runs, cards, now, span);
        if (all.Count == 0)
        {
            return [$"Last {label}: no runs finished."];
        }

        var lines = new List<string> { $"Last {label} ({RunTimeOf(all)}): {StatsText(all, span)}" };
        var used = all
            .Select(e => e.Model)
            .Where(m => m.Length != 0)
            .Distinct(Text.Comparer)
            .OrderBy(m => m, Text.SortComparer)
            .ToList();
        if (used.Count <= 1)
        {
            return lines;
        }

        foreach (var model in used)
        {
            var mine = all.Where(e => Text.Same(e.Model, model)).ToList();
            var shortName = models?.FirstOrDefault(m => Text.Same(m.Key, model))?.Short;
            lines.Add($"  {(string.IsNullOrWhiteSpace(shortName) ? model : shortName)} ({RunTimeOf(mine)}): {StatsText(mine, span)}");
        }

        return lines;
    }

    // The card's state, as Card-Stats works it out (L899-906), with the waiting patterns of
    // Waits-On-Dev (L552) and Waits-On-Planner (L558). Core/Cards owns those patterns from 030-A.
    private static CardRunState StateOf(Card? card) => card switch
    {
        null => CardRunState.Open,
        { Folder: CardFolder.Done } => CardRunState.Done,
        { Folder: CardFolder.Review } => CardRunState.Review,
        _ => IsQuestion(card) ? CardRunState.Questions : CardRunState.Open,
    };

    private static bool IsQuestion(Card card) =>
        (card.Folder == CardFolder.Active && Text.IsMatch(card.Status, "^needs-human"))
        || ((card.Folder == CardFolder.Queue || card.Folder == CardFolder.Active)
            && (Text.IsMatch(card.Status, @"^waiting:\s*(the\s+)?(developer|dev|you|human)\b")
                || Text.IsMatch(card.Status, @"^waiting:\s*(the\s+)?planner\b")));
}

/// <summary>One time range of the runs summary and the Trends chart (1095-C).</summary>
/// <param name="Key">What the page remembers: "1h", "6h", "3d", "1w".</param>
/// <param name="Label">What the summary says: "Last 6 h", "Last 3 days".</param>
/// <param name="Name">The dropdown's text: "6 hours".</param>
/// <param name="Window">How far back the range reaches.</param>
/// <param name="Bucket">The width of one Trends bar over this range.</param>
public sealed record StatsRange(string Key, string Label, string Name, TimeSpan Window, TimeSpan Bucket);
