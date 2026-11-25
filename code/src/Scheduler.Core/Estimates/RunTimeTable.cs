// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Estimates;

/// <summary>
/// One line of Table 1 (EST-14): the runs of one worker type and kind, or of one worker type and category
/// when the cards had no kind (EST-12). Exactly one of <see cref="Kind"/> and <see cref="Category"/> is not "".
/// </summary>
public sealed record RunTimeGroup(
    string Worker,
    string Kind,
    string Category,
    int Runs,
    Figure Median,
    Figure P90,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>
/// Table 1, run time by worker type and kind (EST-12 to EST-14). Rows in, groups out: no clock and no
/// files. The pool's rows are every member project's rows, the project's own included (EST-27).
/// </summary>
public static class RunTimeTable
{
    /// <summary>
    /// The groups of the project's own counted runs (EST-13: result done or review, minutes 0 or more),
    /// each over its window (EST-9). Each figure falls back to the pool's same group when the project has
    /// too few runs (EST-25), except a category group (EST-26). A null <paramref name="pool"/> is a
    /// scheduler run for one project alone: no figure is ever the pool's. Kind groups come first,
    /// by worker type then kind, and category groups last.
    /// </summary>
    public static IReadOnlyList<RunTimeGroup> Build(
        IReadOnlyList<ParsedAccountingRow> project,
        IReadOnlyList<IReadOnlyList<ParsedAccountingRow>>? pool,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);

        var own = Groups(project, now);
        Dictionary<string, List<int>>? pooled = null;
        if (pool is not null)
        {
            pooled = new Dictionary<string, List<int>>(Text.Comparer);
            foreach (var (key, runs) in Groups(pool.SelectMany(rows => rows), now))
            {
                pooled[key] = [.. runs.Select(r => r.Minutes)];
            }
        }

        var groups = new List<RunTimeGroup>(own.Count);
        foreach (var (key, runs) in own)
        {
            var newest = runs[0];
            var category = HasKind(newest) ? "" : CategoryOf(newest);
            List<int>? fallback = null;
            if (category.Length == 0)
            {
                pooled?.TryGetValue(key, out fallback);
            }

            var minutes = runs.Select(r => r.Minutes).ToList();
            groups.Add(new RunTimeGroup(
                EstimateFigures.WorkerOf(newest),
                category.Length == 0 ? newest["kind"].Trim().ToLowerInvariant() : "",
                category,
                runs.Count,
                EstimateFigures.Median(minutes, fallback),
                EstimateFigures.P90(minutes, fallback),
                runs.Min(r => r.Finished),
                runs.Max(r => r.Finished)));
        }

        return [.. groups
            .OrderBy(g => g.Category.Length > 0)
            .ThenBy(g => g.Worker, Text.SortComparer)
            .ThenBy(g => g.Kind, Text.SortComparer)
            .ThenBy(g => g.Category, Text.SortComparer)];
    }

    // EST-12, EST-13, EST-9: key -> the group's windowed counted runs, newest first.
    private static Dictionary<string, List<ParsedAccountingRow>> Groups(IEnumerable<ParsedAccountingRow> rows, DateTimeOffset now)
    {
        var byKey = new Dictionary<string, List<ParsedAccountingRow>>(Text.Comparer);
        foreach (var row in rows)
        {
            var outcome = RunOutcomes.Of(row["result"], row["stop_reason"], row.Minutes);
            if (outcome is not (RunOutcome.Done or RunOutcome.Review) || row.Minutes < 0)
            {
                continue;
            }

            var key = EstimateFigures.WorkerOf(row) + "\0" + KindKey(row);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
            }

            list.Add(row);
        }

        var windowed = new Dictionary<string, List<ParsedAccountingRow>>(Text.Comparer);
        foreach (var (key, list) in byKey)
        {
            var kept = EstimateFigures.Window(list, r => r.Finished, now);
            if (kept.Count > 0)
            {
                windowed[key] = [.. kept];
            }
        }

        return windowed;
    }

    // The group's kind, or category:<lane> when the row has none.
    private static string KindKey(ParsedAccountingRow row)
    {
        return HasKind(row) ? row["kind"].Trim() : EstimateFigures.CategoryPrefix + CategoryOf(row);
    }

    private static bool HasKind(ParsedAccountingRow row) => row["kind"].Trim().Length > 0;

    // The row's category letter (its lane column); "unknown" for a row with none.
    private static string CategoryOf(ParsedAccountingRow row) =>
        row["lane"].Trim().Length > 0 ? row["lane"].Trim() : "unknown";
}
