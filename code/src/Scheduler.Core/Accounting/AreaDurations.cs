// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Accounting;

/// <summary>
/// What finished cards really take, per area and model (roadmap item 4, card 640-A): the median
/// and the 90th percentile of the runs' minutes over the last <see cref="WindowRuns"/> runs or
/// <see cref="WindowDays"/> days - whichever is the shorter - of the done and review rows
/// accounting.csv holds (ACCT-1, ACCT-6). A group with fewer than <see cref="MinimumRuns"/> runs
/// gives no figures: one run is an anecdote, not a percentile. Host reads the file; this only
/// reads its lines, so the figures need no I/O to compute.
/// </summary>
public static class AreaDurations
{
    /// <summary>The most runs one area and model is measured over.</summary>
    public const int WindowRuns = 30;

    /// <summary>Runs older than this many days give no figures.</summary>
    public const int WindowDays = 30;

    /// <summary>The least runs a group needs before its figures mean anything.</summary>
    public const int MinimumRuns = 3;

    // The results that mean the card finished: a stopped, killed or quota run says nothing about
    // how long a card really takes.
    private static readonly string[] FinishedWords = ["done", "review"];

    /// <summary>
    /// The finished runs of accounting.csv's own lines (ACCT-1), oldest first as the file keeps
    /// them. A row counts only when its card finished (done or review), it names a card, an area
    /// and a model, its minutes read as a number, and it finished within the last
    /// <paramref name="days"/> days. A row from before the model column (ACCT-4) is no model's
    /// work, and a line the header does not describe is left out rather than guessed at.
    /// </summary>
    public static IReadOnlyList<AreaRun> RunsFromRows(IReadOnlyList<string> lines, DateTimeOffset now, int days = WindowDays)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var first = now.AddDays(-days);
        var runs = new List<AreaRun>();
        foreach (var row in AccountingRows.Parse(lines).Rows)
        {
            // EST-8: every column by its name, so the model is the model column, not the last field.
            if (!FinishedWords.Contains(row["result"], Text.Comparer))
            {
                continue;
            }

            if (row["card"].Length == 0 || row["lane"].Length == 0 || row["model"].Length == 0 || row.Finished < first)
            {
                continue;
            }

            runs.Add(new AreaRun { Area = row["lane"], Model = row["model"], Minutes = row.Minutes, Finished = row.Finished });
        }

        return runs;
    }

    /// <summary>
    /// The typical length of each area and model's finished runs: at most
    /// <see cref="WindowRuns"/> of its newest runs, taken over minutes sorted up. Groups come back
    /// in area then model order; a group with too few runs gives no figures at all.
    /// </summary>
    public static IReadOnlyList<AreaTypical> Typical(IReadOnlyList<AreaRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var byGroup = new Dictionary<string, List<AreaRun>>(Text.Comparer);
        foreach (var run in runs)
        {
            var key = run.Area + "\0" + run.Model;
            if (!byGroup.TryGetValue(key, out var group))
            {
                group = [];
                byGroup[key] = group;
            }

            group.Add(run);
        }

        var typicals = new List<AreaTypical>(byGroup.Count);
        foreach (var group in byGroup.Values)
        {
            // The window is the newest runs, not the file's: a card that used to take an hour on a
            // model that has since got faster should not set today's suggestion.
            var minutes = group
                .OrderByDescending(r => r.Finished)
                .Take(WindowRuns)
                .Select(r => r.Minutes)
                .OrderBy(m => m)
                .ToList();
            if (minutes.Count < MinimumRuns)
            {
                continue;
            }

            typicals.Add(new AreaTypical
            {
                Area = group[0].Area,
                Model = group[0].Model,
                Runs = minutes.Count,
                Median = Median(minutes),
                P90 = Percentile(minutes, 0.9),
            });
        }

        return typicals.OrderBy(t => t.Area, Text.SortComparer).ThenBy(t => t.Model, Text.SortComparer).ToList();
    }

    /// <summary>The figures for one area and model, or null when there are none; text compares as ever.</summary>
    public static AreaTypical? For(IReadOnlyList<AreaTypical> typicals, string area, string model)
    {
        ArgumentNullException.ThrowIfNull(typicals);
        return area.Length == 0 || model.Length == 0
            ? null
            : typicals.FirstOrDefault(t => Text.Same(t.Area, area) && Text.Same(t.Model, model));
    }

    // The middle run; the mean of the two middles when the count is even, rounded up on a half.
    internal static int Median(List<int> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (int)Math.Round((sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0, MidpointRounding.AwayFromZero);

    // Nearest rank: the smallest minutes value that at least p of the runs finished within.
    internal static int Percentile(List<int> sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Count) - 1];
}
