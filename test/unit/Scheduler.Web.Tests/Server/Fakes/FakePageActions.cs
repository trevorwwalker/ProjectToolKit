// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Tests.Server.Fakes;

/// <summary>
/// A fake of the page contract that records which route reached it. The state, slot and answer
/// rules it applies are the ones Host will implement (schedule.ps1 Handle); the Web tests only
/// check that the route arrives here with the right arguments.
/// </summary>
internal sealed class FakePageActions : IPageActions
{
    private int _nextId = 1;

    /// <summary>How many times each POST route reached this fake.</summary>
    public Dictionary<string, int> Calls { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The scheduler state the fake guards its actions with, as today.</summary>
    public SchedulerState State { get; set; } = SchedulerState.Running;

    /// <summary>When set, every call waits here first: the scheduler thread is busy.</summary>
    public ManualResetEventSlim? BlockOn { get; set; }

    /// <summary>The last card name any card route carried.</summary>
    public string? LastCard { get; set; }

    /// <summary>The last slot number any slot route carried.</summary>
    public int? LastSlot { get; set; }

    /// <summary>The last message /send carried.</summary>
    public string? LastMessage { get; set; }

    /// <summary>The last answer as <c>card|question|choice|note</c>.</summary>
    public string? LastAnswer { get; set; }

    /// <summary>The last category /accept-category carried.</summary>

    /// <summary>The last template action as <c>project|file</c>, from /template-overwrite or /template-keep (580-A).</summary>
    public string? LastTemplate { get; set; }

    /// <summary>
    /// The last Worker types panel Save carried, as <c>total|name:max:agent:model:endpoint:metrics:projects;...</c>
    /// (WT-P3, WT-P4).
    /// </summary>
    public string? LastWorkerTypes { get; set; }

    /// <summary>1147-C: the default type the last Worker types Save named.</summary>
    public string? LastDefaultWorker { get; set; }

    /// <summary>
    /// The name each Worker types panel row was loaded as, <c>;</c> separated in row order (947-B):
    /// what tells a rename from a type removed and another added. Empty for a row Add type made.
    /// </summary>
    public string? LastWorkerTypesOriginal { get; set; }

    /// <summary>The agent /agent-resume cleared the out-of-quota mark for (WT-9).</summary>
    public string? LastAgentResumed { get; set; }

    public int CallCount(string path) => Calls.GetValueOrDefault(path);

    private ActionResult Run(string path, Action? apply = null)
    {
        // A test blocks the scheduler's thread either everywhere (the loop is busy) or on one route
        // (one click's git work is slow while the rest of the page stays quick).
        if (BlockOn is { } gate && (Block.Count == 0 || Block.Contains(path)))
        {
            gate.Wait();
        }

        Calls[path] = Calls.GetValueOrDefault(path) + 1;
        lock (_order)
        {
            Order.Add(path);
        }

        apply?.Invoke();
        return Fails.TryGetValue(path, out var why)
            ? ActionResult.Failed(why)
            : ActionResult.Queued(_nextId++);
    }

    /// <summary>The routes in the order their work actually ran (957-B: one queue, click order).</summary>
    public List<string> Order { get; } = [];

    /// <summary>The routes that answer with a failure instead of accepting, with the message (957-B).</summary>
    public Dictionary<string, string> Fails { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set with <see cref="BlockOn"/>, only these routes wait; empty blocks every one.</summary>
    public HashSet<string> Block { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The answers, in the order they reached the scheduler (957-B item 5).</summary>
    public List<string> Answers { get; } = [];

    /// <summary>The routes in the order their work ran, comma separated.</summary>
    public string Ran()
    {
        lock (_order)
        {
            return string.Join(",", Order);
        }
    }

    private readonly List<string> _order = [];

    public ActionResult Pause() => Run("/pause", () =>
    {
        if (State == SchedulerState.Running)
        {
            State = SchedulerState.Paused;
        }
    });

    public ActionResult Resume() => Run("/resume", () =>
    {
        if (State is SchedulerState.Paused or SchedulerState.PausedRed or SchedulerState.PausedQuota)
        {
            State = SchedulerState.Running;
        }
    });

    public ActionResult HealthNow() => Run("/health-now");

    public ActionResult Drain() => Run("/drain", () => State = SchedulerState.Draining);

    public ActionResult PlannerToggle() => Run("/planner-toggle");

    public ActionResult StopAll() => Run("/stopall", () => State = SchedulerState.Draining);

    public ActionResult Tick() => Run("/tick");

    public ActionResult Send(int slot, string message) => Run("/send", () =>
    {
        LastSlot = slot;
        LastMessage = message;
    });

    public ActionResult Answer(string card, int question, string choice, string note) => Run("/answer", () =>
    {
        LastAnswer = $"{card}|{question}|{choice}|{note}";
        lock (Answers)
        {
            Answers.Add(LastAnswer);
        }
    });

    public string? LastPlanMark { get; set; }

    public ActionResult PlanMark(string plan, string step) => Run("/plan-mark", () => LastPlanMark = plan + "|" + step);

    public ActionResult OpenCategories() => Run("/open-lanes");

    public ActionResult OpenWorkers() => Run("/open-workers");

    public ActionResult TemplateOverwrite(string project, string file) => Run("/template-overwrite", () => LastTemplate = $"{project}|{file}");

    public ActionResult TemplateKeep(string project, string file) => Run("/template-keep", () => LastTemplate = $"{project}|{file}");

    public ActionResult Open(string card) => Run("/open", () => LastCard = card);

    public string? LastNote { get; set; }

    public ActionResult ResumeCard(string card) => Run("/resume-card", () => LastCard = card);

    public ActionResult ResumeCard(string card, string note) => Run("/resume-card", () => { LastCard = card; LastNote = note; });

    public ActionResult Done(string card) => Run("/done", () => LastCard = card);

    public ActionResult Retry(string card) => Run("/retry", () => LastCard = card);

    public ActionResult Done(string card, string note) => Run("/done", () => { LastCard = card; LastNote = note; });

    public ActionResult Retry(string card, string note) => Run("/retry", () => { LastCard = card; LastNote = note; });

    public ActionResult SaveNote(string card, string note) => Run("/note", () => { LastCard = card; LastNote = note; });

    public ActionResult Stop(int slot) => Run("/stop", () => LastSlot = slot);

    public ActionResult Kill(int slot) => Run("/kill", () => LastSlot = slot);

    // WT-P4: the Worker types panel's Save and one agent's Resume.
    public ActionResult SaveWorkerTypes(int maxWorkers, IReadOnlyList<PageWorkerTypeView> types, string defaultWorker) =>
        Run("/worker-types", () =>
        {
            LastDefaultWorker = defaultWorker;
            LastWorkerTypes = maxWorkers + "|" + string.Join(
                ";",
                types.Select(type => $"{type.Name}:{type.Max}:{type.Agent}:{type.Model}:{type.Endpoint}:{type.MetricsUrl}:{string.Join(",", type.Projects)}"));
            LastWorkerTypesOriginal = string.Join(";", types.Select(type => type.OriginalName));
        });

    public ActionResult ResumeAgent(string agent) => Run("/agent-resume", () => LastAgentResumed = agent);

    // 992-B: the Categories panel's Save, as "letter(orig):name:purpose:colour:owns|except" rows and the add-only list.
    public ActionResult SaveCategories(IReadOnlyList<PageCategory> categories, IReadOnlyList<string> addOnly) =>
        Run("/categories", () =>
        {
            LastCategories = string.Join(";", categories.Select(c => $"{c.Letter}({c.OriginalLetter}):{c.Name}:{c.Purpose}:{c.Color}:{string.Join(",", c.Owns)}|{string.Join(",", c.Except)}"));
            LastAddOnly = string.Join(",", addOnly);
        });

    public string? LastCategories { get; private set; }

    public string? LastAddOnly { get; private set; }
}
