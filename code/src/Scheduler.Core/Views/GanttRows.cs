// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Views;

public enum GanttRowKind
{
    Project,
    Plan,
    Step,
    Group,
    Card,
}

/// <summary>One row of the Gantt: a project, a group (plan, step, Bugs, Requests, Other cards) or a card.</summary>
public sealed record GanttRow
{
    public required GanttRowKind Kind { get; init; }

    /// <summary>A key that is unique among the rows of one result, for folding.</summary>
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>The card, for a card row.</summary>
    public ViewCard? Card { get; init; }

    public IReadOnlyList<GanttRow> Children { get; init; } = [];

    /// <summary>A card row's bars (one per run in the window, plus a running bar); empty on a group row.</summary>
    public IReadOnlyList<GanttBar> Bars { get; init; } = [];

    /// <summary>A group row's one summary span over its children; null when none of them has a bar.</summary>
    public GanttBar? Summary { get; init; }

    /// <summary>Complete cards under this row (a card row: 1 or 0).</summary>
    public int Done { get; init; }

    /// <summary>Cards that count under this row: every card but the cancelled ones.</summary>
    public int Total { get; init; }
}

/// <summary>The Gantt's rows for one project (pure): grouped by plan, cut to a time window.</summary>
public static class GanttRows
{
    /// <summary>
    /// The project row with its groups and card rows. A card is listed when it has a run or a running bar inside
    /// [from, to], is in active/, or is in <paramref name="listed"/> (the cards with an estimate, VW-18: the
    /// unfinished ones); a group with no listed card is dropped.
    /// </summary>
    public static GanttRow Of(ViewsSnapshot snapshot, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, IReadOnlySet<string>? listed = null)
    {
        var rows = new Dictionary<string, GanttRow>(StringComparer.Ordinal);
        foreach (var card in snapshot.Cards)
        {
            var bars = GanttBars.ForCard(snapshot.Runs, card.Id, from, to, now);
            if (bars.Count == 0 && card.Folder != ViewFolder.Active && listed?.Contains(card.Id) != true)
            {
                continue;
            }

            rows[card.Id] = new GanttRow
            {
                Kind = GanttRowKind.Card,
                Key = "card:" + card.Id,
                Label = card.Id + (card.Title.Length == 0 ? "" : " " + card.Title),
                Card = card,
                Bars = bars,
                Done = CardMark.IsComplete(card) ? 1 : 0,
                Total = CardMark.IsCancelled(card) ? 0 : 1,
            };
        }

        var children = ByPlan(snapshot, rows);
        return Group(GanttRowKind.Project, "project:" + snapshot.Project, snapshot.Project, children);
    }

    private static List<GanttRow> ByPlan(ViewsSnapshot snapshot, Dictionary<string, GanttRow> rows)
    {
        var titles = snapshot.Plans.GroupBy(p => p.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);
        var result = new List<GanttRow>();
        foreach (var plan in PlanTree.Of(snapshot).Top)
        {
            if (plan.Plan.Kind == PlanKind.NoPlan)
            {
                // Cards with no plan: Bugs (type bug) and Requests (the rest), directly under the project.
                var own = Listed(plan.Other?.Cards ?? [], rows);
                AddIfAny(result, Group(GanttRowKind.Group, "bugs", "Bugs", own.Where(r => IsBug(r.Card!)).ToList()));
                AddIfAny(result, Group(GanttRowKind.Group, "requests", "Requests", own.Where(r => !IsBug(r.Card!)).ToList()));
                continue;
            }

            if (PlanRow(plan, rows, titles) is { } row)
            {
                result.Add(row);
            }
        }

        return result;
    }

    // One plan's row (VW-32): its steps (each with its cards, child plans, sub-steps) and its Other cards; null when nothing under it is listed.
    private static GanttRow? PlanRow(PlanNode plan, Dictionary<string, GanttRow> rows, IReadOnlyDictionary<string, string> titles)
    {
        var key = "plan:" + plan.Plan.Path;
        var kids = plan.Steps.Select(s => StepRow(key, s, rows, titles)).OfType<GanttRow>().ToList();
        if (plan.Other is { } other)
        {
            AddIfAny(kids, Group(GanttRowKind.Group, key + "/other", "Other cards", Listed(other.Cards, rows)));
        }

        if (kids.Count == 0)
        {
            return null;
        }

        var label = titles.GetValueOrDefault(plan.Plan.Path) is { Length: > 0 } t ? t : plan.Plan.Path;
        if (plan.FlagText.Length > 0)
        {
            label += " (" + plan.FlagText + ")";
        }

        return Group(GanttRowKind.Plan, key, label, kids);
    }

    private static GanttRow? StepRow(string parentKey, PlanStepNode node, Dictionary<string, GanttRow> rows, IReadOnlyDictionary<string, string> titles)
    {
        var step = node.Step!;
        var key = parentKey + "/" + step.Label;
        var kids = Listed(node.Cards, rows);
        kids.AddRange(node.Plans.Select(p => PlanRow(p, rows, titles)).OfType<GanttRow>());
        kids.AddRange(node.SubSteps.Select(s => StepRow(key, s, rows, titles)).OfType<GanttRow>());
        return kids.Count == 0 ? null : Group(GanttRowKind.Step, key, step.Label + " " + step.Title, kids);
    }

    private static List<GanttRow> Listed(IEnumerable<ViewCard> cards, Dictionary<string, GanttRow> rows) =>
        cards.Where(c => rows.ContainsKey(c.Id)).Select(c => rows[c.Id]).ToList();

    private static bool IsBug(ViewCard card) => string.Equals(card.CardType, "bug", StringComparison.OrdinalIgnoreCase);

    private static void AddIfAny(List<GanttRow> list, GanttRow row)
    {
        if (row.Children.Count > 0)
        {
            list.Add(row);
        }
    }

    private static GanttRow Group(GanttRowKind kind, string key, string label, IReadOnlyList<GanttRow> children) =>
        new()
        {
            Kind = kind,
            Key = key,
            Label = label,
            Children = children,
            Summary = GanttBars.Summarise(children.SelectMany(c => c.Kind == GanttRowKind.Card ? c.Bars : c.Summary is { } s ? [s] : [])),
            Done = children.Sum(c => c.Done),
            Total = children.Sum(c => c.Total),
        };
}
