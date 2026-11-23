using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Views;

/// <summary>VW-14: the counts of a plan or of one step: created, done and the pending cards split as the board's columns.</summary>
public sealed record PlanTally
{
    /// <summary>Every card naming it.</summary>
    public int Created { get; init; }

    /// <summary>In done/ or archive/.</summary>
    public int Done { get; init; }

    /// <summary>Waiting in queue/ (or claimed in active/ but not running).</summary>
    public int Queue { get; init; }

    public int Running { get; init; }

    public int Review { get; init; }

    /// <summary>Status needs-human, or waiting on the developer.</summary>
    public int WaitingOnYou { get; init; }

    public int WaitingOnPlanner { get; init; }

    public int Pending => Queue + Running + Review + WaitingOnYou + WaitingOnPlanner;
}

/// <summary>One step of a plan with its counts and its cards' ids (VW-15).</summary>
public sealed record PlanStepCount(string Step, PlanTally Tally, IReadOnlyList<string> CardIds);

/// <summary>One plan with its counts and its steps. For No plan <see cref="Plan"/> has kind NoPlan.</summary>
public sealed record PlanCount(PlanRef Plan, PlanTally Tally, IReadOnlyList<PlanStepCount> Steps);

/// <summary>VW-14, VW-15: counts per plan and per step, in the Plans view's order (VW-11).</summary>
public static class PlanCounts
{
    public const string NoStep = "(no step)";

    private static readonly Regex FirstNumber = new(@"\d+");

    /// <param name="cards">Every card of the project.</param>
    /// <param name="running">The ids of the cards with a worker running.</param>
    /// <param name="onMain">The paths that exist on main, for <see cref="PlanPaths.Resolve"/>.</param>
    public static IReadOnlyList<PlanCount> Of(IEnumerable<ViewCard> cards, ISet<string> running, ISet<string> onMain)
    {
        var byPlan = new Dictionary<PlanRef, List<(ViewCard Card, string Step)>>();
        foreach (var card in cards)
        {
            var plan = PlanPaths.Resolve(card.PlanPath, onMain);
            var step = plan.Kind == PlanKind.NoPlan ? "" : NormaliseStep(card.PlanStep);
            if (!byPlan.TryGetValue(plan, out var list))
            {
                byPlan[plan] = list = [];
            }

            list.Add((card, step.Length == 0 && plan.Kind != PlanKind.NoPlan ? NoStep : step));
        }

        return byPlan
            .OrderBy(p => Group(p.Key.Kind))
            .ThenBy(p => p.Key.Path, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PlanCount(
                p.Key,
                Tally(p.Value.Select(x => x.Card), running),
                p.Value
                    .GroupBy(x => x.Step)
                    .OrderBy(g => g.Key == NoStep ? 1 : 0)
                    .ThenBy(g => FirstNumberOf(g.Key))
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .Where(g => g.Key.Length > 0)
                    .Select(g => new PlanStepCount(
                        g.Key,
                        Tally(g.Select(x => x.Card), running),
                        g.OrderBy(x => x.Card.Number).Select(x => x.Card.Id).ToList()))
                    .ToList()))
            .ToList();
    }

    /// <summary>A step compared without case, brackets and trailing punctuation: "(5.2)." and "5.2" are one step.</summary>
    public static string NormaliseStep(string? step)
    {
        var s = Regex.Replace(step ?? "", @"[()\[\]{}]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
        return s.TrimEnd('.', ',', ';', ':', '!', '?', ' ');
    }

    /// <summary>The Plans view's order: jobs/plans/ by name, elsewhere (and missing) by path, archived, No plan.</summary>
    private static int Group(PlanKind kind) => kind switch
    {
        PlanKind.Plans => 0,
        PlanKind.Elsewhere or PlanKind.Missing => 1,
        PlanKind.Archived => 2,
        _ => 3,
    };

    private static long FirstNumberOf(string step)
    {
        var m = FirstNumber.Match(step);
        return m.Success && long.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : long.MaxValue;
    }

    private static PlanTally Tally(IEnumerable<ViewCard> cards, ISet<string> running)
    {
        int created = 0, done = 0, queue = 0, run = 0, review = 0, you = 0, planner = 0;
        foreach (var c in cards)
        {
            created++;
            if (c.Folder is ViewFolder.Done or ViewFolder.Archive)
            {
                done++;
            }
            else if (running.Contains(c.Id))
            {
                run++;
            }
            else if (WaitsOnDeveloper(c))
            {
                you++;
            }
            else if (WaitsOnPlanner(c))
            {
                planner++;
            }
            else if (c.Folder == ViewFolder.Review)
            {
                review++;
            }
            else
            {
                queue++;
            }
        }

        return new PlanTally
        {
            Created = created,
            Done = done,
            Queue = queue,
            Running = run,
            Review = review,
            WaitingOnYou = you,
            WaitingOnPlanner = planner,
        };
    }

    private static bool IsOpenFolder(ViewCard c) => c.Folder is ViewFolder.Queue or ViewFolder.Active;

    private static bool WaitsOnDeveloper(ViewCard c) =>
        IsOpenFolder(c) && (Text.IsMatch(c.Status, @"^needs-human\b") || Text.IsMatch(c.Status, CardWaits.DeveloperPattern));

    private static bool WaitsOnPlanner(ViewCard c) =>
        IsOpenFolder(c) && Text.IsMatch(c.Status, CardWaits.PlannerPattern);
}
