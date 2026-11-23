namespace Scheduler.Core.MainModel;

/// <summary>
/// Which paths of a tree the model looks at (MM-7, MM-6): whole folders (the jobs folders) and
/// single files (watched paths). A tree walk descends only into a subtree that is one of the
/// folders, inside one, or on the way to one; nothing else of the repository is read.
/// </summary>
public sealed class PathFilter
{
    private readonly string[] folders;   // each ends in "/"
    private readonly HashSet<string> files;
    private readonly bool all;

    /// <summary>A filter for <paramref name="folderPaths"/> (and all below them) and <paramref name="filePaths"/>.</summary>
    public PathFilter(IEnumerable<string> folderPaths, IEnumerable<string> filePaths)
    {
        folders = [.. folderPaths.Select(f => f.Trim('/') + "/").Distinct(StringComparer.Ordinal)];
        files = new HashSet<string>(filePaths.Select(f => f.Trim('/')), StringComparer.Ordinal);
    }

    private PathFilter()
    {
        folders = [];
        files = [];
        all = true;
    }

    /// <summary>Every path: for the commit log and the whole-tree diff of two commits (MM-12, MM-14).</summary>
    public static PathFilter All { get; } = new();

    /// <summary>Whether a walk descends into the subtree at <paramref name="treePath"/> ("" is the root).</summary>
    public bool Descend(string treePath)
    {
        if (all || treePath.Length == 0)
        {
            return true;
        }

        var dir = treePath + "/";
        foreach (var folder in folders)
        {
            if (dir.StartsWith(folder, StringComparison.Ordinal) || folder.StartsWith(dir, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var file in files)
        {
            if (file.StartsWith(dir, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the blob at <paramref name="path"/> is kept.</summary>
    public bool Keep(string path)
    {
        if (all || files.Contains(path))
        {
            return true;
        }

        foreach (var folder in folders)
        {
            if (path.StartsWith(folder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
