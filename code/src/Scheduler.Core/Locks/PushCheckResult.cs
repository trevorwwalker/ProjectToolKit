namespace Scheduler.Core.Locks;

/// <summary>The push check's verdict on one pushed ref (design <c>dynamic-locks.md</c>, DL-16 to DL-18).</summary>
public sealed record PushCheckResult
{
    /// <summary>The paths refused: outside the held set and DL-15.</summary>
    public IReadOnlyList<string> Refused { get; init; } = [];

    /// <summary>One line per refusal (each refused path, and a version not above main's), for the hook to print.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>DL-16: add-only files that delete lines; logged and shown, never refused.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when the push may go (the hook exits 0).</summary>
    public bool Ok => Lines.Count == 0;
}
