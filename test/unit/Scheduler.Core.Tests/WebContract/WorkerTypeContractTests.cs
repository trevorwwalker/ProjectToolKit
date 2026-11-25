// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.WebContract;

/// <summary>The page contract for worker types (WT-P3, WT-P4, WT-9): views, and the two new actions.</summary>
public class WorkerTypeContractTests
{
    private sealed class OldActions : IPageActions
    {
        public ActionResult Pause() => ActionResult.Queued(1);
        public ActionResult Resume() => ActionResult.Queued(1);
        public ActionResult HealthNow() => ActionResult.Queued(1);
        public ActionResult Drain() => ActionResult.Queued(1);
        public ActionResult PlannerToggle() => ActionResult.Queued(1);
        public ActionResult StopAll() => ActionResult.Queued(1);
        public ActionResult Tick() => ActionResult.Queued(1);
        public ActionResult Send(int slot, string message) => ActionResult.Queued(1);
        public ActionResult Answer(string card, int question, string choice, string note) => ActionResult.Queued(1);
        public ActionResult OpenCategories() => ActionResult.Queued(1);
        public ActionResult OpenWorkers() => ActionResult.Queued(1);
        public ActionResult AcceptCategory(string category) => ActionResult.Queued(1);
        public ActionResult Open(string card) => ActionResult.Queued(1);
        public ActionResult ResumeCard(string card) => ActionResult.Queued(1);
        public ActionResult Done(string card) => ActionResult.Queued(1);
        public ActionResult Retry(string card) => ActionResult.Queued(1);
        public ActionResult Stop(int slot) => ActionResult.Queued(1);
        public ActionResult Kill(int slot) => ActionResult.Queued(1);
        public ActionResult TemplateOverwrite(string project, string file) => ActionResult.Queued(1);
        public ActionResult TemplateKeep(string project, string file) => ActionResult.Queued(1);
    }

    // WT-P4: a host that has not learned the new actions refuses them in words instead of failing to build.
    [Fact]
    public void Page_NewRoutes_RefusedUntilHostImplementsThem()
    {
        IPageActions actions = new OldActions();

        Assert.False(actions.SaveWorkerTypes(2, []).Ok);
        Assert.False(actions.ResumeAgent("claude").Ok);
    }

    // WT-9: the page's Resume clears every agent's mark, and acts on the state as before.
    [Fact]
    public void TypeDispatch_PageResume_ClearsEveryAgent_StateAsBefore()
    {
        var quota = new AgentQuota();
        quota.Mark("claude", DateTimeOffset.UnixEpoch);
        quota.Mark("copilot", DateTimeOffset.UnixEpoch);

        Assert.Equal(SchedulerState.Running, PageResume.Apply(SchedulerState.Paused, quota));
        Assert.Empty(quota.Agents);
        Assert.Equal(SchedulerState.Draining, PageResume.Apply(SchedulerState.Draining, quota));
    }
}
