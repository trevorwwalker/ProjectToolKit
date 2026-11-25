// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
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

    /// <summary>The child plans drawn under this step (VW-27), by path.</summary>
    public IReadOnlyList<PlanNode> Plans { get; init; } = [];

    /// <summary>Complete cards under this node.</summary>
    public int Done { get; init; }

    /// <summary>The cards that count: every card but the cancelled ones.</summary>
    public int Total { get; init; }

    /// <summary>The cancelled or errored cards under this node, left out of <see cref="Total"/>.</summary>
    public int Cancelled { get; init; }

    /// <summary>What the developer set on this node's mark (1125-C); automatic when nothing.</summary>
    public ManualMark Manual { get; init; }

    /// <summary>A green check: the node has children and every one is complete or cancelled, at least one complete.</summary>
    public bool Checked => Mark == PlanMark.Complete;

    /// <summary>
    /// From the children's marks (its cards and sub-steps): green check when all are complete or cancelled with one
    /// complete, red X when all are cancelled, else an empty box. A node with no children stays an empty box.
    /// </summary>
    public PlanMark Mark => PlanMarks.Manual(Manual) ?? PlanMarks.Of(Cards.Select(CardMark.Of).Concat(Plans.Select(p => p.Mark)).Concat(SubSteps.Select(s => s.Mark)));
}

/// <summary>The rule that makes a parent's mark from its children's marks.</summary>
public static class PlanMarks
{
    /// <summary>The mark a developer's setting forces: checked is complete; null for automatic.</summary>
    public static PlanMark? Manual(ManualMark manual) => manual switch
    {
        ManualMark.Checked => PlanMark.Complete,
        _ => null,
    };

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

    /// <summary>What the developer set on the plan's own mark (1125-C).</summary>
    public ManualMark Manual { get; init; }

    /// <summary>Why the plan is not under its parent (VW-28); none for a root and for a placed child.</summary>
    public PlanFlag Flag { get; init; }

    /// <summary>The words for <see cref="Flag"/>; empty when none.</summary>
    public string FlagText { get; init; } = "";

    /// <summary>The Parent line as read; null for a root.</summary>
    public PlanParentLine? Parent { get; init; }

    /// <summary>Complete cards under the plan, child plans included (VW-29).</summary>
    public int Done => Steps.Sum(s => s.Done) + (Other?.Done ?? 0);

    /// <summary>The cards that count under the plan, child plans included.</summary>
    public int Total => Steps.Sum(s => s.Total) + (Other?.Total ?? 0);

    /// <summary>The cancelled cards under the plan, child plans included.</summary>
    public int Cancelled => Steps.Sum(s => s.Cancelled) + (Other?.Cancelled ?? 0);

    /// <summary>Every step (and the Other cards) is complete or cancelled, at least one complete.</summary>
    public bool Complete => Mark == PlanMark.Complete;

    /// <summary>From its steps' marks and the Other cards' mark, by the same rule as a step.</summary>
    public PlanMark Mark => PlanMarks.Manual(Manual) ?? PlanMarks.Of(Steps.Select(n => n.Mark).Concat(Other is { } o ? [o.Mark] : []));
}

/// <summary>The nested Plans tree of one project (VW-27): the top level, and how many roots there are (VW-30).</summary>
public sealed record PlanForest
{
    /// <summary>Roots (by path), unplaced plans, missing plans, then the No plan node.</summary>
    public IReadOnlyList<PlanNode> Top { get; init; } = [];

    /// <summary>The plans in jobs/plans/ with no parent.</summary>
    public int Roots { get; init; }

    public static readonly PlanForest Empty = new();
}

/// <summary>The Plans view's tree for one project, in the order <see cref="PlanCounts"/> orders plans.</summary>
public static class PlanTree
{
    /// <param name="cards">Every card of the project.</param>
    /// <param name="onMain">The paths that exist on main.</param>
    /// <param name="alsoPlans">Plans to list even when no card names them; only files in jobs/plans/ count (VW-11).</param>
    /// <param name="marks">The marks the developer set, by plan path and step label; one for a step that does not exist is ignored (1125-C).</param>
    /// <param name="planText">A plan document's text by its path; a plan with no text has no steps.</param>
    public static IReadOnlyList<PlanNode> Of(
        IEnumerable<ViewCard> cards, ISet<string> onMain, IReadOnlyDictionary<string, string> planText,
        IEnumerable<PlanRef>? alsoPlans = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks = null)
    {
        return Group(cards, onMain, alsoPlans)
            .OrderBy(p => Group(p.Key.Kind))
            .ThenBy(p => p.Key.Path, StringComparer.OrdinalIgnoreCase)
            .Select(p => Build(p.Key, p.Value, planText, marks, null))
            .ToList();
    }

    private static Dictionary<PlanRef, List<ViewCard>> Group(IEnumerable<ViewCard> cards, ISet<string> onMain, IEnumerable<PlanRef>? alsoPlans)
    {
        var byPlan = new Dictionary<PlanRef, List<ViewCard>>();
        foreach (var plan in (alsoPlans ?? []).Where(p => p.Kind is PlanKind.Plans or PlanKind.Missing))
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

        return byPlan;
    }

    private static PlanNode Build(PlanRef plan, List<ViewCard> cards, IReadOnlyDictionary<string, string> planText, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks, IReadOnlyDictionary<string, List<PlanNode>>? children)
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

        var nodes = steps.Select(s => Node(s, matched, marks, plan.Path, children)).ToList();
        return new PlanNode
        {
            Plan = plan,
            Manual = PlanMarkFile.Of(marks, plan.Path, PlanMarkFile.PlanKey),
            Steps = nodes,
            Other = other.Count == 0 ? null : new PlanStepNode { Cards = other, Done = DoneCount(other), Total = CountOf(other), Cancelled = CancelledCount(other) },
        };
    }

    private static PlanStepNode Node(PlanStep step, Dictionary<PlanStep, List<ViewCard>> matched, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks, string plan, IReadOnlyDictionary<string, List<PlanNode>>? children)
    {
        var own = matched.GetValueOrDefault(step) ?? [];
        var subs = step.SubSteps.Select(s => Node(s, matched, marks, plan, children)).ToList();
        IReadOnlyList<PlanNode> plans = children is not null && children.TryGetValue(step.Label, out var kids)
            ? kids.OrderBy(k => k.Plan.Path, StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        return new PlanStepNode
        {
            Step = step,
            Manual = PlanMarkFile.Of(marks, plan, step.Label),
            Cards = own,
            SubSteps = subs,
            Plans = plans,
            Done = DoneCount(own) + subs.Sum(s => s.Done) + plans.Sum(p => p.Done),
            Total = CountOf(own) + subs.Sum(s => s.Total) + plans.Sum(p => p.Total),
            Cancelled = CancelledCount(own) + subs.Sum(s => s.Cancelled) + plans.Sum(p => p.Cancelled),
        };
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ViewsSnapshot, PlanForest> Forests = new();

    /// <summary>The snapshot's forest (VW-33), built once per snapshot instance.</summary>
    public static PlanForest Of(ViewsSnapshot snapshot) => Forests.GetValue(snapshot, s =>
    {
        var onMain = s.Plans.Where(p => p.Kind == PlanKind.Plans).Select(p => p.Path).ToHashSet(StringComparer.Ordinal);
        return Nest(s.Cards, onMain, s.PlanTexts, s.Plans.Select(p => new PlanRef(p.Kind, p.Path)), s.Marks);
    });

    /// <summary>The node and every plan under it, depth-first in VW-27's order.</summary>
    public static IEnumerable<PlanNode> Walk(PlanNode node)
    {
        yield return node;
        foreach (var step in node.Steps)
        {
            foreach (var p in WalkStep(step))
            {
                yield return p;
            }
        }
    }

    private static IEnumerable<PlanNode> WalkStep(PlanStepNode step)
    {
        foreach (var child in step.Plans)
        {
            foreach (var p in Walk(child))
            {
                yield return p;
            }
        }

        foreach (var sub in step.SubSteps)
        {
            foreach (var p in WalkStep(sub))
            {
                yield return p;
            }
        }
    }

    /// <summary>VW-27 to VW-30: every plan placed once, child plans under their parent's step, cycles broken.</summary>
    public static PlanForest Nest(
        IEnumerable<ViewCard> cards, ISet<string> onMain, IReadOnlyDictionary<string, string> planText,
        IEnumerable<PlanRef>? alsoPlans = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks = null)
    {
        var byPlan = Group(cards, onMain, alsoPlans);
        var cmp = StringComparer.OrdinalIgnoreCase;

        // Resolve each plan's Parent line (VW-26).
        var lines = new Dictionary<string, PlanParentLine>(StringComparer.Ordinal);
        var parentOf = new Dictionary<string, (string Path, PlanStep? Step)>(StringComparer.Ordinal);
        var flags = new Dictionary<string, PlanFlag>(StringComparer.Ordinal);
        foreach (var plan in byPlan.Keys.Where(k => k.Kind == PlanKind.Plans).ToList())
        {
            if (!planText.TryGetValue(plan.Path, out var text) || PlanParent.Of(text) is not { } line)
            {
                continue;
            }

            lines[plan.Path] = line;
            var target = PlanPaths.Resolve(line.Path, onMain);
            if (target.Kind == PlanKind.NoPlan)
            {
                flags[plan.Path] = PlanFlag.ParentNotAPlan;
            }
            else if (target.Kind == PlanKind.Missing)
            {
                flags[plan.Path] = PlanFlag.ParentNotFound;
            }
            else if (target.Path == plan.Path)
            {
                parentOf[plan.Path] = (target.Path, null);
            }
            else
            {
                var step = planText.TryGetValue(target.Path, out var parentText) ? PlanStepMatch.Of(line.StepText, PlanSteps.Of(parentText)) : null;
                if (step is null)
                {
                    flags[plan.Path] = PlanFlag.StepNotFound;
                }
                else
                {
                    parentOf[plan.Path] = (target.Path, step);
                    byPlan.TryAdd(new PlanRef(PlanKind.Plans, target.Path), []);
                }
            }
        }

        // Break cycles at the member that sorts first by path (VW-28).
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in parentOf.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
        {
            var chain = new List<string>();
            var at = start;
            while (parentOf.ContainsKey(at) && !visited.Contains(at))
            {
                var seen = chain.IndexOf(at);
                if (seen >= 0)
                {
                    var first = chain.Skip(seen).OrderBy(k => k, cmp).ThenBy(k => k, StringComparer.Ordinal).First();
                    parentOf.Remove(first);
                    flags[first] = PlanFlag.Cycle;
                    break;
                }

                chain.Add(at);
                at = parentOf[at].Path;
            }

            visited.UnionWith(chain);
        }

        // Children by parent path and step label.
        var kids = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var (child, (parent, step)) in parentOf)
        {
            if (!kids.TryGetValue(parent, out var byStep))
            {
                kids[parent] = byStep = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            if (!byStep.TryGetValue(step!.Label, out var list))
            {
                byStep[step.Label] = list = [];
            }

            list.Add(child);
        }

        var built = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
        PlanNode Make(PlanRef plan)
        {
            if (built.TryGetValue(plan.Path, out var existing))
            {
                return existing;
            }

            Dictionary<string, List<PlanNode>>? children = null;
            if (kids.TryGetValue(plan.Path, out var byStep))
            {
                children = byStep.ToDictionary(e => e.Key, e => e.Value.Select(p => Make(new PlanRef(PlanKind.Plans, p))).ToList());
            }

            lines.TryGetValue(plan.Path, out var parentLine);
            var flag = flags.GetValueOrDefault(plan.Path);
            var node = Build(plan, byPlan[plan], planText, marks, children) with { Parent = parentLine, Flag = flag, FlagText = PlanFlags.Text(flag, parentLine) };
            built[plan.Path] = node;
            return node;
        }

        var top = byPlan.Keys
            .Where(p => p.Kind != PlanKind.Plans || !parentOf.ContainsKey(p.Path))
            .OrderBy(p => p.Kind switch { PlanKind.Plans => flags.ContainsKey(p.Path) ? 1 : 0, PlanKind.Missing => 2, _ => 3 })
            .ThenBy(p => p.Path, cmp)
            .Select(Make)
            .ToList();
        return new PlanForest { Top = top, Roots = byPlan.Keys.Count(p => p.Kind == PlanKind.Plans && !lines.ContainsKey(p.Path)) };
    }

    private static int DoneCount(IEnumerable<ViewCard> cards) => cards.Count(CardMark.IsComplete);

    private static int CancelledCount(IEnumerable<ViewCard> cards) => cards.Count(CardMark.IsCancelled);

    private static int CountOf(IEnumerable<ViewCard> cards) => cards.Count(c => !CardMark.IsCancelled(c));

    private static int Group(PlanKind kind) => kind switch
    {
        PlanKind.Plans => 0,
        PlanKind.Missing => 1,
        _ => 3,
    };
}
