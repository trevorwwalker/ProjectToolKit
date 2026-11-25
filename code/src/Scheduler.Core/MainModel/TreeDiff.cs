// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.MainModel;

/// <summary>A path whose blob differs between two trees: added (no old), deleted (no new) or changed.</summary>
/// <param name="Path">The path in the repository, with <c>/</c>.</param>
/// <param name="OldBlob">The blob id before, or null when the path was added.</param>
/// <param name="NewBlob">The blob id after, or null when the path was deleted.</param>
public sealed record PathChange(string Path, string? OldBlob, string? NewBlob);

/// <summary>
/// MM-7's tree diff over an abstract tree source: it reads both sides of a subtree only when the
/// two tree ids differ and the filter leads into it, so an unchanged folder costs nothing and a
/// card moved between two folders costs the trees along its two paths. Pure but for the source.
/// </summary>
public static class TreeDiff
{
    /// <summary>
    /// The blobs that differ between root trees <paramref name="oldTree"/> and
    /// <paramref name="newTree"/> (either may be null: an empty tree), kept by
    /// <paramref name="filter"/>, in ordinal path order. Throws
    /// <see cref="MainModelReadException"/> when a tree cannot be read.
    /// </summary>
    public static IReadOnlyList<PathChange> Diff(IObjectSource source, string? oldTree, string? newTree, PathFilter filter)
    {
        var changes = new List<PathChange>();
        Walk(source, "", oldTree, newTree, filter, changes);
        changes.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return changes;
    }

    /// <summary>
    /// The blob id at <paramref name="path"/> under root tree <paramref name="rootTree"/>, reading
    /// only the trees on the way; null when there is no blob there. Throws
    /// <see cref="MainModelReadException"/> when a tree cannot be read.
    /// </summary>
    public static string? Lookup(IObjectSource source, string rootTree, string path)
    {
        var parts = path.Trim('/').Split('/');
        var tree = rootTree;
        for (var i = 0; i < parts.Length; i++)
        {
            var item = Read(source, tree).FirstOrDefault(e => e.Name == parts[i]);
            if (item is null)
            {
                return null;
            }

            if (i == parts.Length - 1)
            {
                return item.IsTree ? null : item.Id;
            }

            if (!item.IsTree)
            {
                return null;
            }

            tree = item.Id;
        }

        return null;
    }

    private static void Walk(IObjectSource source, string prefix, string? oldTree, string? newTree, PathFilter filter, List<PathChange> changes)
    {
        if (oldTree == newTree)
        {
            return;
        }

        IReadOnlyList<TreeItem> olds = oldTree is null ? [] : Read(source, oldTree);
        IReadOnlyList<TreeItem> news = newTree is null ? [] : Read(source, newTree);
        var oldByName = olds.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var newByName = news.ToDictionary(e => e.Name, StringComparer.Ordinal);

        foreach (var name in oldByName.Keys.Union(newByName.Keys, StringComparer.Ordinal))
        {
            oldByName.TryGetValue(name, out var o);
            newByName.TryGetValue(name, out var n);
            var path = prefix.Length == 0 ? name : prefix + "/" + name;

            var oldSub = o is { IsTree: true } ? o.Id : null;
            var newSub = n is { IsTree: true } ? n.Id : null;
            if (oldSub != newSub && filter.Descend(path))
            {
                Walk(source, path, oldSub, newSub, filter, changes);
            }

            var oldBlob = o is { IsTree: false } ? o.Id : null;
            var newBlob = n is { IsTree: false } ? n.Id : null;
            if (oldBlob != newBlob && filter.Keep(path))
            {
                changes.Add(new PathChange(path, oldBlob, newBlob));
            }
        }
    }

    private static IReadOnlyList<TreeItem> Read(IObjectSource source, string treeId) =>
        source.ReadTree(treeId) ?? throw new MainModelReadException("tree " + treeId + " cannot be read");
}
