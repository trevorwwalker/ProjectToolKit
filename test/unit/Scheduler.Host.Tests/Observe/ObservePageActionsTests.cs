using Scheduler.Core.WebContract;
using Scheduler.Host.Observe;

namespace Scheduler.Host.Tests.Observe;

/// <summary>The observe run's page does only what changes its own state (card 390-B, roadmap item 1).</summary>
public sealed class ObservePageActionsTests
{
    private sealed class Inner : IPageActions
    {
        public List<string> Calls { get; } = [];

        private ActionResult Ok(string name)
        {
            Calls.Add(name);
            return ActionResult.Queued(1);
        }

        public ActionResult Pause() => Ok("pause");

        public ActionResult Resume() => Ok("resume");

        public ActionResult HealthNow() => Ok("health");

        public ActionResult Drain() => Ok("drain");

        public ActionResult PlannerToggle() => Ok("planner");

        public ActionResult StopAll() => Ok("stopall");

        public ActionResult Tick() => Ok("tick");

        public ActionResult Send(int slot, string message) => Ok("send");

        public ActionResult Answer(string card, int question, string choice, string note) => Ok("answer");

        public ActionResult OpenCategories() => Ok("openlanes");

        public ActionResult OpenWorkers() => Ok("openworkers");

        public ActionResult AcceptCategory(string category) => Ok("acceptlane");

        public ActionResult TemplateOverwrite(string project, string file) => Ok("templateoverwrite");

        public ActionResult TemplateKeep(string project, string file) => Ok("templatekeep");

        public ActionResult Open(string card) => Ok("open");

        public ActionResult ResumeCard(string card) => Ok("resumecard");

        public ActionResult Done(string card) => Ok("done");

        public ActionResult Retry(string card) => Ok("retry");

        public ActionResult Stop(int slot) => Ok("stop");

        public ActionResult Kill(int slot) => Ok("kill");
    }

    [Fact]
    public void Pause_resume_drain_and_tick_are_done_and_the_rest_refused()
    {
        // PAGE-9 (the page's actions), roadmap item 1: commits, worker files and processes are the old scheduler's
        var inner = new Inner();
        IPageActions page = new ObservePageActions(inner);

        Assert.True(page.Pause().Ok);
        Assert.True(page.Resume().Ok);
        Assert.True(page.Drain().Ok);
        Assert.True(page.Tick().Ok);
        ActionResult[] refused =
        [
            page.HealthNow(), page.PlannerToggle(), page.StopAll(), page.Send(1, "x"), page.Answer("c", 1, "yes", ""),
            page.OpenCategories(), page.OpenWorkers(), page.AcceptCategory("A"), page.Open("c"),
            page.ResumeCard("c"), page.Done("c"), page.Retry("c"), page.Stop(1), page.Kill(1),
        ];

        Assert.Equal(["pause", "resume", "drain", "tick"], inner.Calls);
        Assert.All(refused, r => Assert.Equal(ObservePageActions.Refused, r.Error));
    }
}
