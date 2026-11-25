// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using Scheduler.Core.Contracts;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// LANE-3 as changed by SC-4 (320-A Q5, 560-A): a redefined category is no longer held, so its cards
/// start without an Accept; the page's /accept-category route stays and does no harm.
/// </summary>
public class CategoryAcceptTests
{
    private const string CategoriesV1 = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    private const string CategoriesV2 = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/","src/shared/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    // LANE-3 as changed by SC-4 (320-A Q5, 560-A): a category redefined while its card is queued is
    // not held; the definition is accepted at once and the card starts without an Accept.
    [Fact]
    public void Categories_RedefinedWithCardQueued_StartsWithoutAccept()
    {
        using var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-280b-lane");
        harness.Tick_Once();

        harness.Host.Cards.Add(Card(CardFolder.Queue, 10, "A"));
        harness.Host.CategoriesJson = CategoriesV2;
        harness.Tick_Once();

        Assert.Contains((1, "010-A", false), harness.Host.Starts);
    }

    private static Card Card(CardFolder folder, int num, string category) => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{num:000}-{category}-x.md",
        Name = $"{num:000}-{category}-x",
        Num = num,
        Category = category,
        Id = $"{num:000}-{category}",
        Status = "ready",
    };
}
