using Scheduler.Core.Accounting;
using Scheduler.Core.Health;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;
using Scheduler.Host.State;
using Scheduler.Web.Views;

namespace Scheduler.Host.PageActions;

/// <summary>
/// What the page snapshot needs that Host keeps beside the tick rather than in it (450-B): the
/// health check, accounting's runs, the finished-card flags, the notice and the planner switch.
/// Each is optional; a missing one leaves its window as it is with nothing to show.
/// </summary>
public sealed record PageSnapshotSources
{
    /// <summary>The health check of main (HLTH-5, HLTH-6), or null when the project has none.</summary>
    public HealthCheck? Health { get; init; }

    /// <summary>The last finished runs, newest first (ACCT-5), for Recent events.</summary>
    public Func<IReadOnlyList<RecentEvent>>? Recent { get; init; }

    /// <summary>Accounting's runs (ACCT-6), for the Control window's stats.</summary>
    public Func<IReadOnlyList<RunStat>>? Runs { get; init; }

    /// <summary>
    /// Accounting's own 14-day read of <c>accounting.csv</c> (TREND-1, 770-B), which reaches further
    /// back than <see cref="Runs"/>; Host's <c>AccountingCsv.TrendRuns</c>. Absent, the Trends window
    /// falls back to <see cref="Runs"/>, as it did before the read had a home.
    /// </summary>
    public Func<IReadOnlyList<RunStat>>? TrendRuns { get; init; }

    /// <summary>
    /// The cards whose run finished without updating them (395-B, 410-A), each with the folder and
    /// blob main showed when it was flagged - Host's <c>FinishedCardCheck.Flagged</c>. Without the
    /// folder and stamp the page's row can never leave, not even after Done.
    /// </summary>
    public Func<IReadOnlyCollection<FinishedCardFlag>>? FinishedWithoutUpdate { get; init; }

    /// <summary>
    /// The generic files whose copy was edited in this project (roadmap item 1, job card 580-A), as
    /// <c>TemplateRefresh.Edited</c> holds them; null when the project keeps no templates.
    /// </summary>
    public Func<IReadOnlyCollection<PageTemplateEdit>>? EditedTemplates { get; init; }

    /// <summary>The notice of a failed page action (MOVE-11).</summary>
    public PageNotice? Notice { get; init; }

    /// <summary>Whether planner push is on, or null when the project has no planner outbox (MP-9).</summary>
    public Func<bool>? PlannerPush { get; init; }

    /// <summary>A running worker's live stats by slot (PAGE-10, 460-B), for its window's stats line.</summary>
    public Func<int, WorkerStats?>? Stats { get; init; }

    /// <summary>
    /// 964-B: renders what the running workers have added to their raw logs since the last read,
    /// called each time the live feed builds (PAGE-3), so a window grows between ticks; null renders
    /// only at the tick's watch.
    /// </summary>
    public Action? LiveRender { get; init; }

    /// <summary>models.json: by model, its other names, the short one first (DISP-18), for the worker window's title (510-B).</summary>
    public Func<IReadOnlyDictionary<string, IReadOnlyList<string>>>? Models { get; init; }

    /// <summary>
    /// Whether the worker types the tick holds came from main's <c>workers.json</c> (WT-C7, 974-B).
    /// False until the scheduler has read it: the Worker types panel then shows no types rather than
    /// the ones the slot migration makes, so a restart cannot show a <c>worker</c> in place of a saved
    /// name. Null, as a host that gates nothing has it: the types are shown as they are.
    /// </summary>
    public Func<bool>? WorkersFromMain { get; init; }

    /// <summary>
    /// 990-B: the moves the page's own actions have committed, laid over the tick's cards until its
    /// next card read shows them, so an answered or finished card never comes back in between.
    /// </summary>
    public PageCardOverlay? Overlay { get; init; }
}
