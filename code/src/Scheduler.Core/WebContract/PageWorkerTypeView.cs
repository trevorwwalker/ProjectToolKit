namespace Scheduler.Core.WebContract;

/// <summary>One row of the Worker types panel (WT-P3): a type, how many of it run and its settings.</summary>
public sealed record PageWorkerTypeView
{
    /// <summary>The type's name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The type's name as the panel loaded it (947-B): the hidden field that lets a Save tell a rename
    /// from a remove-plus-add. Empty for a row <c>Add type</c> made, and for a snapshot row that names
    /// no original, where the name itself is the original.
    /// </summary>
    public string OriginalName { get; init; } = "";

    /// <summary>How many workers of the type run now ("<c>Running</c> of <c>Max</c>").</summary>
    public int Running { get; init; }

    /// <summary>The cap; 0 = off.</summary>
    public int Max { get; init; }

    /// <summary>The agent, or empty for the config's <c>worker</c> section.</summary>
    public string Agent { get; init; } = "";

    /// <summary>The model, or empty.</summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// 1087-C: the model the type really runs when <see cref="Model"/> is empty - the agent's default,
    /// or the served name of a server alias, as the worker windows show it (WT-P3). The Host fills it;
    /// the panel shows it greyed as the Model box's placeholder, and the box's value stays empty so an
    /// untouched Save writes no model.
    /// </summary>
    public string EffectiveModel { get; init; } = "";

    /// <summary>The model server endpoint, or empty.</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>The metrics URL, or empty.</summary>
    public string MetricsUrl { get; init; } = "";

    /// <summary>Takes only cards that ask for <c>Worker: premium</c>.</summary>
    public bool Premium { get; init; }

    /// <summary>The projects it serves, in a pool; empty = every project.</summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>Since when the type's agent is out of quota (WT-9), or null; the panel then shows Resume for the agent.</summary>
    public DateTimeOffset? OutOfQuotaSince { get; init; }
}
