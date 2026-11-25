// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

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

    // 1171-C: in a pool the click names its project in the form (as the page posts it, with the fetch header),
    // and only that project's action runs; the page's answer is the queue's id.
    [Theory]
    [InlineData("AmigaX", "ProjectToolkit")]
    [InlineData("ProjectToolkit", "AmigaX")]
    public void Card1171_InAPool_TheProjectInTheForm_ChoosesWhoseMarkIsSet(string project, string other)
    {
        using var pool = new Pool();

        var response = pool.Router.Handle(new PageRequest
        {
            Method = "POST",
            Path = "/plan-mark",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Requested-With"] = "fetch" },
            FormValues = PageRequest.ParseForm("project=" + project + "&plan=jobs%2Fplans%2Fx_plan.md&step=3.1"),
        });

        Assert.Equal(202, response.Status);
        Assert.StartsWith("{\"id\":", response.Body, StringComparison.Ordinal);
        Assert.True(pool.Queue.WaitIdle(TimeSpan.FromSeconds(10)));
        Assert.Equal("jobs/plans/x_plan.md|3.1", pool.Actions.Projects[project].LastPlanMark);
        Assert.Equal(0, pool.Actions.Projects[other].CallCount("/plan-mark"));
    }

    // 1171-C: without project= a pool answers "which project?" (the page then says so on its notice line).
    [Fact]
    public void Card1171_InAPool_NoProject_IsRefused_AndNothingRuns()
    {
        using var pool = new Pool();

        var response = pool.Router.Handle(new PageRequest
        {
            Method = "POST",
            Path = "/plan-mark",
            FormValues = PageRequest.ParseForm("plan=jobs%2Fplans%2Fx_plan.md&step=3.1"),
        });

        Assert.Equal(400, response.Status);
        Assert.StartsWith("which project?", response.Body, StringComparison.Ordinal);
        Assert.True(pool.Queue.WaitIdle(TimeSpan.FromSeconds(10)));
        Assert.All(pool.Actions.Projects.Values, a => Assert.Equal(0, a.CallCount("/plan-mark")));
    }

    private sealed class Pool : IDisposable
    {
        private readonly FakePageData _poolData = new();

        public Pool()
        {
            var data = new PoolData(_poolData);
            foreach (var name in new[] { "AmigaX", "ProjectToolkit" })
            {
                data.Projects[name] = new FakePageData();
                Actions.Projects[name] = new FakePageActions();
            }

            Router = new PageRouter(_poolData, new FakePageActions(), new FakePageRenderer(), new LiveFeed(new TestClock(), _poolData),
                remoteControl: false, Queue, new PoolRouting(data, Actions, new PoolRenderer()));
        }

        public PageActionQueue Queue { get; } = new();

        public PoolActions Actions { get; } = new();

        public PageRouter Router { get; }

        public void Dispose() => Queue.Dispose();
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

    private sealed class PoolRenderer : IPoolPageRenderer
    {
        public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed) => "the pool page";

        public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed, string? view) => "the pool page";
    }
}
