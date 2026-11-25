// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1207-C: the pool page's "Requests to the planner" window, and /request with project= in a pool.</summary>
public sealed class PoolRequestsWindowTests
{
    private static JobRequest Req(string name, string title, string forWho, bool open = true) => new()
    {
        Path = "jobs/requests/" + name + ".md",
        Name = name,
        Title = title,
        For = forWho,
        Open = open,
    };

    private static PageSnapshot Project(string name, IReadOnlyList<JobRequest> requests, IReadOnlyList<string>? waiting = null) =>
        ViewsFixture.Full() with
        {
            Project = name,
            Planner = new PagePlannerView { OpenRequests = requests, WaitingCards = waiting ?? [] },
        };

    private static string Render(params PageSnapshot[] projects) =>
        new CombinedPageRenderer().Render(projects, "", remote: false, controlsAllowed: true);

    [Fact]
    public void TwoProjects_OneRowPerOpenRequest_NewestFirst_WithProjectLinks()
    {
        var html = Render(
            Project("AmigaX", [Req("12-first", "First ask", "the pool"), Req("30-third", "Third ask", "planner"), Req("20-old", "Answered", "x", open: false)]),
            Project("Other", [Req("25-second", "Second ask", "me")]));

        Assert.Contains("id='w-requests'", html, StringComparison.Ordinal);
        Assert.Contains("<span class='tag rq-tag'>3 open</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Answered", html, StringComparison.Ordinal);
        var third = html.IndexOf("/request?name=30-third&project=AmigaX", StringComparison.Ordinal);
        var second = html.IndexOf("/request?name=25-second&project=Other", StringComparison.Ordinal);
        var first = html.IndexOf("/request?name=12-first&project=AmigaX", StringComparison.Ordinal);
        Assert.True(third >= 0 && second > third && first > second, "newest number first");
        Assert.Contains("data-project='Other'", html[second..], StringComparison.Ordinal);
        Assert.Contains("class='chip projchip'", html[(html.IndexOf("id='w-requests'", StringComparison.Ordinal))..], StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingCards_AreListed_AndCountedInTheTag()
    {
        var html = Render(Project("AmigaX", [Req("5-ask", "Ask", "pool")], ["77-B-needs-plan.md"]), Project("Other", []));

        Assert.Contains("<span class='tag rq-tag'>1 open, 1 card</span>", html, StringComparison.Ordinal);
        Assert.Contains("Card waiting on the planner", html, StringComparison.Ordinal);
        Assert.Contains("/card?project=AmigaX&name=77-B-needs-plan.md", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tables_AreTaggedForTheFilter_RowsCarryTheirProject_AndTheEmptyLineIsHidden()
    {
        var html = Render(Project("AmigaX", [Req("5-ask", "Ask", "pool")], ["77-B-needs-plan.md"]), Project("ProjectToolkit", []));
        var win = html[html.IndexOf("id='w-requests'", StringComparison.Ordinal)..];

        Assert.Contains("<table class='rq-open'>", win, StringComparison.Ordinal);
        Assert.Contains("<table class='rq-waiting'", win, StringComparison.Ordinal);
        Assert.Contains("<tr data-project='AmigaX'><td class='nw projcol'>", win, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project='ProjectToolkit'", win[..win.IndexOf("</section>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("<p class='mute rq-none' hidden></p>", win, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterScript_HidesEmptyTables_RecountsTheTag_AndSaysNothingForTheProject()
    {
        var html = Render(Project("AmigaX", [Req("5-ask", "Ask", "pool")]), Project("ProjectToolkit", []));

        Assert.Contains("applyRequests(v);", html, StringComparison.Ordinal);
        Assert.Contains("t.classList.toggle('projhide', v !== 'all' && n === 0)", html, StringComparison.Ordinal);
        Assert.Contains("'Nothing for the planner in ' + v + '.'", html, StringComparison.Ordinal);
        Assert.Contains("counts.waiting === 1 ? ' card' : ' cards'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingOpenOrWaiting_NoWindow()
    {
        var html = Render(Project("AmigaX", [Req("1-done", "Done", "x", open: false)]), Project("Other", []));

        Assert.DoesNotContain("id='w-requests'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_WithProject_ServesTheProjectsRequest_InAPool()
    {
        var poolData = new FakePageData();
        var data = new PoolData(poolData);
        var actions = new PoolActions();
        foreach (var name in new[] { "AmigaX", "Other" })
        {
            data.Projects[name] = new FakePageData();
            data.Projects[name].Requests["12-first.md"] = "request of " + name;
            actions.Projects[name] = new FakePageActions();
        }

        var wide = new FakePageActions();
        using var queue = new PageActionQueue();
        var router = new PageRouter(poolData, wide, new FakePageRenderer(), new LiveFeed(new TestClock(), poolData), remoteControl: false, queue,
            new PoolRouting(data, actions, new PoolPage()));

        var response = router.Handle(new PageRequest { Method = "GET", Path = "/request", QueryValues = PageRequest.ParseQuery("name=12-first.md&project=Other") });

        Assert.Equal(200, response.Status);
        Assert.Contains("request of Other", response.Body, StringComparison.Ordinal);
    }

    private sealed class PoolData(FakePageData pool) : IPoolPageData
    {
        public Dictionary<string, FakePageData> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? FontPath => pool.FontPath;

        public string LogDirectory => pool.LogDirectory;

        public PoolPageSnapshot PoolSnapshot() => new() { Built = DateTimeOffset.UnixEpoch };

        public IPageData? For(string project) => Projects.GetValueOrDefault(project);

        public PageSnapshot Snapshot() => pool.Snapshot();

        public string? Card(string name) => pool.Card(name);

        public string? Request(string name) => pool.Request(name);

        public string? HealthText() => pool.HealthText();

        public string StatusJson() => pool.StatusJson();

        public IReadOnlyList<LiveWorker> RunningWorkers() => pool.RunningWorkers();
    }

    private sealed class PoolActions : IPoolPageActions
    {
        public Dictionary<string, FakePageActions> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> IPoolPageActions.Projects => [.. Projects.Keys];

        public IPageActions? For(string project) => Projects.GetValueOrDefault(project);

        public ActionResult Pause(string? project) => new() { Ok = true };

        public ActionResult Drain(string? project) => new() { Ok = true };

        public ActionResult Resume(string? project) => new() { Ok = true };

        public ActionResult StopAll() => new() { Ok = true };

        public ActionResult Stop(string project) => new() { Ok = true };

        public ActionResult SetWorkspaces(string project, string value) => new() { Ok = true };
    }

    private sealed class PoolPage : IPoolPageRenderer
    {
        public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed) => "the pool page";

        public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed, string? view) => "the pool page";
    }
}
