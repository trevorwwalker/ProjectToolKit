namespace Scheduler.Core.MainModel;

/// <summary>One entry of a git tree object: a blob or a subtree, by name (MM-7).</summary>
/// <param name="Name">The entry's name inside its tree (no <c>/</c>).</param>
/// <param name="Id">The object id of the blob or tree.</param>
/// <param name="IsTree">True for a subtree, false for a blob. Gitlinks are left out by the source.</param>
public sealed record TreeItem(string Name, string Id, bool IsTree);

/// <summary>The parts of a commit object the model uses (MM-7, MM-12).</summary>
/// <param name="Sha">The commit's id.</param>
/// <param name="Tree">Its root tree's id.</param>
/// <param name="Parents">Its parents, first parent first.</param>
/// <param name="Time">The committer time.</param>
/// <param name="Subject">The first line of the message.</param>
public sealed record CommitInfo(
    string Sha,
    string Tree,
    IReadOnlyList<string> Parents,
    DateTimeOffset Time,
    string Subject);

/// <summary>
/// Read access to a repository's objects by id, the abstract tree source the model is built on
/// (state-cache.md section 6). Host's in-process reader (<c>MainObjects</c>, MM-1 to MM-3) and the
/// CLI reader (MM-16) implement it; tests use an in-memory store. A null answer means the object
/// could not be read: the step that asked fails and the model keeps its previous snapshot (MM-2,
/// CARD-3). An implementation may also throw; the model treats that the same way.
/// </summary>
public interface IObjectSource
{
    /// <summary>The commit <paramref name="sha"/>, or null when it cannot be read.</summary>
    CommitInfo? ReadCommit(string sha);

    /// <summary>The entries of tree <paramref name="treeId"/>, or null when it cannot be read.</summary>
    IReadOnlyList<TreeItem>? ReadTree(string treeId);

    /// <summary>The text of blob <paramref name="blobId"/> (UTF-8), or null when it cannot be read.</summary>
    string? ReadBlob(string blobId);
}

/// <summary>An object the model needed could not be read; the step that asked fails (MM-2, MM-7).</summary>
public sealed class MainModelReadException(string message) : Exception(message);
