namespace Scheduler.Web.Tests.Server;

/// <summary>1125-C: POST /plan-mark hands the plan and step to the page action.</summary>
public class PlanMarkRouteTests
{
    [Fact]
    public async Task PlanMark_PassesThePlanAndStep()
    {
        using var web = new WebHarness();

        await web.PostFormAsync("/plan-mark", "plan=jobs%2Fplans%2Fx_plan.md&step=3.1");

        Assert.Equal(1, web.Actions.CallCount("/plan-mark"));
        Assert.Equal("jobs/plans/x_plan.md|3.1", web.Actions.LastPlanMark);
    }
}
