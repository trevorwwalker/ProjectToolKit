namespace Scheduler.Core.Contracts;

/// <summary>
/// Read access to <c>&lt;remote&gt;/&lt;branch&gt;</c>, never a working tree (CARD-1). Host runs git
/// without prompts (CFG-16). Used by reading cards and requests (A2), categories (LANE-1), workers
/// (WRK-1), the remote config (CFG-3), duplicates (CARD-19) and the health check's sha (A7).
/// </summary>
public interface IMainRepository
{
    /// <summary><c>git fetch</c> of the remote; false when it failed (CARD-2 spaces the calls).</summary>
    bool Fetch();

    /// <summary>
    /// <c>git ls-tree -r</c> of <paramref name="folder"/> on the remote branch, or null when git
    /// failed or gave nothing, so the caller keeps its previous list (CARD-3).
    /// </summary>
    IReadOnlyList<TreeEntry>? ListTree(string folder);

    /// <summary>A blob's text, or null when it cannot be read.</summary>
    string? ReadBlob(string blobId);

    /// <summary>A file's text on the remote branch (<c>git show &lt;remote&gt;/&lt;branch&gt;:&lt;path&gt;</c>), or null.</summary>
    string? ReadFile(string path);

    /// <summary>The remote branch's head commit sha, or null when git failed.</summary>
    string? HeadSha();
}
