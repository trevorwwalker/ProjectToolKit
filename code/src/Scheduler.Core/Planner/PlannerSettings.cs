namespace Scheduler.Core.Planner;

/// <summary>
/// Whether the planner outbox exists at all (PLAN-1, L1272-1286): only when both
/// <c>planner.fetchRepo</c> and <c>planner.outbox</c> are set; the outbox is the fetch repository's
/// folder of that name.
/// </summary>
public sealed record PlannerSettings
{
    /// <summary>The developer's copy, where only <c>git fetch</c> is run.</summary>
    public required string FetchRepo { get; init; }

    /// <summary>The outbox folder: <see cref="FetchRepo"/> plus the configured relative path.</summary>
    public required string Outbox { get; init; }

    /// <summary>The settings, or null (no planner feature) when either value is empty.</summary>
    public static PlannerSettings? Create(string? fetchRepo, string? outbox)
    {
        if (string.IsNullOrEmpty(fetchRepo) || string.IsNullOrEmpty(outbox))
        {
            return null;
        }

        return new PlannerSettings { FetchRepo = fetchRepo, Outbox = Path.Combine(fetchRepo, outbox) };
    }
}
