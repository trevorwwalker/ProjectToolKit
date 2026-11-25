// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>
/// One method per POST route of PAGE-9. Each queues the action for the scheduler thread (PAGE-1)
/// and returns at once. Web calls it; Host implements it.
/// </summary>
public interface IPageActions
{
    /// <summary><c>/pause</c> (running only).</summary>
    ActionResult Pause();

    /// <summary><c>/resume</c> (paused states only; MOVE-8 clears retries).</summary>
    ActionResult Resume();

    /// <summary><c>/health-now</c>.</summary>
    ActionResult HealthNow();

    /// <summary><c>/drain</c>.</summary>
    ActionResult Drain();

    /// <summary><c>/planner-toggle</c>.</summary>
    ActionResult PlannerToggle();

    /// <summary><c>/stopall</c>.</summary>
    ActionResult StopAll();

    /// <summary>
    /// A pool row's Stop (card 1004-C): this project's running workers are asked to stop, as Stop all
    /// asks every worker, and the project pauses so it starts none; the scheduler does not exit.
    /// Refused where a page has no such action (the observer's).
    /// </summary>
    ActionResult StopWorkers() => ActionResult.Failed("stop for one project is a pool page action");

    /// <summary><c>/tick</c> (debounced, PAGE-4).</summary>
    ActionResult Tick();

    /// <summary><c>/send</c>: a message to a running worker.</summary>
    ActionResult Send(int slot, string message);

    /// <summary><c>/answer</c>: answer a card question (MOVE-2).</summary>
    ActionResult Answer(string card, int question, string choice, string note);

    /// <summary>1125-C: <c>/plan-mark</c>: cycle the mark of a plan (step "plan") or step, automatic to checked to cleared, in jobs/plan-marks.json.</summary>
    ActionResult PlanMark(string plan, string step) => ActionResult.Failed("plan marks are set on the project page");

    /// <summary><c>/open-categories</c>.</summary>
    ActionResult OpenCategories();

    /// <summary><c>/open-workers</c>.</summary>
    ActionResult OpenWorkers();

    /// <summary><c>/open</c>: open a card (MOVE-9).</summary>
    ActionResult Open(string card);

    /// <summary><c>/resume-card</c> (MOVE-7).</summary>
    ActionResult ResumeCard(string card);

    /// <summary>1056-B: <c>/resume-card</c> with the developer's note, kept in the card's <c>## Dev</c> section.</summary>
    ActionResult ResumeCard(string card, string note) => ResumeCard(card);

    /// <summary><c>/done</c> (MOVE-6).</summary>
    ActionResult Done(string card);

    /// <summary><c>/retry</c> (MOVE-8).</summary>
    ActionResult Retry(string card);

    /// <summary>1099-C: <c>/done</c> with the developer's note, kept in the card's <c>## Dev</c> section.</summary>
    ActionResult Done(string card, string note) => Done(card);

    /// <summary>1099-C: <c>/retry</c> with the developer's note, kept in the card's <c>## Dev</c> section.</summary>
    ActionResult Retry(string card, string note) => Retry(card);

    /// <summary>1099-C: <c>/note</c>: the developer's note filed in the card's <c>## Dev</c> section; the card does not move.</summary>
    ActionResult SaveNote(string card, string note) => ActionResult.Failed("a note cannot be saved from this page");

    /// <summary><c>/stop</c>: stop a worker cleanly.</summary>
    ActionResult Stop(int slot);

    /// <summary><c>/kill</c>: kill a worker process tree.</summary>
    ActionResult Kill(int slot);

    /// <summary>
    /// <c>/template-overwrite</c>: the edited copy <paramref name="file"/> of a generic file in
    /// <paramref name="project"/> takes the toolkit's template (roadmap item 1, job card 580-A).
    /// </summary>
    ActionResult TemplateOverwrite(string project, string file);

    /// <summary>
    /// <c>/template-keep</c>: leave <paramref name="project"/>'s copy <paramref name="file"/> as the
    /// project has it and stop asking until the toolkit's template changes again (roadmap item 1).
    /// </summary>
    ActionResult TemplateKeep(string project, string file);

    /// <summary>
    /// <c>/worker-types</c> (WT-P4): the Worker types panel's Save - the total and the types in order.
    /// Host implements it with the pool form of workers.json (WT-C7, WT-C11); until it does, it fails.
    /// A row whose <see cref="PageWorkerTypeView.Name"/> differs from its
    /// <see cref="PageWorkerTypeView.OriginalName"/> is a rename, not a new type: Host then moves the
    /// open cards that name the old name with it, in the same commit, and refuses to remove a type that
    /// open cards still name (947-B).
    /// </summary>
    ActionResult SaveWorkerTypes(int maxWorkers, IReadOnlyList<PageWorkerTypeView> types) =>
        SaveWorkerTypes(maxWorkers, types, "");

    /// <summary>
    /// 1146-A: <see cref="SaveWorkerTypes(int, IReadOnlyList{PageWorkerTypeView})"/> that also sets the
    /// pool's <c>defaultWorker</c>, the type that takes cards with no <c>Worker:</c> line (WT-3).
    /// <paramref name="defaultWorker"/> names a row by its new name; "" keeps the current default
    /// (following it through a rename). A default that names no row is refused.
    /// </summary>
    ActionResult SaveWorkerTypes(int maxWorkers, IReadOnlyList<PageWorkerTypeView> types, string defaultWorker) =>
        ActionResult.Failed("worker types cannot be saved by this scheduler yet");

    /// <summary><c>/agent-resume?agent=&lt;name&gt;</c> (WT-9): clears that agent's out-of-quota mark.</summary>
    ActionResult ResumeAgent(string agent) =>
        ActionResult.Failed("agents cannot be resumed by this scheduler yet");

    /// <summary>
    /// <c>/categories</c> (992-B): the Categories panel's Save - the categories in order and the shared
    /// add-only list. Host validates them, writes the categories file and commits it as Worker types Save
    /// does; a row whose <see cref="PageCategory.Letter"/> differs from its
    /// <see cref="PageCategory.OriginalLetter"/> changes that category's letter, and a category that open
    /// cards use can neither be removed nor have its letter changed (992-B Q1).
    /// </summary>
    ActionResult SaveCategories(IReadOnlyList<PageCategory> categories, IReadOnlyList<string> addOnly) =>
        ActionResult.Failed("categories cannot be saved by this scheduler yet");
}
