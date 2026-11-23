namespace Scheduler.Core.Contracts;

/// <summary>What happened to a <see cref="JobsChange"/>: pushed, or the error.</summary>
public sealed record CommitResult
{
    /// <summary>The commit was pushed to main.</summary>
    public required bool Ok { get; init; }

    /// <summary>The error text when it was not, else "".</summary>
    public string Error { get; init; } = "";

    /// <summary>The pushed commit's sha, or "".</summary>
    public string Sha { get; init; } = "";
}
