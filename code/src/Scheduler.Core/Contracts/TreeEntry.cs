namespace Scheduler.Core.Contracts;

/// <summary>One blob of <c>git ls-tree -r</c>: its path and blob id (CARD-1).</summary>
public sealed record TreeEntry
{
    /// <summary>The path in the repository, with <c>/</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The blob id; a blob is re-read only when this changes (CARD-1).</summary>
    public required string BlobId { get; init; }
}
