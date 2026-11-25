// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Estimates;

/// <summary>EST-33: the units line of the document.</summary>
public sealed record EstimateUnits(string Time, string Share);

/// <summary>EST-33: the window and the fewest runs the report used (EST-9, EST-10).</summary>
public sealed record EstimateWindow(int Days, int NewestRuns, int MinRunsMedian, int MinRunsP90, int MinRunsShare);

/// <summary>EST-33: how many rows the report read: the project's, the pool's, and the lines skipped (EST-8).</summary>
public sealed record EstimateRows(int Project, int Pool, int Skipped);

/// <summary>A figure as the document gives it: whole minutes or null, where it comes from (project, pool or none) and the runs it rests on.</summary>
public sealed record FigureJson(int? Minutes, string Source, int Runs);

/// <summary>A <c>runTime</c> entry (EST-14): either <see cref="Kind"/> or <see cref="Category"/>, never both.</summary>
public sealed record RunTimeJson(
    string Worker,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    FigureJson Median,
    FigureJson P90,
    string From,
    string To);

/// <summary>A <c>review</c> entry (EST-20).</summary>
public sealed record ReviewJson(string Kind, FigureJson Median, FigureJson P90, int ToDone, int ToQueue);

/// <summary>An <c>outcomes</c> entry (EST-22, EST-23): the whole entry has one source and one run count.</summary>
public sealed record OutcomeJson(
    string Worker,
    string Source,
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

/// <summary>One source of rows for the report: a project's accounting rows, the lines skipped reading them, and its review stays.</summary>
public sealed record EstimateSource(IReadOnlyList<ParsedAccountingRow> Rows, int Skipped, ReviewStaySource Stays);

/// <summary>
/// The document of design section 3 (EST-33), built from rows (EST-9 to EST-26) and written as JSON with
/// <see cref="ToJson"/>. Field names are camelCase and fixed; new fields are only ever added.
/// </summary>
public sealed record EstimatesReport(
    int Schema,
    string? Project,
    string Written,
    string Scheduler,
    EstimateUnits Units,
    EstimateWindow Window,
    EstimateRows Rows,
    string Help,
    IReadOnlyList<RunTimeJson> RunTime,
    IReadOnlyList<ReviewJson> Review,
    ReviewOpen ReviewOpen,
    IReadOnlyList<OutcomeJson> Outcomes)
{
    /// <summary>EST-33: the schema number; it becomes 2 only if a field changes meaning.</summary>
    public const int SchemaNumber = 1;

    /// <summary>The <c>help</c> text of the document.</summary>
    public const string HelpText = "Figures are history, not promises. Each figure says where it comes from (project = this project's own runs, pool = every project in the pool, none = too few runs anywhere) and how many runs it rests on. A group named category:<L> holds runs of cards that had no Kind: line.";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The document as JSON (EST-33).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads a document written by <see cref="ToJson"/>; null when the text is not one.</summary>
    public static EstimatesReport? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<EstimatesReport>(json, Options);
    }

    /// <summary>
    /// A project's report: its own figures, falling back per figure to the pool (EST-25). <paramref name="pool"/> holds
    /// every member project's source, this one included (EST-27); null is a scheduler run for one project alone, with no
    /// pool and no <c>pool</c> source. <paramref name="open"/> is the project's open review stays.
    /// </summary>
    public static EstimatesReport ForProject(
        string? project,
        EstimateSource own,
        IReadOnlyList<EstimateSource>? pool,
        IReadOnlyList<ReviewStay> open,
        DateTimeOffset now,
        string scheduler)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(open);

        var poolRows = pool?.Select(s => s.Rows).ToList();
        var runTime = RunTimeTable.Build(own.Rows, poolRows, now);
        var review = ReviewStays.Table(own.Stays, pool?.Select(s => s.Stays).ToList(), open, now);
        var outcomes = OutcomeTable.Build(own.Rows, poolRows, now);
        return Make(
            project,
            now,
            scheduler,
            new EstimateRows(own.Rows.Count, pool?.Sum(s => s.Rows.Count) ?? 0, own.Skipped),
            runTime,
            review,
            outcomes,
            toPool: false);
    }

    /// <summary>
    /// The pool's report (EST-31): the same shape built from the pool's rows alone, <c>project</c> null and every source
    /// <c>pool</c>. Each member's rows stay one source; the open stays are those of the pool's members, given by the caller.
    /// </summary>
    public static EstimatesReport ForPool(
        IReadOnlyList<EstimateSource> pool,
        IReadOnlyList<ReviewStay> open,
        DateTimeOffset now,
        string scheduler)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(open);

        var rows = pool.SelectMany(s => s.Rows).ToList();
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in pool)
        {
            foreach (var (card, kind) in source.Stays.Kinds)
            {
                _ = kinds.TryAdd(card, kind);
            }
        }

        var merged = new ReviewStaySource([.. pool.SelectMany(s => s.Stays.Stays)], kinds);
        return Make(
            null,
            now,
            scheduler,
            new EstimateRows(0, rows.Count, pool.Sum(s => s.Skipped)),
            RunTimeTable.Build(rows, null, now),
            ReviewStays.Table(merged, null, open, now),
            OutcomeTable.BuildPool([.. pool.Select(s => s.Rows)], now),
            toPool: true);
    }

    private static EstimatesReport Make(
        string? project,
        DateTimeOffset now,
        string scheduler,
        EstimateRows rows,
        IReadOnlyList<RunTimeGroup> runTime,
        ReviewTable review,
        IReadOnlyList<OutcomeGroup> outcomes,
        bool toPool)
    {
        return new EstimatesReport(
            SchemaNumber,
            project,
            Time(now),
            scheduler,
            new EstimateUnits("minutes", "percent"),
            new EstimateWindow(
                EstimateFigures.WindowDays,
                EstimateFigures.NewestRuns,
                EstimateFigures.MinRunsMedian,
                EstimateFigures.MinRunsP90,
                EstimateFigures.MinRunsShare),
            rows,
            HelpText,
            [.. runTime.Select(g => new RunTimeJson(
                g.Worker,
                g.Kind.Length > 0 ? g.Kind : null,
                g.Kind.Length > 0 ? null : g.Category,
                Json(g.Median, toPool),
                Json(g.P90, toPool),
                Time(g.From),
                Time(g.To)))],
            [.. review.Groups.Select(g => new ReviewJson(g.Kind, Json(g.Median, toPool), Json(g.P90, toPool), g.ToDone, g.ToQueue))],
            review.Open,
            [.. outcomes.Select(g => new OutcomeJson(
                g.Worker,
                SourceName(g.Source, toPool),
                g.Runs,
                g.Done,
                g.Review,
                g.Question,
                g.Planner,
                g.Yielded,
                g.TimeLimit,
                g.Stopped,
                g.Failed,
                g.OutOfQuota,
                g.DidNotStart,
                g.NotCounted,
                g.Cards,
                g.MoreThanOneRun,
                Math.Round(g.RunsPerCard, 1)))]);
    }

    private static FigureJson Json(Figure figure, bool toPool) => new(figure.Minutes, SourceName(figure.Source, toPool), figure.Runs);

    // In the pool's own report every figure that has a source is the pool's (EST-31).
    private static string SourceName(FigureSource source, bool toPool) => source switch
    {
        FigureSource.None => "none",
        FigureSource.Pool => "pool",
        _ => toPool ? "pool" : "project",
    };

    private static string Time(DateTimeOffset t) => t.ToString(TimeFormat, CultureInfo.InvariantCulture);
}
