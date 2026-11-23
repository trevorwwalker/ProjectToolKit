namespace Scheduler.Host.Git;

/// <summary>
/// 1013-B: many blobs read in one git process (<c>git cat-file --batch</c>) instead of one process each, so a
/// cold start over a folder of a hundred requests or cards costs one process, not a hundred.
/// </summary>
public interface IBlobBatchReader
{
    /// <summary>
    /// The text of each of <paramref name="blobIds"/> that could be read, by blob id; a blob missing from the
    /// answer could not be read (the caller reads it alone or tries again later). Empty when git failed.
    /// </summary>
    IReadOnlyDictionary<string, string> ReadBlobs(IReadOnlyCollection<string> blobIds);
}
