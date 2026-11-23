using Scheduler.Core.Categories;
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Core.Tick;
using Scheduler.Host.Moves;

namespace Scheduler.Host.PageActions;

/// <summary>
/// Everything the page's POST handlers (280-B) call: the tick whose state they change, the ports
/// they write through, and the collaborators the composition has. The optional ones leave their
/// routes refusing rather than pretending: a scheduler without a health check cannot run one now.
/// </summary>
public sealed class PageActionParts
{
    /// <summary>The configuration; its log folder holds the msg and stop flag files.</summary>
    public required SchedulerConfig Config { get; init; }

    /// <summary>The clock the message lines are stamped with.</summary>
    public required IClock Clock { get; init; }

    /// <summary>Where the scheduler log writes.</summary>
    public required ISchedulerLog Log { get; init; }

    /// <summary>The state files, for the accepted category definitions (LANE-3).</summary>
    public required IStateFiles Files { get; init; }

    /// <summary>The running workers, the state, the enabled slots, the categories and the workers.</summary>
    public required SchedulerTick Tick { get; init; }

    /// <summary>Ends a process tree (TIME-11, TIME-12).</summary>
    public required IWorkerProcesses Processes { get; init; }

    /// <summary>Every commit to main, through the answers worktree (MOVE-1, MOVE-10).</summary>
    public required IJobsCommitter Committer { get; init; }

    /// <summary>Reads main, for the text a rewritten file is compared and matched with (MOVE-10).</summary>
    public required IMainRepository Main { get; init; }

    /// <summary>Asks the loop for a tick at once (PAGE-4).</summary>
    public required Action TickNow { get; init; }

    /// <summary>Shows a notice at the top of the page (MOVE-11), when the composition keeps one.</summary>
    public Action<string>? Notice { get; init; }

    /// <summary>An action was accepted, which clears the notice (MOVE-11), when the composition keeps one.</summary>
    public Action? Succeeded { get; init; }

    /// <summary>The health check, for <c>/health-now</c>; null when none is set.</summary>
    public HealthCheck? Health { get; init; }

    /// <summary>Brings a card to the front (MOVE-9); null when nothing can open a file here.</summary>
    public CardOpener? Opener { get; init; }

    /// <summary>Opens categories.json or workers.json; null when nothing can open a file here.</summary>
    public IFileLauncher? Launcher { get; init; }

    /// <summary>The planner's push switch, for <c>/planner-toggle</c>; null when no planner is running.</summary>
    public Func<bool>? PlannerPushGet { get; init; }

    /// <summary>The generic files kept in step with the toolkit (roadmap item 1, 540-B), for <c>/template-overwrite</c> and <c>/template-keep</c>; null in an observe or dry run.</summary>
    public Templates.TemplateRefresh? Templates { get; init; }

    /// <summary>Saves the planner's push switch (PLAN rules); null when no planner is running.</summary>
    public Action<bool>? PlannerPushSet { get; init; }

    /// <summary>workers.json's path on main.</summary>
    public string WorkersRel => $"{Config.JobsDir}/workers.json";

    /// <summary>
    /// Whether the worker types the panel shows came from main's <c>workers.json</c> (WT-C7, 974-B);
    /// while it is false the panel's Save is refused, so a start-up that has not reached main cannot
    /// write the slot migration into the file. Null, as a host that gates nothing has it: Save goes
    /// ahead as before.
    /// </summary>
    public Func<bool>? WorkersFromMain { get; init; }

    /// <summary>categories.json's path on main.</summary>
    /// <summary>990-B: where each committed card move is noted for the snapshot, before the tick reads it.</summary>
    public PageCardOverlay? Overlay { get; init; }

    public string CategoriesRel =>CategoriesLocation.Rel(Config.JobsDir, Main.ReadFile);
}
