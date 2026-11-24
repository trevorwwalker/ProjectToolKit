using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>1128-C: the standing Bugs and Requests groups for cards with no plan, and "Plan not found".</summary>
public class PlanGroupsTests
{
    private static readonly ISet<string> OnMain = new HashSet<string>(["jobs/plans/a_plan.md"], StringComparer.Ordinal);

    private static ViewCard Card(string id, ViewFolder folder = ViewFolder.Queue, string type = "feature", string plan = "none (developer, 2026-11-22)", string status = "") => new()
    {
        Id = id,
        Number = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture),
        Category = id.Split('-')[1],
        Folder = folder,
        CardType = type,
        PlanPath = plan,
        Status = status,
    };

    private static ViewRun Run(string id, int month, int year = 2026) =>
        new() { CardId = id, Start = new DateTimeOffset(year, month, 10, 12, 0, 0, TimeSpan.Zero), End = new DateTimeOffset(year, month, 10, 13, 0, 0, TimeSpan.Zero) };

    [Fact]
    public void Split_BugsAreTypeBugWithNoPlan_RequestsAreTheRest_PlannedCardsAreInNeither()
    {
        var groups = PlanGroups.Of(
            [Card("1-A", type: "bug"), Card("2-A", type: "BUG", plan: ""), Card("3-A"), Card("4-A", type: "bug", plan: "jobs/plans/a_plan.md"), Card("5-A", type: "")],
            OnMain);

        Assert.Equal(["1-A", "2-A"], groups.Bugs!.Categories.Single().Months.Single().Cards.Select(c => c.Id));
        Assert.Equal(["3-A", "5-A"], groups.Requests!.Categories.Single().Months.Single().Cards.Select(c => c.Id));
        Assert.Null(groups.NotFound);
    }

    [Fact]
    public void NoCards_NoGroups()
    {
        var groups = PlanGroups.Of([Card("4-A", plan: "jobs/plans/a_plan.md")], OnMain);

        Assert.Null(groups.Bugs);
        Assert.Null(groups.Requests);
        Assert.Null(groups.NotFound);
    }

    [Fact]
    public void Grouping_CategoryThenMonth_NewestMonthFirst_NotRunYetFirst_CardsByNumber()
    {
        var groups = PlanGroups.Of(
            [Card("9-B"), Card("1-A"), Card("2-A"), Card("3-A"), Card("4-A"), Card("5-A")],
            OnMain,
            [Run("1-A", 10), Run("2-A", 11), Run("2-A", 9), Run("3-A", 11), Run("4-A", 11, 2025), Run("9-B", 11)]);

        var requests = groups.Requests!;
        Assert.Equal(["A", "B"], requests.Categories.Select(c => c.Category));
        var a = requests.Categories[0];
        // 2-A's first run is in September; 5-A has no run.
        Assert.Equal(["", "2026-11", "2026-10", "2026-09", "2025-11"], a.Months.Select(m => m.Month));
        Assert.Equal(["5-A"], a.Months[0].Cards.Select(c => c.Id));
        Assert.Equal(["3-A"], a.Months[1].Cards.Select(c => c.Id));
        Assert.Equal(["2-A"], a.Months[3].Cards.Select(c => c.Id));
    }

    [Fact]
    public void Marks_AMonthOfDoneCardsIsChecked_OpenOnesShowDoneOverTotal_CancelledAreLeftOut()
    {
        var groups = PlanGroups.Of(
            [Card("1-A", ViewFolder.Done), Card("2-A", ViewFolder.Done), Card("3-A", ViewFolder.Queue), Card("4-A", ViewFolder.Done, status: "cancelled"), Card("5-B", ViewFolder.Done)],
            OnMain,
            [Run("1-A", 10), Run("2-A", 10), Run("3-A", 11), Run("4-A", 11), Run("5-B", 10)]);

        var a = groups.Requests!.Categories[0];
        var november = a.Months.Single(m => m.Month == "2026-11");
        var october = a.Months.Single(m => m.Month == "2026-10");
        Assert.Equal(PlanMark.Open, november.Mark);
        Assert.Equal((0, 1), (november.Done, november.Total));
        Assert.True(october.Checked);
        Assert.Equal((2, 2), (october.Done, october.Total));
        Assert.Equal(PlanMark.Open, a.Mark);
        Assert.Equal((2, 3), (a.Done, a.Total));
        Assert.True(groups.Requests.Categories[1].Checked);
        Assert.Equal(PlanMark.Open, groups.Requests.Mark);
        Assert.Equal((3, 4), (groups.Requests.Done, groups.Requests.Total));
    }

    [Fact]
    public void PlanNotFound_ACardNamingANonPlanPathStaysVisible_NotInBugsOrRequests()
    {
        var groups = PlanGroups.Of([Card("1-A", type: "bug", plan: "docs/design/old.md"), Card("2-A", ViewFolder.Done, plan: "docs/x.md"), Card("3-A")], OnMain);

        Assert.Equal(["1-A", "2-A"], groups.NotFound!.Cards.Select(c => c.Id));
        Assert.Null(groups.Bugs);
        Assert.Equal(["3-A"], groups.Requests!.Categories.Single().Months.Single().Cards.Select(c => c.Id));
        Assert.Equal((1, 2), (groups.NotFound.Done, groups.NotFound.Total));
    }
}
