using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1125-C: the mark of a plan or step is clickable and says when the developer set it; a card's mark is not.</summary>
public sealed class PlanMarkClickTests
{
    private const string Plan = "jobs/plans/m_plan.md";

    private static string Html(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks)
    {
        var card = new ViewCard { Id = "1-C", Number = 1, Category = "C", Title = "T", Folder = ViewFolder.Done, PlanPath = Plan, PlanStep = "step 1" };
        var views = new ProjectViews
        {
            Project = "P",
            Snapshot = ViewsSnapshot.Build("P", [card], [new ViewPlan { Path = Plan, Kind = PlanKind.Plans }], new Dictionary<string, string> { [Plan] = "# M\n\n## A\n\n### a1\n\n## B\n" }, marks: marks),
            OnMain = new HashSet<string>([Plan], StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var page = new PageSnapshot
        {
            Built = DateTimeOffset.UnixEpoch,
            Project = "P",
            Version = "3.0.99",
            State = new PageStateView { State = SchedulerState.Running, Text = "running" },
            Health = new PageHealthView { Green = true, Text = "ok" },
            Stats = new PageStats { Cards = 0, Minutes = 0, Text = "" },
        };
        var pool = new PoolPageSnapshot
        {
            Built = DateTimeOffset.UnixEpoch,
            MaxWorkers = 1,
            Projects = [new PoolProjectView { Name = "P", Weight = 1, WeightShare = 1, UsedShare = 1 }],
            Pages = [page],
            Views = [views],
        };
        return new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "plans");
    }

    [Fact]
    public void PlanAndSteps_HaveAClickableMark_WithTheirKey_ButNotTheCards()
    {
        var html = Html(null);

        Assert.Contains("class='pm-click'", html, StringComparison.Ordinal);
        Assert.Contains("data-plan='" + Plan + "' data-step='plan'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='1'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='1.1'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='2'", html, StringComparison.Ordinal);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "class='pm-click'").Count);
        Assert.DoesNotContain("pm-set'>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AManualMark_ShowsTheSetByYouMarker()
    {
        var html = Html(new Dictionary<string, IReadOnlyDictionary<string, ManualMark>> { [Plan] = new Dictionary<string, ManualMark> { ["2"] = ManualMark.Checked } });

        Assert.Contains("set by you: checked", html, StringComparison.Ordinal);
        Assert.Contains("<span class='pm-set'>", html, StringComparison.Ordinal);
    }
}
