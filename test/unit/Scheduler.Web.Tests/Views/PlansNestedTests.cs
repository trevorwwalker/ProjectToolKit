// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1233-C: the Plans page draws child plans inside their parent's step (views.md VW-27 to VW-30).</summary>
public sealed class PlansNestedTests
{
    private const string Root = "jobs/plans/root_plan.md";
    private const string Child = "jobs/plans/child_plan.md";
    private const string Lost = "jobs/plans/lost_plan.md";

    private static ViewCard Card(string id, ViewFolder folder, string plan, string step) => new()
    {
        Id = id,
        Number = long.Parse(id[..id.IndexOf('-')]),
        Category = id[(id.IndexOf('-') + 1)..],
        Title = "Title of " + id,
        Folder = folder,
        PlanPath = plan,
        PlanStep = step,
        CardType = "feature",
    };

    private static string Html(IEnumerable<ViewCard> cards, Dictionary<string, string> texts, Dictionary<string, Dictionary<string, ManualMark>>? marks = null)
    {
        var plans = texts.Keys.Select(p => new ViewPlan { Path = p, Kind = PlanKind.Plans, Title = p }).ToList();
        var view = new ProjectViews
        {
            Project = "P",
            Snapshot = ViewsSnapshot.Build("P", cards, plans, texts),
            Running = new HashSet<string>(StringComparer.Ordinal),
            OnMain = new HashSet<string>(texts.Keys, StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var pool = new PoolPageSnapshot { Built = DateTimeOffset.UnixEpoch, MaxWorkers = 1, Running = 0, Projects = [], Pages = [], Views = [view] };
        var page = new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "plans");
        Assert.DoesNotContain("w-plans-completed", page, StringComparison.Ordinal);
        return page;
    }

    private static Dictionary<string, string> Texts() => new()
    {
        [Root] = "# Root\n\n## 1. First\n\n## 2. Second\n",
        [Child] = "# Child\n\nParent: jobs/plans/root_plan.md, step 1\n\n## 1. Only\n",
    };

    private static List<ViewCard> Cards() =>
    [
        Card("10-A", ViewFolder.Queue, Root, "step 1"),
        Card("11-A", ViewFolder.Queue, Child, "step 1"),
        Card("12-A", ViewFolder.Done, Child, "step 1"),
    ];

    private static int Count(string html, string needle) => Regex.Matches(html, Regex.Escape(needle)).Count;

    [Fact]
    public void VW27_Page_ChildPlanInsideParentStep_AfterCards()
    {
        var html = Html(Cards(), Texts());

        var step = html.IndexOf("data-key='s|P|" + Root + "|1'", StringComparison.Ordinal);
        var card = html.IndexOf("id=10-A", step, StringComparison.Ordinal);
        var child = html.IndexOf("data-key='p|P|" + Child + "'", StringComparison.Ordinal);
        var second = html.IndexOf("2. Second", StringComparison.Ordinal);
        Assert.True(step >= 0 && card > step && child > card && child < second);
    }

    [Fact]
    public void VW27_Page_NestedPlanFoldedByDefault()
    {
        var html = Html(Cards(), Texts());

        Assert.Contains("<details class='plan plan-step' data-project='P' data-key='p|P|" + Child + "'><summary>", html, StringComparison.Ordinal);
        Assert.Contains("<details class='plan' data-project='P' data-key='p|P|" + Root + "' open><summary>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW27_Page_StepWithOnlyChildPlan_IsDetails()
    {
        var texts = Texts();
        var html = Html([Card("11-A", ViewFolder.Queue, Child, "step 1")], texts);

        Assert.Contains("data-key='s|P|" + Root + "|1'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='p|P|" + Child + "'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW27_Page_DetailsKeysUniqueWithNesting()
    {
        var html = Html(Cards(), Texts());

        var from = html.IndexOf("id='w-plans'", StringComparison.Ordinal);
        html = html[from..html.IndexOf("<script", from, StringComparison.Ordinal)];
        var keys = Regex.Matches(html, "<details[^>]*data-key='([^']*)'").Select(m => m.Groups[1].Value).ToList();
        Assert.True(keys.Count >= 4);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Count(html, "<details"), keys.Count);
    }

    [Fact]
    public void VW27_Page_ExpandAllReachesNested()
    {
        // The script acts on every details[data-key] under the window, at any depth; nested plans carry a key.
        Assert.Contains("w.querySelectorAll('details[data-key]').forEach", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("data-key='p|P|" + Child + "'", Html(Cards(), Texts()), StringComparison.Ordinal);
    }

    [Fact]
    public void VW28_Page_UnplacedPlan_TopLevelWithReason()
    {
        var texts = Texts();
        texts[Lost] = "# Lost\n\nParent: jobs/plans/nowhere_plan.md, step 1\n\n## 1. Only\n";
        var html = Html([.. Cards(), Card("13-A", ViewFolder.Queue, Lost, "step 1")], texts);

        Assert.Contains("parent not found: ", html, StringComparison.Ordinal);
        Assert.Contains("class='mute plan-flag'", html, StringComparison.Ordinal);
        // Drawn at the top level: its details is not the step class, and the root's block ended before it.
        Assert.Contains("<details class='plan' data-project='P' data-key='p|P|" + Lost + "'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_CountsAreRolledUp()
    {
        var html = Html(Cards(), Texts());

        // Root step 1: its own card (0/1) plus the child's two cards (1/2): 1/3; the root holds the same.
        var step = html.IndexOf("data-key='s|P|" + Root + "|1'", StringComparison.Ordinal);
        Assert.Contains("1/3", html[step..html.IndexOf("</summary>", step, StringComparison.Ordinal)], StringComparison.Ordinal);
        var root = html.IndexOf("data-key='p|P|" + Root + "'", StringComparison.Ordinal);
        Assert.Contains("1/3", html[root..html.IndexOf("</summary>", root, StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_NestedMarkClickCarriesChildPath()
    {
        var html = Html(Cards(), Texts());

        Assert.Contains("data-plan='" + Child + "' data-step='" + PlanMarkFile.PlanKey + "'", html, StringComparison.Ordinal);
        Assert.Contains("data-plan='" + Child + "' data-step='1'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW30_Page_TwoRoots_SaysTwoRootPlans()
    {
        var texts = Texts();
        texts["jobs/plans/other_plan.md"] = "# Other\n\n## 1. Only\n";

        Assert.Contains("<div class='mute plans-roots'>2 root plans</div>", Html(Cards(), texts), StringComparison.Ordinal);
    }

    [Fact]
    public void VW30_Page_OneRoot_SaysNothing()
    {
        Assert.DoesNotContain("root plans", Html(Cards(), Texts()), StringComparison.Ordinal);
    }

    [Fact]
    public void VW30_Page_CompleteChildUnderOpenRoot_IsDrawnInTheTree_WithDataComplete()
    {
        var cards = new List<ViewCard> { Card("10-A", ViewFolder.Queue, Root, "step 1"), Card("12-A", ViewFolder.Done, Child, "step 1") };

        var open = Html(cards, Texts());

        Assert.Contains("data-key='p|P|" + Child + "'", open, StringComparison.Ordinal);
        Assert.Contains("data-key='p|P|" + Child + "' data-complete='1'", open, StringComparison.Ordinal);
    }

    // 1299-C: the number shown in front of a step is its whole path in the plan tree; keys, marks and links keep the own label.
    private const string Grand = "jobs/plans/grand_plan.md";
    private const string Twin = "jobs/plans/twin_plan.md";

    private static string Tree(Dictionary<string, string> texts) => Html([], texts);

    [Fact]
    public void VW29_Page_RootSteps_ShowTheirOwnNumbers()
    {
        var html = Tree(new() { [Root] = "# Root\n\n## 1. First\n\n## 2. Second\n" });

        Assert.Contains(">1. First</a>", html, StringComparison.Ordinal);
        Assert.Contains(">2. Second</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_ChildPlanStep_ShowsParentDotOwn_KeysUnchanged()
    {
        var html = Html([Card("20-A", ViewFolder.Queue, Child, "step 7")], new()
        {
            [Root] = "# Root\n\n## 1. First\n\n## 2. Second\n",
            [Child] = "# Child\n\nParent: jobs/plans/root_plan.md, step 2\n\n## 1. Skip\n\n## 7. Seventh\n",
        });

        Assert.Contains(">2.7. Seventh</a>", html, StringComparison.Ordinal);
        Assert.Contains("title='step 7 of " + Child + "'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='s|P|" + Child + "|7'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-key='s|P|" + Child + "|2.7'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='s|P|" + Root + "|2'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_ThreeLevels_ShowThreeParts()
    {
        var html = Html([Card("21-A", ViewFolder.Queue, Grand, "step 8")], new()
        {
            [Root] = "# Root\n\n## 1. First\n\n## 2. Second\n",
            [Child] = "# Child\n\nParent: jobs/plans/root_plan.md, step 2\n\n## 10. Tenth\n",
            [Grand] = "# Grand\n\nParent: jobs/plans/child_plan.md, step 10\n\n## 8. Eighth\n",
        });

        Assert.Contains(">2.10.8. Eighth</a>", html, StringComparison.Ordinal);
        Assert.Contains("data-key='s|P|" + Grand + "|8'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_SubStepAndChildUnderSubStep_ShowAsPath()
    {
        var html = Tree(new()
        {
            [Root] = "# Root\n\n## 1. First\n\n## 2. Second\n\n### Alpha\n\n### Beta\n",
            [Child] = "# Child\n\nParent: jobs/plans/root_plan.md, step 2.2\n\n## 1. Only\n\n### Deep\n",
        });

        Assert.Contains(">2.2. Beta</a>", html, StringComparison.Ordinal);
        Assert.Contains(">2.2.1. Only</a>", html, StringComparison.Ordinal);
        Assert.Contains(">2.2.1.1. Deep</a>", html, StringComparison.Ordinal);
        Assert.Contains("data-key='s|P|" + Root + "|2.2'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_TwoChildPlansUnderOneStep_BothNumberFromParent_OptionA()
    {
        var html = Tree(new()
        {
            [Root] = "# Root\n\n## 1. First\n",
            [Child] = "# Child\n\nParent: jobs/plans/root_plan.md, step 1\n\n## 1. Alpha\n",
            [Twin] = "# Twin\n\nParent: jobs/plans/root_plan.md, step 1\n\n## 1. Beta\n",
        });

        Assert.Contains(">1.1. Alpha</a>", html, StringComparison.Ordinal);
        Assert.Contains(">1.1. Beta</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW29_Page_UnplacedPlan_NumbersFromItsOwnLabels()
    {
        var html = Tree(new()
        {
            [Root] = "# Root\n\n## 1. First\n",
            [Lost] = "# Lost\n\nParent: jobs/plans/nowhere_plan.md, step 1\n\n## 4. Fourth\n",
        });

        Assert.Contains(">4. Fourth</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(".4. Fourth", html, StringComparison.Ordinal);
    }
}
