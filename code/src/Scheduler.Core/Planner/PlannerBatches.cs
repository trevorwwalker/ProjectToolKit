using Scheduler.Core.Contracts;

namespace Scheduler.Core.Planner;

/// <summary>Which outbox folders are batches to apply (PLAN-2, <c>Planner-Batches</c> L1288).</summary>
public static class PlannerBatches
{
    /// <summary>
    /// The complete batches in name order: folders with a <c>batch.json</c>, never <c>sent</c> or
    /// <c>failed</c>. A folder without <c>batch.json</c> is still being written.
    /// </summary>
    public static IReadOnlyList<string> Complete(IEnumerable<OutboxFolder> folders) =>
        [.. folders
            .Where(f => f.HasBatchJson && !Text.Same(f.Name, "sent") && !Text.Same(f.Name, "failed"))
            .Select(f => f.Name)
            .OrderBy(n => n, Text.SortComparer)];
}
