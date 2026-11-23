using System.Text;

namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1): a repository's refs, read from the files git keeps them in. A loose ref file wins over its line
/// in <c>packed-refs</c>; peeled lines (<c>^&lt;id&gt;</c>) are skipped; a symbolic ref (<c>ref: &lt;name&gt;</c>) is
/// followed. A worktree's own refs (<c>HEAD</c>, <c>refs/worktree/</c>, <c>refs/bisect/</c>, <c>refs/rewritten/</c>
/// and the pseudo-refs) are read from its git folder, every other ref from the common folder.
/// </summary>
public sealed class Refs(GitFolder folder)
{
    private const int MaxSymbolicDepth = 10;

    /// <summary>
    /// The object <paramref name="name"/> points at, found as <c>git rev-parse</c> finds a ref name: the name itself,
    /// then under <c>refs/</c>, <c>refs/tags/</c>, <c>refs/heads/</c>, <c>refs/remotes/</c> and
    /// <c>refs/remotes/&lt;name&gt;/HEAD</c>. Null when no such ref exists.
    /// </summary>
    public ObjectId? Resolve(string name)
    {
        Dictionary<string, ObjectId>? packed = null;
        foreach (var candidate in Candidates(name))
        {
            if (Follow(candidate, ref packed, 0) is { } id)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Every ref under <paramref name="prefix"/> (for example <c>refs/</c>) with its object, as <c>git for-each-ref</c> lists them.</summary>
    public SortedDictionary<string, ObjectId> List(string prefix = "refs/")
    {
        var all = new SortedDictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var (name, id) in ReadPacked())
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                all[name] = id;
            }
        }

        foreach (var root in new[] { folder.CommonDir, folder.GitDir }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var refsDir = Path.Combine(root, "refs");
            if (!Directory.Exists(refsDir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(refsDir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetRelativePath(root, file).Replace('\\', '/');
                var linked = !string.Equals(folder.GitDir, folder.CommonDir, StringComparison.OrdinalIgnoreCase);
                var own = string.Equals(root, folder.GitDir, StringComparison.OrdinalIgnoreCase);
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || (linked && IsPerWorktree(name) != own))
                {
                    continue;
                }

                if (Resolve(name) is { } id)
                {
                    all[name] = id;
                }
            }
        }

        return all;
    }

    private static IEnumerable<string> Candidates(string name)
    {
        if (name == "HEAD" || name.StartsWith("refs/", StringComparison.Ordinal))
        {
            yield return name;
            yield break;
        }

        yield return name;
        yield return "refs/" + name;
        yield return "refs/tags/" + name;
        yield return "refs/heads/" + name;
        yield return "refs/remotes/" + name;
        yield return "refs/remotes/" + name + "/HEAD";
    }

    private ObjectId? Follow(string name, ref Dictionary<string, ObjectId>? packed, int depth)
    {
        if (depth > MaxSymbolicDepth)
        {
            return null;
        }

        var file = Path.Combine(IsPerWorktree(name) ? folder.GitDir : folder.CommonDir, name.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(file))
        {
            var text = Encoding.UTF8.GetString(SharedFile.ReadAll(file)).Trim();
            if (text.StartsWith("ref:", StringComparison.Ordinal))
            {
                return Follow(text[4..].Trim(), ref packed, depth + 1);
            }

            if (text.Length >= ObjectId.Length * 2 && ObjectId.TryParse(text.AsSpan(0, ObjectId.Length * 2), out var id))
            {
                return id;
            }

            return null;
        }

        packed ??= ReadPacked();
        return packed.TryGetValue(name, out var p) ? p : null;
    }

    private Dictionary<string, ObjectId> ReadPacked()
    {
        var refs = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var path = Path.Combine(folder.CommonDir, "packed-refs");
        if (!File.Exists(path))
        {
            return refs;
        }

        foreach (var raw in Encoding.UTF8.GetString(SharedFile.ReadAll(path)).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < ObjectId.Length * 2 + 2 || line[0] == '#' || line[0] == '^')
            {
                continue;
            }

            if (ObjectId.TryParse(line.AsSpan(0, ObjectId.Length * 2), out var id) && line[ObjectId.Length * 2] == ' ')
            {
                refs[line[(ObjectId.Length * 2 + 1)..]] = id;
            }
        }

        return refs;
    }

    private static bool IsPerWorktree(string name) =>
        !name.StartsWith("refs/", StringComparison.Ordinal)
        || name.StartsWith("refs/worktree/", StringComparison.Ordinal)
        || name.StartsWith("refs/bisect/", StringComparison.Ordinal)
        || name.StartsWith("refs/rewritten/", StringComparison.Ordinal);
}

/// <summary>
/// 1061-B (MM-3): files read so git can still rename over them, replace them or delete them while they are open,
/// and closed again before the call returns.
/// </summary>
internal static class SharedFile
{
    public const FileShare Share = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, Share, 4096, FileOptions.None);

    public static byte[] ReadAll(string path)
    {
        using var s = Open(path);
        var data = new byte[s.Length];
        s.ReadExactly(data);
        return data;
    }
}
