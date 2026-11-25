// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Accounting;

/// <summary>
/// The daily figures the Trends window charts (roadmap item 3, card 630-A): per local day and per
/// model, how many cards finished (done or review) and how many runs it took, over the last 14
/// days, so a change - a model, a prompt, an area split - shows whether it helped. The runs that
/// were nobody's doing are left out exactly as ACCT-6 leaves them out, and a run with no card is
/// no card's work. Time comes in as an instant and a zone, so the grouping needs no clock.
/// </summary>
public static class Trends
{
    /// <summary>How many local days the chart covers, today included.</summary>
    public const int WindowDays = 14;

    // ACCT-6: a run that never started, hit a quota or found a down model server is nobody's doing.
    private static readonly string[] NotTheCardsDoing = ["no log", "out of quota", "model server down"];

    // The results that mean the card finished, which is what the bars count.
    private static readonly string[] FinishedWords = ["done", "review"];

    /// <summary>
    /// The last <paramref name="days"/> local days, oldest first, today last, whether or not
    /// anything ran on them. Within a day the models come back in name order; the cards a model
    /// finished are counted once each, however many runs it took, and a card that ran on two models
    /// the same day counts as one under each - which is the comparison the chart exists for.
    /// <paramref name="zone"/> is the machine's own unless a test gives another.
    /// </summary>
    public static IReadOnlyList<TrendDay> Daily(
        IReadOnlyList<RunStat> runs,
        DateTimeOffset now,
        int days = WindowDays,
        IReadOnlyList<ModelEntry>? models = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        zone ??= TimeZoneInfo.Local;

        var today = DateOf(now, zone);
        var first = today.AddDays(-(days - 1));

        // Runs counted per day, and within a day per model: how many runs, and which cards finished.
        var perDay = new Dictionary<DateOnly, Dictionary<string, Tally>>();
        foreach (var run in runs)
        {
            if (run.Card.Length == 0 || NotTheCardsDoing.Contains(run.Result, Text.Comparer))
            {
                continue;
            }

            var date = DateOf(run.Finished, zone);
            if (date < first || date > today)
            {
                continue;
            }

            if (!perDay.TryGetValue(date, out var byModel))
            {
                byModel = new Dictionary<string, Tally>(Text.Comparer);
                perDay[date] = byModel;
            }

            if (!byModel.TryGetValue(run.Model, out var tally))
            {
                tally = new Tally();
                byModel[run.Model] = tally;
            }

            tally.Runs++;
            if (FinishedWords.Contains(run.Result, Text.Comparer))
            {
                tally.Finished.Add(run.Card);
            }
        }

        var window = new List<TrendDay>(days);
        for (var day = 0; day < days; day++)
        {
            var date = first.AddDays(day);
            if (!perDay.TryGetValue(date, out var modelsThatDay))
            {
                window.Add(new TrendDay { Date = date, Segments = [] });
                continue;
            }

            var segments = new List<TrendSegment>(modelsThatDay.Count);
            foreach (var model in modelsThatDay.Keys.OrderBy(k => k, Text.SortComparer))
            {
                var tally = modelsThatDay[model];
                segments.Add(new TrendSegment
                {
                    Model = model,
                    Label = Label(models, model),
                    CardsDone = tally.Finished.Count,
                    Runs = tally.Runs,
                });
            }

            window.Add(new TrendDay { Date = date, Segments = segments });
        }

        return window;
    }

    /// <summary>
    /// 1095-C: the chosen range as buckets, oldest first, the one holding <paramref name="now"/> last,
    /// whether or not anything ran in them. Buckets start on the local clock's multiples of their
    /// width (a 30 min bucket on :00 and :30, a 6 h one at 00, 06, 12, 18, a day at midnight). The
    /// bucket's runs and finished cards are counted as <see cref="Daily"/> counts a day's. The label
    /// is the time for buckets under a day (with the date where a longer range crosses midnight), and the date for a day's.
    /// </summary>
    public static IReadOnlyList<TrendDay> Range(
        IReadOnlyList<RunStat> runs,
        DateTimeOffset now,
        StatsRange range,
        IReadOnlyList<ModelEntry>? models = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(range);
        zone ??= TimeZoneInfo.Local;

        var width = range.Bucket.Ticks;
        var count = (int)Math.Ceiling(range.Window.Ticks / (double)width);
        // Whole buckets of wall-clock time since midnight, so the arithmetic below is the clock's.
        DateTime Floor(DateTime t) => t.Date.AddTicks((t.TimeOfDay.Ticks / width) * width);
        var last = Floor(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var first = last.AddTicks(-(count - 1) * width);

        var perBucket = new Dictionary<int, Dictionary<string, Tally>>();
        foreach (var run in runs)
        {
            if (run.Card.Length == 0 || NotTheCardsDoing.Contains(run.Result, Text.Comparer))
            {
                continue;
            }

            var at = TimeZoneInfo.ConvertTime(run.Finished, zone).DateTime;
            var index = (int)Math.Floor((at - first).Ticks / (double)width);
            if (at < first || index >= count)
            {
                continue;
            }

            if (!perBucket.TryGetValue(index, out var byModel))
            {
                byModel = new Dictionary<string, Tally>(Text.Comparer);
                perBucket[index] = byModel;
            }

            if (!byModel.TryGetValue(run.Model, out var tally))
            {
                tally = new Tally();
                byModel[run.Model] = tally;
            }

            tally.Runs++;
            if (FinishedWords.Contains(run.Result, Text.Comparer))
            {
                tally.Finished.Add(run.Card);
            }
        }

        var daily = range.Bucket >= TimeSpan.FromDays(1);
        var longer = range.Window > TimeSpan.FromDays(1);
        var buckets = new List<TrendDay>(count);
        for (var i = 0; i < count; i++)
        {
            var start = first.AddTicks(i * width);
            var label = daily
                ? start.ToString("MM-dd", CultureInfo.InvariantCulture)
                : start.ToString(longer && start.TimeOfDay == TimeSpan.Zero ? "MM-dd" : "HH:mm", CultureInfo.InvariantCulture);
            var segments = perBucket.TryGetValue(i, out var byModel) ? SegmentsOf(byModel, models) : [];
            buckets.Add(new TrendDay
            {
                Date = DateOnly.FromDateTime(start),
                Time = TimeOnly.FromDateTime(start),
                Label = label,
                Segments = segments,
            });
        }

        return buckets;
    }

    private static List<TrendSegment> SegmentsOf(Dictionary<string, Tally> byModel, IReadOnlyList<ModelEntry>? models)
    {
        var segments = new List<TrendSegment>(byModel.Count);
        foreach (var model in byModel.Keys.OrderBy(k => k, Text.SortComparer))
        {
            segments.Add(new TrendSegment
            {
                Model = model,
                Label = Label(models, model),
                CardsDone = byModel[model].Finished.Count,
                Runs = byModel[model].Runs,
            });
        }

        return segments;
    }

    /// <summary>
    /// The runs of the last <paramref name="days"/> local days out of <c>accounting.csv</c>'s own
    /// lines (ACCT-1), which is how the chart reaches further back than the 48 hours the Control
    /// window's figures keep (ACCT-4). Host reads the file; this only reads the lines. A line the
    /// header does not describe is left out rather than guessed at, a card name is cut to its id,
    /// and a file from before the model column gets its rows with no model.
    /// </summary>
    public static IReadOnlyList<RunStat> RunsFromRows(
        IReadOnlyList<string> lines,
        DateTimeOffset now,
        int days = WindowDays,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        zone ??= TimeZoneInfo.Local;

        var first = DateOf(now, zone).AddDays(-(days - 1));
        var runs = new List<RunStat>();
        foreach (var row in AccountingRows.Parse(lines).Rows)
        {
            if (DateOf(row.Finished, zone) < first)
            {
                continue;
            }

            // EST-8: every column by its name; a column an older row lacks reads as "".
            runs.Add(new RunStat
            {
                Finished = row.Finished,
                Card = CardId(row["card"]),
                Result = row["result"],
                Minutes = row.Minutes,
                Model = row["model"],
                CardType = row["card_type"],
                Version = row["version"],
            });
        }

        return runs;
    }

/// <summary>    /// 1030-A: the runs grouped by the card's type, untyped ("") last after feature and bug; a type    /// with no runs is left out.    /// </summary>    public static IReadOnlyList<TypeTotal> ByType(IReadOnlyList<RunStat> runs)    {        ArgumentNullException.ThrowIfNull(runs);        return [.. runs            .GroupBy(r => r.CardType.ToLowerInvariant(), Text.Comparer)            .OrderBy(g => g.Key == "feature" ? 0 : g.Key == "bug" ? 1 : 2)            .ThenBy(g => g.Key, StringComparer.Ordinal)            .Select(g => new TypeTotal            {                CardType = g.Key,                Runs = g.Count(),                Minutes = g.Sum(r => r.Minutes),                Cards = g.Select(r => r.Card).Distinct(Text.Comparer).Count(),            })];    }
    /// <summary>
    /// 1030-A: the runs grouped by the card's type, feature then bug then untyped (""); a type with
    /// no runs is left out.
    /// </summary>
    public static IReadOnlyList<TypeTotal> ByType(IReadOnlyList<RunStat> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return [.. runs
            .GroupBy(r => r.CardType.ToLowerInvariant(), StringComparer.Ordinal)
            .OrderBy(g => g.Key == "feature" ? 0 : g.Key == "bug" ? 1 : 2)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TypeTotal
            {
                CardType = g.Key,
                Runs = g.Count(),
                Minutes = g.Sum(r => r.Minutes),
                Cards = g.Select(r => r.Card).Distinct(StringComparer.Ordinal).Count(),
            })];
    }

    // The card's id, which is what its rows are filed under (ACCT-5).
    private static readonly Regex CardIdPattern = new(@"^(\d+-[A-Z])", RegexOptions.CultureInvariant);

    private static string CardId(string card) => CardIdPattern.Match(card) is { Success: true } m ? m.Value : card;

    /// <summary>
    /// Whether there is anything to chart: one day with a counted run is enough (1327-C; the
    /// two-days rule of 630-A went, so a project has its chart on its first day).
    /// </summary>
    public static bool HasTrend(IReadOnlyList<RunStat> runs, DateTimeOffset now, TimeZoneInfo? zone = null) =>
        HasTrend(Daily(runs, now, WindowDays, models: null, zone));

    /// <summary>
    /// The same question of days already grouped, which is what the page asks so it and the host
    /// cannot disagree about whether to show the window.
    /// </summary>
    public static bool HasTrend(IReadOnlyList<TrendDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        foreach (var day in days)
        {
            if (day.HasData)
            {
                return true;
            }
        }

        return false;
    }

    // models.json's short name for a key, as ACCT-6's per-model lines show it; the key when none.
    private static string Label(IReadOnlyList<ModelEntry>? models, string model) =>
        models?.FirstOrDefault(m => Text.Same(m.Key, model)) is { Short.Length: > 0 } entry ? entry.Short : model;

    // The run's date as the machine's calendar sees it. ConvertTime gives the instant in the zone;
    // its DateTime part is the wall-clock reading the day is named by.
    private static DateOnly DateOf(DateTimeOffset when, TimeZoneInfo zone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(when, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    // One model's day: its runs, and the distinct cards that finished under it.
    private sealed class Tally
    {
        public int Runs;

        public HashSet<string> Finished { get; } = new(Text.Comparer);
    }
}
