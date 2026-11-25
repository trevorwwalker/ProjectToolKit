// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Estimates;

/// <summary>Where a figure comes from (EST-25).</summary>
public enum FigureSource
{
    /// <summary>Too few runs anywhere: the figure is null.</summary>
    None,

    /// <summary>The project's own runs.</summary>
    Project,

    /// <summary>Every project in the pool.</summary>
    Pool,
}

/// <summary>
/// One figure of a table: whole minutes (null when no source had enough runs), where it came from, and
/// how many runs it rests on. With source <see cref="FigureSource.None"/> the count is the project's own,
/// which was too few (EST-25).
/// </summary>
public sealed record Figure(int? Minutes, FigureSource Source, int Runs);

/// <summary>
/// The window, the fewest runs and the fallback from the project to the pool, shared by the tables
/// (EST-9 to EST-11, EST-25, EST-26). Pure: the time is passed in.
/// </summary>
public static class EstimateFigures
{
    /// <summary>EST-9: a group's runs older than this many days are out.</summary>
    public const int WindowDays = 60;

    /// <summary>EST-9: a group keeps at most its newest this many runs.</summary>
    public const int NewestRuns = 100;

    /// <summary>EST-10: the fewest runs for a median.</summary>
    public const int MinRunsMedian = 3;

    /// <summary>EST-10: the fewest runs for a 90th percentile.</summary>
    public const int MinRunsP90 = 10;

    /// <summary>EST-10: the fewest runs for a share.</summary>
    public const int MinRunsShare = 10;

    /// <summary>The name of a worker type no row names (EST-12).</summary>
    public const string OtherWorker = "other";

    /// <summary>The prefix of the key of a group of cards with no kind (EST-12).</summary>
    public const string CategoryPrefix = "category:";

    /// <summary>
    /// EST-9: the items that finished within the last <see cref="WindowDays"/> days, newest first, at most
    /// <see cref="NewestRuns"/> of them. A tie keeps the order the items came in.
    /// </summary>
    public static IReadOnlyList<T> Window<T>(IEnumerable<T> items, Func<T, DateTimeOffset> finished, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(finished);

        var first = now.AddDays(-WindowDays);
        return items.Where(i => finished(i) >= first).OrderByDescending(finished).Take(NewestRuns).ToList();
    }

    /// <summary>EST-12: a row's worker type: its <c>type</c> column, else its <c>model</c>, else <c>other</c>.</summary>
    public static string WorkerOf(ParsedAccountingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var type = row["type"].Trim();
        if (type.Length > 0)
        {
            return type;
        }

        var model = row["model"].Trim();
        return model.Length > 0 ? model : OtherWorker;
    }

    /// <summary>EST-25: the project's median, else the pool's, else none. A null pool, or no pool allowed (EST-26), never falls back.</summary>
    public static Figure Median(IReadOnlyList<int> project, IReadOnlyList<int>? pool) =>
        Choose(project, pool, MinRunsMedian, sorted => AreaDurations.Median(sorted));

    /// <summary>EST-25: the project's 90th percentile (nearest rank), else the pool's, else none.</summary>
    public static Figure P90(IReadOnlyList<int> project, IReadOnlyList<int>? pool) =>
        Choose(project, pool, MinRunsP90, sorted => AreaDurations.Percentile(sorted, 0.9));

    private static Figure Choose(IReadOnlyList<int> project, IReadOnlyList<int>? pool, int least, Func<List<int>, int> measure)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Count >= least)
        {
            return new Figure(measure([.. project.Order()]), FigureSource.Project, project.Count);
        }

        return pool is not null && pool.Count >= least
            ? new Figure(measure([.. pool.Order()]), FigureSource.Pool, pool.Count)
            : new Figure(null, FigureSource.None, project.Count);
    }
}
