namespace Scheduler.Core.WebContract;

/// <summary>What a page action returns (PAGE-1, MOVE-11): the queued id, or the error to show.</summary>
public sealed record ActionResult
{
    /// <summary>Whether the action was accepted.</summary>
    public required bool Ok { get; init; }

    /// <summary>The id of the queued action (the id of a fetch POST answer), when accepted.</summary>
    public int Id { get; init; }

    /// <summary>The error to show as a notice, when not accepted.</summary>
    public string Error { get; init; } = "";

    /// <summary>An accepted action.</summary>
    public static ActionResult Queued(int id) => new() { Ok = true, Id = id };

    /// <summary>A refused action.</summary>
    public static ActionResult Failed(string error) => new() { Ok = false, Error = error };
}
