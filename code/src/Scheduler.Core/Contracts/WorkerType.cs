namespace Scheduler.Core.Contracts;

/// <summary>
/// One worker type (WT-C3): what its workers run, and how many of them may run at once. A type
/// replaces the fixed slots of WRK-1 to WRK-3 - any type may use any worktree folder (WT-W1), and
/// the folder's number stays the worker's number (WT-W3). Its effective endpoint, model, metrics,
/// agent and cli are <c>Effective-Worker</c> with the type in place of the worker definition (WT-C4).
/// </summary>
public sealed record WorkerType
{
    /// <summary>The type's name: the <c>types</c> key, or the name the migration gave it (WT-C2, WT-C10.4).</summary>
    public required string Name { get; init; }

    /// <summary>How many of this type may run at once (WT-2). 0 is off, which is what Disable was (WT-C3).</summary>
    public int Max { get; init; }

    /// <summary>The agent name, or "" for the config's <c>worker</c> section (WT-C3, CFG-11).</summary>
    public string Agent { get; init; } = "";

    /// <summary>The model, or "" for the agent's or the config's (WT-C4).</summary>
    public string Model { get; init; } = "";

    /// <summary>The model server endpoint, or "" for none of its own (WT-C4, WT-10).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>The metrics URL, or "" (WT-C4).</summary>
    public string MetricsUrl { get; init; } = "";

    /// <summary>The projects this type serves, pool only (WT-C8); empty is every project (530-D Q1).</summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>WT-C8 (969-A: no categories): the type serves <paramref name="project"/>; names compare without case.</summary>
    public bool Serves(string project) => Projects.Count == 0 || Projects.Contains(project, Text.Comparer);

    /// <summary>
    /// The slot numbers this type was migrated from (WT-C10.5), which a <c>Worker: &lt;number&gt;</c>
    /// line names until the pool form is first saved (WT-3, 530-D Q3). Empty for a type read from <c>pool</c>.
    /// </summary>
    public IReadOnlyList<int> Slots { get; init; } = [];
}
