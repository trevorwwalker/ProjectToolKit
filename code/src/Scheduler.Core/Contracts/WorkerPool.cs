namespace Scheduler.Core.Contracts;

/// <summary>
/// The worker types and the total of them (WT-C1, WT-C5): what the <c>pool</c> section of
/// workers.json says (WT-C7), or what today's <c>slots</c> and <c>workers</c> migrate to in memory
/// (WT-C10). The scheduler starts no more than <see cref="MaxWorkers"/> workers in all (WT-1) and no
/// more of a type than its own cap (WT-2).
/// </summary>
public sealed record WorkerPool
{
    /// <summary>How many workers run at once in all, across every project of a pool (WT-1).</summary>
    public int MaxWorkers { get; init; } = 2;

    /// <summary>The types in the order the file lists them: the first that fits and has room takes a card (WT-C5, WT-5).</summary>
    public IReadOnlyList<WorkerType> Types { get; init; } = [];

    /// <summary>
    /// The types were derived from <c>slots</c> and <c>workers</c> (WT-C10) rather than read from a
    /// <c>pool</c>. Until the pool form is first saved a <c>Worker: &lt;number&gt;</c> line names the
    /// type migrated from that slot, and nothing after it (WT-3, WT-C11, 530-D Q3).
    /// </summary>
    public bool Migrated { get; init; }
}
