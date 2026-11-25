// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Views;

/// <summary>The cards of one category in one month (1128-C). <see cref="Month"/> is "yyyy-MM", or empty for cards that have not run.</summary>
public sealed record CardMonthNode
{
    public required string Month { get; init; }

    public IReadOnlyList<ViewCard> Cards { get; init; } = [];

    public int Done { get; init; }

    public int Total { get; init; }

    public PlanMark Mark => PlanMarks.Of(Cards.Select(CardMark.Of));

    public bool Checked => Mark == PlanMark.Complete;
}

/// <summary>One category's months inside a group, newest month first.</summary>
public sealed record CardCategoryNode
{
    public required string Category { get; init; }

    public IReadOnlyList<CardMonthNode> Months { get; init; } = [];

    public int Done => Months.Sum(m => m.Done);

    public int Total => Months.Sum(m => m.Total);

    public PlanMark Mark => PlanMarks.Of(Months.Select(m => m.Mark));

    public bool Checked => Mark == PlanMark.Complete;
}

/// <summary>A standing group of cards that belong to no plan: Bugs or Requests.</summary>
public sealed record CardGroupNode
{
    public required string Name { get; init; }

    public IReadOnlyList<CardCategoryNode> Categories { get; init; } = [];

    public int Done => Categories.Sum(c => c.Done);

    public int Total => Categories.Sum(c => c.Total);

    public PlanMark Mark => PlanMarks.Of(Categories.Select(c => c.Mark));

    public bool Checked => Mark == PlanMark.Complete;
}

/// <summary>The cards that name a plan path that is not a file in jobs/plans/: the "Plan not found" node.</summary>
public sealed record PlanNotFoundNode
{
    public IReadOnlyList<ViewCard> Cards { get; init; } = [];

    public int Done => Cards.Count(CardMark.IsComplete);

    public int Total => Cards.Count(c => !CardMark.IsCancelled(c));

    public PlanMark Mark => PlanMarks.Of(Cards.Select(CardMark.Of));

    public bool Checked => Mark == PlanMark.Complete;
}

/// <summary>What the Plans view shows below the plans for cards with no plan (1128-C).</summary>
public sealed record PlanGroups
{
    /// <summary>Type bug with no plan; null when there is none.</summary>
    public CardGroupNode? Bugs { get; init; }

    /// <summary>Every other card with no plan; null when there is none.</summary>
    public CardGroupNode? Requests { get; init; }

    /// <summary>Cards whose Plan: names a path that is not a plan; null when there is none.</summary>
    public PlanNotFoundNode? NotFound { get; init; }

    public static PlanGroups Of(IEnumerable<ViewCard> cards, ISet<string> onMain, IEnumerable<ViewRun>? runs = null)
    {
        var firstRun = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var run in runs ?? [])
        {
            if (!firstRun.TryGetValue(run.CardId, out var at) || run.Start < at)
            {
                firstRun[run.CardId] = run.Start;
            }
        }

        var bugs = new List<ViewCard>();
        var requests = new List<ViewCard>();
        var notFound = new List<ViewCard>();
        foreach (var card in cards.OrderBy(c => c.Number))
        {
            if (PlanPaths.Resolve(card.PlanPath, onMain).Kind != PlanKind.NoPlan)
            {
                continue;
            }

            if (PlanPaths.Named(card.PlanPath).Length > 0)
            {
                notFound.Add(card);
            }
            else if (string.Equals(card.CardType, "bug", StringComparison.OrdinalIgnoreCase))
            {
                bugs.Add(card);
            }
            else
            {
                requests.Add(card);
            }
        }

        return new PlanGroups
        {
            Bugs = Group("Bugs", bugs, firstRun),
            Requests = Group("Requests", requests, firstRun),
            NotFound = notFound.Count == 0 ? null : new PlanNotFoundNode { Cards = notFound },
        };
    }

    private static CardGroupNode? Group(string name, List<ViewCard> cards, Dictionary<string, DateTimeOffset> firstRun)
    {
        if (cards.Count == 0)
        {
            return null;
        }

        var categories = cards
            .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CardCategoryNode
            {
                Category = g.Key,
                Months = g
                    .GroupBy(c => firstRun.TryGetValue(c.Id, out var at) ? at.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) : "")
                    .OrderBy(m => m.Key.Length == 0 ? 0 : 1)
                    .ThenByDescending(m => m.Key, StringComparer.Ordinal)
                    .Select(m => new CardMonthNode
                    {
                        Month = m.Key,
                        Cards = m.OrderBy(c => c.Number).ToList(),
                        Done = m.Count(CardMark.IsComplete),
                        Total = m.Count(c => !CardMark.IsCancelled(c)),
                    })
                    .ToList(),
            })
            .ToList();
        return new CardGroupNode { Name = name, Categories = categories };
    }
}
