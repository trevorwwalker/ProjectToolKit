using System.Text.RegularExpressions;

namespace Scheduler.Core.Views;

/// <summary>The three-way mark of a tree item: a green check, an empty box, a red X.</summary>
public enum PlanMark
{
    Open,
    Complete,
    Cancelled,
}

/// <summary>The mark of one card (1114-C).</summary>
public static class CardMark
{
    private static readonly Regex Closed = new(@"^\s*(closed|cancel+ed|replaced|dropped|failed|errored)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Cancelled or errored: its Status says closed, cancelled, replaced, dropped, failed or errored.</summary>
    public static bool IsCancelled(ViewCard card) => Closed.IsMatch(card.Status);

    /// <summary>In done/ or archive/ and not cancelled.</summary>
    public static bool IsComplete(ViewCard card) =>
        card.Folder is ViewFolder.Done or ViewFolder.Archive && !IsCancelled(card);

    public static PlanMark Of(ViewCard card) =>
        IsCancelled(card) ? PlanMark.Cancelled : IsComplete(card) ? PlanMark.Complete : PlanMark.Open;
}

/// <summary>A node of the Plans tree: a step, a sub-step, or "Other cards". Done and Total count its cards and its sub-steps' cards.</summary>
public sealed record PlanStepNode
{
    /// <summary>The step, null for "Other cards".</summary>
    public PlanStep? Step { get; init; }

    /// <summary>The cards matched to this step itself, by number.</summary>
    public IReadOnlyList<ViewCard> Cards { get; init; } = [];

    public IReadOnlyList<PlanStepNode> SubSteps { get; init; } = [];

    /// <summary>Complete cards under this node.</summary>
    public int Done { get; init; }

    /// <summary>The cards that count: every card but the cancelled ones.</summary>
    public int Total { get; init; }

    /// <summary>The cancelled or errored cards under this node, left out of <see cref="Total"/>.</summary>
    public int Cancelled { get; init; }

    /// <summary>A green check: the node has children and every one is complete or cancelled, at least one complete.</summary>
    public bool Checked => Mark == PlanMark.Complete;

    /// <summary>
    /// From the children's marks (its cards and sub-steps): green check when all are complete or cancelled with one
    /// complete, red X when all are cancelled, else an empty box. A node with no children stays an empty box.
    /// </summary>
    public PlanMark Mark => PlanMarks.Of(Cards.Select(CardMark.Of).Concat(SubSteps.Select(s => s.Mark)));
}

/// <summary>The rule that makes a parent's mark from its children's marks.</summary>
public static class PlanMarks
{
    public static PlanMark Of(IEnumerable<PlanMark> children)
    {
        var list = children.ToList();
        if (list.Count == 0 || list.Contains(PlanMark.Open))
        {
            return PlanMark.Open;
        }

        return list.Contains(PlanMark.Complete) ? PlanMark.Complete : PlanMark.Cancelled;
    }
}

/// <summary>One plan with its steps; for No plan <see cref="Plan"/> has kind NoPlan and only <see cref="Other"/> is set.</summary>
public sealed record PlanNode
{
    public required PlanRef Plan { get; init; }

    public IReadOnlyList<PlanStepNode> Steps { get; init; } = [];

    /// <summary>The cards that match no step of the plan (or every card of No plan); null when there are none.</summary>
    public PlanStepNode? Other { get; init; }

    /// <summary>Every step (and the Other cards) is complete or cancelled, at least one complete.</summary>
    public bool Complete => Mark == PlanMark.Complete;

    /// <summary>From its steps' marks and the Other cards' mark, by the same rule as a step.</summary>
    public PlanMark Mark => PlanMarks.Of(Steps.Select(n => n.Mark).Concat(Other is { } o ? [o.Mark] : []));
}

/// <summary>The Plans view's tree for one project, in the order <see cref="PlanCounts"/> orders plans.</summary>
public static class PlanTree
{
    /// <param name="cards">Every card of the project.</param>
    /// <param name="onMain">The paths that exist on main.</param>
    /// <param name="alsoPlans">Plans to list even when no card names them.</param>
    /// <param name="planText">A plan document's text by its path; a plan with no text has no steps.</param>
    public static IReadOnlyList<PlanNode> Of(
        IEnumerable<ViewCard> cards, ISet<string> onMain, IReadOnlyDictionary<string, string> planText,
        IEnumerable<PlanRef>? alsoPlans = null)
    {
        var byPlan = new Dictionary<PlanRef, List<ViewCard>>();
        foreach (var plan in alsoPlans ?? [])
        {
            if (!byPlan.ContainsKey(plan))
            {
                byPlan[plan] = [];
            }
        }

        foreach (var card in cards)
        {
            var plan = PlanPaths.Resolve(card.PlanPath, onMain);
            if (!byPlan.TryGetValue(plan, out var list))
            {
                byPlan[plan] = list = [];
            }

            list.Add(card);
        }

        return byPlan
            .OrderBy(p => Group(p.Key.Kind))
            .ThenBy(p => p.Key.Path, StringComparer.OrdinalIgnoreCase)
            .Select(p => Build(p.Key, p.Value, planText))
            .ToList();
    }

    private static PlanNode Build(PlanRef plan, List<ViewCard> cards, IReadOnlyDictionary<string, string> planText)
    {
        var steps = plan.Kind == PlanKind.NoPlan || !planText.TryGetValue(plan.Path, out var text)
            ? []
            : PlanSteps.Of(text);
        var matched = new Dictionary<PlanStep, List<ViewCard>>();
        var other = new List<ViewCard>();
        foreach (var card in cards.OrderBy(c => c.Number))
        {
            var step = plan.Kind == PlanKind.NoPlan ? null : PlanStepMatch.Of(card.PlanStep, steps);
            if (step is null)
            {
                other.Add(card);
                continue;
            }

            if (!matched.TryGetValue(step, out var list))
            {
                matched[step] = list = [];
            }

            list.Add(card);
        }

        var nodes = steps.Select(s => Node(s, matched)).ToList();
        return new PlanNode
        {
            Plan = plan,
            Steps = nodes,
            Other = other.Count == 0 ? null : new PlanStepNode { Cards = other, Done = DoneCount(other), Total = CountOf(other), Cancelled = CancelledCount(other) },
        };
    }

    private static PlanStepNode Node(PlanStep step, Dictionary<PlanStep, List<ViewCard>> matched)
    {
        var own = matched.GetValueOrDefault(step) ?? [];
        var subs = step.SubSteps.Select(s => Node(s, matched)).ToList();
        return new PlanStepNode
        {
            Step = step,
            Cards = own,
            SubSteps = subs,
            Done = DoneCount(own) + subs.Sum(s => s.Done),
            Total = CountOf(own) + subs.Sum(s => s.Total),
            Cancelled = CancelledCount(own) + subs.Sum(s => s.Cancelled),
        };
    }

    private static int DoneCount(IEnumerable<ViewCard> cards) => cards.Count(CardMark.IsComplete);

    private static int CancelledCount(IEnumerable<ViewCard> cards) => cards.Count(CardMark.IsCancelled);

    private static int CountOf(IEnumerable<ViewCard> cards) => cards.Count(c => !CardMark.IsCancelled(c));

    private static int Group(PlanKind kind) => kind switch
    {
        PlanKind.Plans => 0,
        PlanKind.Elsewhere or PlanKind.Missing => 1,
        PlanKind.Archived => 2,
        _ => 3,
    };
}
