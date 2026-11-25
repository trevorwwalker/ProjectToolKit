// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Security.Cryptography;
using System.Text;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Core.Views;

/// <summary>The folder a card is in, as the views see it (done/ and archive/ are two).</summary>
public enum ViewFolder
{
    Queue,
    Active,
    Review,
    Done,
    Archive,
}

/// <summary>One card of the views snapshot (views.md section 1): pure data, no I/O.</summary>
public sealed record ViewCard
{
    public required string Id { get; init; }

    public required long Number { get; init; }

    public required string Category { get; init; }

    public string Title { get; init; } = "";

    public required ViewFolder Folder { get; init; }

    public string Status { get; init; } = "";

    public string CardType { get; init; } = "";

    public string PlanPath { get; init; } = "";

    public string PlanStep { get; init; } = "";

    /// <summary>The Priority line: 0 high, 1 normal, 2 low (CARD-8); the Gantt estimate's dispatch order (VW-19).</summary>
    public int Priority { get; init; } = 1;

    /// <summary>The Worker line, empty when the card has none.</summary>
    public string Worker { get; init; } = "";

    /// <summary>The card ids of the Depends on line (empty for "none").</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>The Time box in minutes, null when the card has none.</summary>
    public int? TimeBoxMinutes { get; init; }

    /// <summary>The paths of the Scope line, split on commas and trimmed.</summary>
    public IReadOnlyList<string> Scope { get; init; } = [];

    /// <summary>The paths of a Scope line: comma-separated, trimmed, empty parts dropped.</summary>
    public static IReadOnlyList<string> SplitScope(string? line) =>
        line is null ? [] : [.. line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>An open card (queue/, active/, review/) as the views' card.</summary>
    public static ViewCard From(Card card) => new()
    {
        Id = card.Id,
        Number = card.Num,
        Category = card.Category,
        Title = card.Title,
        Folder = card.Folder switch
        {
            CardFolder.Queue => ViewFolder.Queue,
            CardFolder.Active => ViewFolder.Active,
            CardFolder.Review => ViewFolder.Review,
            _ => ViewFolder.Done,
        },
        Status = card.Status,
        Priority = card.Prio,
        CardType = card.CardType,
        PlanPath = card.PlanPath,
        PlanStep = card.PlanStep,
        Worker = card.WorkerReq ?? "",
        DependsOn = card.Depends,
        TimeBoxMinutes = card.TimeBox,
        Scope = SplitScope(card.ScopeLine),
    };

    /// <summary>A done/ or archive/ card's header (IMainModel.DoneCards) as the views' card.</summary>
    public static ViewCard From(DoneCard card) => new()
    {
        Id = card.Id,
        Number = card.Num,
        Category = card.Category,
        Title = card.Title,
        Folder = card.Folder.Equals("archive", StringComparison.OrdinalIgnoreCase) ? ViewFolder.Archive : ViewFolder.Done,
        Status = card.Status,
        CardType = card.CardType,
        PlanPath = card.PlanPath,
        PlanStep = card.PlanStep,
        Worker = card.WorkerReq ?? "",
        DependsOn = card.Depends,
        TimeBoxMinutes = card.TimeBox,
        Scope = SplitScope(card.ScopeLine),
    };
}

/// <summary>A plan the Plans view lists: where it is, its title and its status line (VW-11, VW-13).</summary>
public sealed record ViewPlan
{
    /// <summary>The resolved path, or the name for a roadmap item.</summary>
    public required string Path { get; init; }

    public required PlanKind Kind { get; init; }

    public string Title { get; init; } = "";

    public string Status { get; init; } = "";
}

/// <summary>One run of a card: when it started and ended, its result and who ran it (the Gantt's bar).</summary>
public sealed record ViewRun
{
    public required string CardId { get; init; }

    public required DateTimeOffset Start { get; init; }

    /// <summary>Null while the run is still going.</summary>
    public DateTimeOffset? End { get; init; }

    /// <summary>The run's result word (done, review, needs-human, waiting, failed, stopped, yielded); empty while running.</summary>
    public string Result { get; init; } = "";

    public string Model { get; init; } = "";

    public string WorkerType { get; init; } = "";

    public string Slot { get; init; } = "";

    /// <summary>For a running run: when its time box ends, if the card has one.</summary>
    public DateTimeOffset? TimeBoxEnd { get; init; }

    public bool Running => End is null;
}

/// <summary>Per project: the cards, the plans and a version hash of both (views.md section 1).</summary>
public sealed record ViewsSnapshot
{
    public required string Project { get; init; }

    public IReadOnlyList<ViewCard> Cards { get; init; } = [];

    public IReadOnlyList<ViewPlan> Plans { get; init; } = [];

    /// <summary>Every run of the project's cards, and the running ones (<see cref="ViewRun.End"/> null).</summary>
    public IReadOnlyList<ViewRun> Runs { get; init; } = [];

    /// <summary>The plan documents' text by path (a plan's steps are its headings), for <see cref="PlanTree"/>.</summary>
    public IReadOnlyDictionary<string, string> PlanTexts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The free-slot cap for the Gantt estimate: the project's workspaces capped by the pool's maxWorkers (VW-19).</summary>
    public int Workers { get; init; } = 1;

    /// <summary>Each category's typical run length per model, for the estimate bars (VW-19).</summary>
    public IReadOnlyList<AreaTypical> Typicals { get; init; } = [];

    /// <summary>The marks the developer set on plans and steps, from jobs/plan-marks.json (1125-C).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> Marks { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, ManualMark>>(StringComparer.Ordinal);

    /// <summary>A hash of the cards, plans and plan texts: it moves exactly when something the views draw does.</summary>
    public string Version { get; init; } = "";

    public static ViewsSnapshot Build(string project, IEnumerable<ViewCard> cards, IEnumerable<ViewPlan> plans, IReadOnlyDictionary<string, string>? planTexts = null, IEnumerable<ViewRun>? runs = null, int workers = 1, IReadOnlyList<AreaTypical>? typicals = null, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks = null)
    {
        var cardList = cards.OrderBy(c => c.Number).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var planList = plans.OrderBy(p => p.Path, StringComparer.Ordinal).ToList();
        var runList = (runs ?? []).OrderBy(r => r.Start).ThenBy(r => r.CardId, StringComparer.Ordinal).ToList();
        var typicalList = typicals ?? [];
        return new ViewsSnapshot
        {
            Workers = workers,
            Typicals = typicalList,
            Project = project,
            Runs = runList,
            Cards = cardList,
            Plans = planList,
            PlanTexts = planTexts ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Marks = marks ?? new Dictionary<string, IReadOnlyDictionary<string, ManualMark>>(StringComparer.Ordinal),
            Version = Hash(project, cardList, planList, planTexts, runList, workers, typicalList, marks),
        };
    }

    private static string Hash(string project, List<ViewCard> cards, List<ViewPlan> plans, IReadOnlyDictionary<string, string>? texts, List<ViewRun> runs, int workers, IReadOnlyList<AreaTypical> typicals, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks)
    {
        var sb = new StringBuilder(project).Append('\n');
        foreach (var c in cards)
        {
            sb.Append(c.Id).Append('|').Append(c.Number).Append('|').Append(c.Category).Append('|').Append(c.Title)
              .Append('|').Append((int)c.Folder).Append('|').Append(c.Status).Append('|').Append(c.CardType)
              .Append('|').Append(c.PlanPath).Append('|').Append(c.PlanStep).Append('|').Append(c.Worker)
              .Append('|').Append(string.Join(',', c.DependsOn)).Append('|').Append(c.TimeBoxMinutes).Append('|').Append(string.Join(',', c.Scope)).Append('|').Append(c.Priority).Append('\n');
        }

        sb.Append("--\n");
        foreach (var p in plans)
        {
            sb.Append(p.Path).Append('|').Append((int)p.Kind).Append('|').Append(p.Title).Append('|').Append(p.Status).Append('\n');
        }

        foreach (var t in (texts ?? new Dictionary<string, string>()).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            sb.Append("--\n").Append(t.Key).Append('|').Append(t.Value);
        }

        foreach (var r in runs)
        {
            sb.Append("--run\n").Append(r.CardId).Append('|').Append(r.Start.UtcTicks).Append('|').Append(r.End?.UtcTicks).Append('|')
              .Append(r.Result).Append('|').Append(r.Model).Append('|').Append(r.WorkerType).Append('|').Append(r.Slot).Append('|')
              .Append(r.TimeBoxEnd?.UtcTicks).Append('\n');
        }

        sb.Append("--workers\n").Append(workers).Append('\n');
        foreach (var t in typicals)
        {
            sb.Append("--typical\n").Append(t).Append('\n');
        }

        if (marks is { Count: > 0 })
        {
            sb.Append("--marks\n").Append(PlanMarkFile.Write(marks));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }
}
