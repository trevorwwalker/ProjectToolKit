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

        Assert.Empty(harness.Tick.Categories.Held);
        var stored = AcceptedCategories.Load(harness.Files);
        Assert.Equal(harness.Tick.Categories.Current!.Categories["A"].Json, stored["A"]);
        Assert.Contains((1, "010-A", false), harness.Host.Starts);
    }

    // The /accept-category route stays for old pages; with nothing held it only re-stores the definition.
    [Fact]
    public async Task Categories_AcceptFromPage_StillAnswers()
    {
        using var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-280b-accept");
        harness.Tick_Once();

        var answer = await harness.Post("/accept-lane?lane=A");

        Assert.Equal(HttpStatusCode.SeeOther, answer.StatusCode);
        Assert.Empty(harness.Tick.Categories.Held);
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
