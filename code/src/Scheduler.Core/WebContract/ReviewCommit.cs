namespace Scheduler.Core.WebContract;

/// <summary>One commit of a card, as the review panel lists it (roadmap item 6, card 650-C).</summary>
public sealed record ReviewCommit
{
    /// <summary>The full commit id.</summary>
    public required string Sha { get; init; }

    /// <summary>The first line of the message.</summary>
    public required string Subject { get; init; }

    /// <summary>When it was committed.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>The files it changed.</summary>
    public IReadOnlyList<ReviewFile> Files { get; init; } = [];
}
