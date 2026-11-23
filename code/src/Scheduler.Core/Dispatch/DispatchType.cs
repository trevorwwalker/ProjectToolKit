using Scheduler.Core.Contracts;

namespace Scheduler.Core.Dispatch;

/// <summary>
/// One worker type as dispatch sees it (WT-3 to WT-5): its effective worker (WT-C4), resolved by
/// the caller, how many of it are running now and what it may still start.
/// </summary>
public sealed record DispatchType
{
    /// <summary>The type's name (WT-C2).</summary>
    public required string Name { get; init; }

    /// <summary>How many of this type may run at once; 0 = off (WT-2).</summary>
    public int Max { get; init; } = 1;

    /// <summary>How many workers of this type are running now, adopted ones included (WT-13).</summary>
    public int Running { get; init; }

    /// <summary>The effective model, or empty.</summary>
    public string Model { get; init; } = "";

    /// <summary>The other names the model answers to in a <c>Worker:</c> line: <c>Model-Short</c> and models.json's <c>short</c> (WT-3).</summary>
    public IReadOnlyList<string> ModelNames { get; init; } = [];

    /// <summary>A premium type (WT-3).</summary>
    public bool Premium { get; init; }

    /// <summary>The effective model server endpoint; empty = always up (WT-4, ACCT-8).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>The agent whose quota the type shares (WT-9); empty = the config's <c>worker</c> section.</summary>
    public string Agent { get; init; } = "";

    /// <summary>The slot numbers the type was migrated from, for <c>Worker: &lt;number&gt;</c> (WT-3, WT-C10.5).</summary>
    public IReadOnlyList<int> Slots { get; init; } = [];

    /// <summary>The projects the type serves, pool only (WT-C3, WT-3, 530-D Q1); empty = every project.</summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>WT-3 (pool): the type serves the named project (names match without case, G-1).</summary>
    public bool Serves(string project) => Projects.Count == 0 || Projects.Contains(project, Text.Comparer);

    /// <summary>
    /// WT-12 (935-A, DISP-19 in type form): the cards the running workers of this type hold, in slot
    /// order, each named by its id ("530-D"). Empty when the caller does not say, and the note then
    /// gives the count only.
    /// </summary>
    public IReadOnlyList<string> RunningCards { get; init; } = [];
}
