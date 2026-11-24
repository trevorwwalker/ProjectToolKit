using Scheduler.Core.Views;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>1125-C: a click on a plan's mark cycles it in jobs/plan-marks.json and commits it.</summary>
public class PlanMarkActionTests
{
    private const string Plan = "jobs/plans/x_plan.md";

    [Fact]
    public void Click_CyclesTheMark_AndCommitsTheFileWithTheNamedMessage()
    {
        var main = new PageReviewRefreshTests.InMemoryMain();
        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(main: main, committer: committer, project: "ptk-1125c");
        harness.Tick_Once();
        var rel = "docs/jobs/plan-marks.json";

        var first = harness.Actions.PlanMark(Plan, "3");
        Assert.True(first.Ok, first.Error);
        var change = Assert.Single(committer.Changes);
        Assert.Equal("jobs: plan mark " + Plan + " 3 checked", change.Message);
        Assert.Equal(ManualMark.Checked, PlanMarkFile.Of(PlanMarkFile.Read(change.Writes[rel]), Plan, "3"));

        main.Files[rel] = change.Writes[rel];
        Assert.True(harness.Actions.PlanMark(Plan, "3").Ok);
        Assert.EndsWith("3 cleared", committer.Changes[1].Message, StringComparison.Ordinal);
        main.Files[rel] = committer.Changes[1].Writes[rel];
        Assert.True(harness.Actions.PlanMark(Plan, "3").Ok);
        Assert.EndsWith("3 automatic", committer.Changes[2].Message, StringComparison.Ordinal);
        Assert.Empty(PlanMarkFile.Read(committer.Changes[2].Writes[rel]));
    }

    [Fact]
    public void Click_WithNoPlan_IsRefused()
    {
        var committer = new RecordingCommitter();
        using var harness = new PageActionHarness(committer: committer, project: "ptk-1125c-none");
        harness.Tick_Once();

        Assert.False(harness.Actions.PlanMark("", "3").Ok);
        Assert.Empty(committer.Changes);
    }
}
