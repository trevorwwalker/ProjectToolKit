namespace Scheduler.Core.Contracts;

/// <summary>A <c>git mv</c> in a <see cref="JobsChange"/>.</summary>
public sealed record FileMove
{
    /// <summary>The path it is at, in the repository.</summary>
    public required string From { get; init; }

    /// <summary>The path it moves to.</summary>
    public required string To { get; init; }
}
