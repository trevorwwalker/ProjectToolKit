// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Estimates;

/// <summary>One outcome of Table 3: how many runs, and their whole-percent share (null below the fewest runs, EST-24).</summary>
public sealed record OutcomeShare(int Count, int? Share);

/// <summary>
/// One line of Table 3 (EST-22, EST-23): a worker type's outcomes. <see cref="Source"/> and
/// <see cref="Runs"/> apply to the whole line, because shares are only worth reading together; the counts,
/// the cards and the runs per card all come from that same source.
/// </summary>
public sealed record OutcomeGroup(
    string Worker,
    FigureSource Source,
    int Runs,
    OutcomeShare Done,
    OutcomeShare Review,
    OutcomeShare Question,
    OutcomeShare Planner,
    OutcomeShare Yielded,
    OutcomeShare TimeLimit,
    OutcomeShare Stopped,
    OutcomeShare Failed,
    int OutOfQuota,
    int DidNotStart,
    int NotCounted,
    int Cards,
    int MoreThanOneRun,
    double RunsPerCard);

/// <summary>
/// Table 3, outcomes by worker type (EST-21 to EST-24, EST-25). Rows in, groups out: no clock and no
/// files. The pool's rows are every member project's rows, the project's own included, each project's
/// kept apart so that card 12-A of two projects is two cards (EST-27).
/// </summary>
public static class OutcomeTable
{
    // EST-22: not the worker type's doing, so not in `runs`.
    private static bool IsAside(RunOutcome o) => o is RunOutcome.NotCounted or RunOutcome.DidNotStart or RunOutcome.OutOfQuota;

    /// <summary>
    /// A line for each worker type with a row in the window. A worker type with at least
    /// <see cref="EstimateFigures.MinRunsShare"/> runs in the project gives the project's own figures;
    /// with fewer, the pool's when it has that many; else the project's counts with no shares and source
    /// none. A null <paramref name="pool"/> is a scheduler run for one project alone: no pool figure.
    /// </summary>
    public static IReadOnlyList<OutcomeGroup> Build(
        IReadOnlyList<ParsedAccountingRow> project,
        IReadOnlyList<IReadOnlyList<ParsedAccountingRow>>? pool,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);

        var own = Tally([project], now);
        var pooled = pool is null ? null : Tally(pool, now);
        var groups = new List<OutcomeGroup>(own.Count);
        foreach (var (key, mine) in own)
        {
            var source = FigureSource.None;
            var use = mine;
            if (mine.Runs >= EstimateFigures.MinRunsShare)
            {
                source = FigureSource.Project;
            }
            else if (pooled is not null && pooled.TryGetValue(key, out var theirs) && theirs.Runs >= EstimateFigures.MinRunsShare)
            {
                source = FigureSource.Pool;
                use = theirs;
            }

            groups.Add(Group(mine.Worker, source, use));
        }

        return [.. groups.OrderBy(g => g.Worker, Text.SortComparer)];
    }

    /// <summary>
    /// The pool's own lines (EST-31): every worker type of the pool's rows, each project's rows kept apart so that card 12-A of
    /// two projects is two cards (EST-27). A worker type with at least <see cref="EstimateFigures.MinRunsShare"/> runs in the
    /// pool has source pool, else none and no shares.
    /// </summary>
    public static IReadOnlyList<OutcomeGroup> BuildPool(IReadOnlyList<IReadOnlyList<ParsedAccountingRow>> pool, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pool);
        return [.. Tally(pool, now).Values
            .Select(c => Group(c.Worker, c.Runs >= EstimateFigures.MinRunsShare ? FigureSource.Pool : FigureSource.None, c))
            .OrderBy(g => g.Worker, Text.SortComparer)];
    }

    private static OutcomeGroup Group(string worker, FigureSource source, Counts c)
    {
        OutcomeShare Share(RunOutcome o) => new(
            c.Of[o],
            source == FigureSource.None || c.Runs == 0
                ? null
                : (int)Math.Round(c.Of[o] * 100.0 / c.Runs, MidpointRounding.AwayFromZero));

        return new OutcomeGroup(
            worker,
            source,
            c.Runs,
            Share(RunOutcome.Done),
            Share(RunOutcome.Review),
            Share(RunOutcome.Question),
            Share(RunOutcome.Planner),
            Share(RunOutcome.Yielded),
            Share(RunOutcome.TimeLimit),
            Share(RunOutcome.Stopped),
            Share(RunOutcome.Failed),
            c.Of[RunOutcome.OutOfQuota],
            c.Of[RunOutcome.DidNotStart],
            c.Of[RunOutcome.NotCounted],
            c.Cards,
            c.MoreThanOne,
            c.Cards == 0 ? 0 : Math.Round(c.AttemptsTotal / (double)c.Cards, 1, MidpointRounding.AwayFromZero));
    }

    // One row with what the tables need to know of it.
    private sealed class Entry
    {
        public required ParsedAccountingRow Row { get; init; }

        public required int Index { get; init; }

        public required string Worker { get; init; }

        public required RunOutcome Outcome { get; init; }

        // For a finishing run (done or review): the runs of its card it took, 1 or more (EST-23).
        public int Attempts { get; set; } = 1;
    }

    private sealed class Counts
    {
        public required string Worker { get; init; }

        public Dictionary<RunOutcome, int> Of { get; } = Enum.GetValues<RunOutcome>().ToDictionary(o => o, _ => 0);

        public int Runs { get; set; }

        public int Cards { get; set; }

        public int MoreThanOne { get; set; }

        public int AttemptsTotal { get; set; }
    }

    // Worker key -> its counts over the window, for these projects' rows together.
    private static Dictionary<string, Counts> Tally(IReadOnlyList<IReadOnlyList<ParsedAccountingRow>> projects, DateTimeOffset now)
    {
        var all = new List<Entry>();
        foreach (var rows in projects)
        {
            var entries = rows.Select((row, i) => new Entry
            {
                Row = row,
                Index = i,
                Worker = EstimateFigures.WorkerOf(row),
                Outcome = RunOutcomes.Of(row["result"], row["stop_reason"], row.Minutes),
            }).ToList();
            CountAttempts(entries);
            all.AddRange(entries);
        }

        var first = now.AddDays(-EstimateFigures.WindowDays);
        var result = new Dictionary<string, Counts>(Text.Comparer);
        foreach (var byWorker in all.Where(e => e.Row.Finished >= first).GroupBy(e => e.Worker, Text.Comparer))
        {
            // EST-9: the newest runs that count; the runs set aside are counted from the oldest of them on.
            var counted = EstimateFigures.Window(byWorker.Where(e => !IsAside(e.Outcome)), e => e.Row.Finished, now);
            var from = counted.Count > 0 ? counted.Min(e => e.Row.Finished) : first;
            var counts = new Counts { Worker = byWorker.OrderByDescending(e => e.Row.Finished).First().Worker, Runs = counted.Count };
            foreach (var e in counted)
            {
                counts.Of[e.Outcome]++;
                if (e.Outcome is RunOutcome.Done or RunOutcome.Review)
                {
                    counts.Cards++;
                    counts.AttemptsTotal += e.Attempts;
                    counts.MoreThanOne += e.Attempts > 1 ? 1 : 0;
                }
            }

            foreach (var e in byWorker.Where(e => IsAside(e.Outcome) && e.Row.Finished >= from))
            {
                counts.Of[e.Outcome]++;
            }

            result[byWorker.Key] = counts;
        }

        return result;
    }

    // EST-23: per card of one project, in finish order, the runs since its last finish (any worker type, any
    // outcome but the ones set aside); a finishing run takes that count plus itself and starts the count again.
    private static void CountAttempts(List<Entry> entries)
    {
        foreach (var card in entries.Where(e => CardId(e.Row).Length > 0).GroupBy(e => CardId(e.Row), Text.Comparer))
        {
            var earlier = 0;
            foreach (var e in card.OrderBy(e => e.Row.Finished).ThenBy(e => e.Index))
            {
                if (IsAside(e.Outcome))
                {
                    continue;
                }

                if (e.Outcome is RunOutcome.Done or RunOutcome.Review)
                {
                    e.Attempts = earlier + 1;
                    earlier = 0;
                }
                else
                {
                    earlier++;
                }
            }
        }
    }

    /// <summary>The card's id, <c>&lt;n&gt;-&lt;L&gt;</c>, from the row's card column (its file name without .md); "" when none.</summary>
    internal static string CardId(ParsedAccountingRow row)
    {
        var card = row["card"].Trim();
        var parts = card.Split('-', 3);
        return parts.Length >= 2 ? string.Join('-', parts[0], parts[1]) : card;
    }
}
