namespace Scheduler.Core.Views;

/// <summary>How the Gantt groups a project's cards.</summary>
public enum GanttGroupBy
{
    Category,
    Plan,
}

public enum GanttRowKind
{
    Project,
    Category,
    Plan,
    Step,
    Group,
    Card,
}

/// <summary>One row of the Gantt: a project, a group (category, plan, step, Bugs, Requests, Other cards) or a card.</summary>
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

/// <summary>The Gantt's rows for one project (pure): grouped by category or by plan, cut to a time window.</summary>
public static class GanttRows
{
    /// <summary>
    /// The project row with its groups and card rows. A card is listed when it has a run or a running bar inside
    /// [from, to], or is in active/; a group with no listed card is dropped.
    /// </summary>
    public static GanttRow Of(ViewsSnapshot snapshot, GanttGroupBy groupBy, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var rows = new Dictionary<string, GanttRow>(StringComparer.Ordinal);
        foreach (var card in snapshot.Cards)
        {
            var bars = GanttBars.ForCard(snapshot.Runs, card.Id, from, to, now);
            if (bars.Count == 0 && card.Folder != ViewFolder.Active)
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

        var children = groupBy == GanttGroupBy.Category ? ByCategory(snapshot, rows) : ByPlan(snapshot, rows);
        return Group(GanttRowKind.Project, "project:" + snapshot.Project, snapshot.Project, children);
    }

    private static List<GanttRow> ByCategory(ViewsSnapshot snapshot, Dictionary<string, GanttRow> rows) =>
        snapshot.Cards
            .Where(c => rows.ContainsKey(c.Id))
            .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => Group(GanttRowKind.Category, "category:" + g.Key, g.Key, g.Select(c => rows[c.Id]).ToList()))
            .ToList();

    private static List<GanttRow> ByPlan(ViewsSnapshot snapshot, Dictionary<string, GanttRow> rows)
    {
        var onMain = new HashSet<string>(snapshot.Plans.Select(p => p.Path), StringComparer.Ordinal);
        var titles = snapshot.Plans.GroupBy(p => p.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);
        var result = new List<GanttRow>();
        foreach (var plan in PlanTree.Of(snapshot.Cards, onMain, snapshot.PlanTexts))
        {
            if (plan.Plan.Kind == PlanKind.NoPlan)
            {
                // Cards with no plan: Bugs (type bug) and Requests (the rest), directly under the project.
                var own = Listed(plan.Other?.Cards ?? [], rows);
                AddIfAny(result, Group(GanttRowKind.Group, "bugs", "Bugs", own.Where(r => IsBug(r.Card!)).ToList()));
                AddIfAny(result, Group(GanttRowKind.Group, "requests", "Requests", own.Where(r => !IsBug(r.Card!)).ToList()));
                continue;
            }

            var key = "plan:" + plan.Plan.Path;
            var kids = plan.Steps.Select(s => StepRow(key, s, rows)).OfType<GanttRow>().ToList();
            if (plan.Other is { } other)
            {
                AddIfAny(kids, Group(GanttRowKind.Group, key + "/other", "Other cards", Listed(other.Cards, rows)));
            }

            var label = titles.GetValueOrDefault(plan.Plan.Path) is { Length: > 0 } t ? t : plan.Plan.Path;
            AddIfAny(result, Group(GanttRowKind.Plan, key, label, kids));
        }

        return result;
    }

    private static GanttRow? StepRow(string parentKey, PlanStepNode node, Dictionary<string, GanttRow> rows)
    {
        var step = node.Step!;
        var key = parentKey + "/" + step.Label;
        var kids = Listed(node.Cards, rows);
        kids.AddRange(node.SubSteps.Select(s => StepRow(key, s, rows)).OfType<GanttRow>());
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
