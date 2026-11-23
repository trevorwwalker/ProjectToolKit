using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Planner;
using Scheduler.Core.Watchdog;

namespace Scheduler.Core.Pool;

/// <summary>
/// What stays per project in the shared pool (MP-9): its repository, its own settings (health
/// command, maxRetries, maxMinutes, answered per project in spec B3 Q14) and its planner outbox.
/// </summary>
/// <param name="Name">The project's name in the top-level config.</param>
/// <param name="Repo">The project's repository; its health check and gates run there only.</param>
/// <param name="Config">The project's own settings.</param>
/// <param name="Planner">Its planner outbox, or null when it has none (PLAN-1).</param>
public sealed record PoolProjectSetup(string Name, string Repo, SchedulerConfig Config, PlannerSettings? Planner)
{
    /// <summary>The project has a health command, so the page shows its health state (MP-9).</summary>
    public bool ShowsHealthState => !string.IsNullOrEmpty(Config.HealthCommand);

    /// <summary>The project has a planner outbox, so the page shows its planner line (MP-9).</summary>
    public bool ShowsPlannerLine => Planner is not null;

    /// <summary>MP-9, DISP-7: the project's dispatch plan, stuck orphans judged by its own maxRetries (Q14).</summary>
    public DispatchPlan Plan(
        IReadOnlyList<Card> cards,
        IReadOnlyCollection<RunningWorker> running,
        IReadOnlyDictionary<string, int> retries,
        CategoriesFile? categories) =>
        DispatchPlanner.Plan(cards, running, retries, categories, Config.MaxRetries);

    /// <summary>TIME-1: the card's limit in minutes: its Time box, else this project's own maxMinutes (Q14).</summary>
    public int Limit(Card card) => WatchdogRules.Limit(card.TimeBox, Config);

    /// <summary>MP-9: each health check to run, for the projects that have a command, against that project's repository only.</summary>
    public static IReadOnlyList<(string Project, string Repo, string Command)> HealthRuns(IEnumerable<PoolProjectSetup> projects) =>
        [.. projects.Where(p => p.ShowsHealthState).Select(p => (p.Name, p.Repo, p.Config.HealthCommand))];

    /// <summary>MP-9: each planner outbox to read, for the projects that have one.</summary>
    public static IReadOnlyList<(string Project, string Outbox)> PlannerOutboxes(IEnumerable<PoolProjectSetup> projects) =>
        [.. projects.Where(p => p.Planner is not null).Select(p => (p.Name, p.Planner!.Outbox))];
}
