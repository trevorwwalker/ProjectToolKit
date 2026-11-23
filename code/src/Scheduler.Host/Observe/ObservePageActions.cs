using Scheduler.Core.WebContract;

namespace Scheduler.Host.Observe;

/// <summary>
/// The observe run's page (roadmap item 1): only what changes the run's own state is done (PAGE-9
/// pause, resume, drain and tick). Everything that would commit to main, write a worker's files or
/// end a process belongs to the old scheduler and is refused.
/// </summary>
public sealed class ObservePageActions(IPageActions inner) : IPageActions
{
    /// <summary>The answer to every refused action.</summary>
    public const string Refused = "observe run: this page only pauses, resumes, drains and ticks; use the old scheduler's page";

    private static ActionResult No() => ActionResult.Failed(Refused);

    /// <inheritdoc />
    public ActionResult Pause() => inner.Pause();

    /// <inheritdoc />
    public ActionResult Resume() => inner.Resume();

    /// <inheritdoc />
    public ActionResult HealthNow() => No();

    /// <inheritdoc />
    public ActionResult Drain() => inner.Drain();

    /// <inheritdoc />
    public ActionResult PlannerToggle() => No();

    /// <inheritdoc />
    public ActionResult StopAll() => No();

    /// <inheritdoc />
    public ActionResult Tick() => inner.Tick();

    /// <inheritdoc />
    public ActionResult Send(int slot, string message) => No();

    /// <inheritdoc />
    public ActionResult Answer(string card, int question, string choice, string note) => No();

    /// <inheritdoc />
    public ActionResult OpenCategories() => No();

    /// <inheritdoc />
    public ActionResult OpenWorkers() => No();

    /// <inheritdoc />
    public ActionResult AcceptCategory(string category) => No();

    /// <inheritdoc />
    public ActionResult TemplateOverwrite(string project, string file) => No();

    /// <inheritdoc />
    public ActionResult TemplateKeep(string project, string file) => No();

    /// <inheritdoc />
    public ActionResult Open(string card) => No();

    /// <inheritdoc />
    public ActionResult ResumeCard(string card) => No();

    /// <inheritdoc />
    public ActionResult Done(string card) => No();

    /// <inheritdoc />
    public ActionResult Retry(string card) => No();

    /// <inheritdoc />
    public ActionResult Stop(int slot) => No();

    /// <inheritdoc />
    public ActionResult Kill(int slot) => No();
}
