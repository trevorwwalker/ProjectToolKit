namespace Scheduler.Core.Planner;

/// <summary>
/// Main as the answers worktree has it when a batch is applied (PLAN-4, PLAN-5): Host resets the
/// worktree to the remote branch, then these read it. Paths are repository paths with <c>/</c>.
/// </summary>
public interface IPlannerMain
{
    /// <summary>Every file under the jobs folder (<c>git ls-tree -r --name-only HEAD</c>), in git's order.</summary>
    IReadOnlyList<string> Paths();

    /// <summary>The file's blob id in HEAD, or null when HEAD does not have it.</summary>
    string? BlobId(string path);

    /// <summary>The file's text, or null when it is not there.</summary>
    string? ReadFile(string path);

    /// <summary>A blob's text (<c>git cat-file -p</c>), or null when it cannot be read.</summary>
    string? ReadBlob(string blobId);
}
