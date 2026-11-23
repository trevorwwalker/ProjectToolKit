using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.Git;

/// <summary>
/// Reads of history in process (state-cache.md MM-14, card 1065-B), on the object reader of MM-1: what
/// <c>git diff --name-status --no-renames</c>, <c>git log</c>'s first-parent range, <c>git merge-base</c> and
/// <c>%(trailers)</c> answer, for the locks audit, the scope audit and the wip branches. Each answer is either
/// exactly what git gives or null, and null sends the caller to git (MM-16): a merge in the range, a range
/// longer than <see cref="WalkLimit"/>, a criss-cross merge base. It never writes the repository.
/// </summary>
public static class InProcessHistory
{
    /// <summary>The most commits one walk reads before it leaves the question to git.</summary>
    public const int WalkLimit = 10_000;

    /// <summary>One line of <c>--name-status --no-renames</c>: the path, its status letter (A, D, M or T) and its ids before and after.</summary>
    public sealed record NameStatus(string Path, char Status, ObjectId? Old = null, ObjectId? New = null);

    /// <summary>A commit of a walk with its id.</summary>
    public sealed record WalkedCommit(ObjectId Id, Commit Commit);

    /// <summary>
    /// The repository's object reader, or null when MM-2 refuses it (or there is none) and git must be asked.
    /// </summary>
    public static MainObjects? Open(string repository) => MainObjects.TryOpen(repository, out _);

    /// <summary>Whether an exception is one a read in process may throw, which sends the caller to git.</summary>
    public static bool IsReadFailure(Exception e) =>
        e is GitObjectException or IOException or UnauthorizedAccessException or FormatException or InvalidDataException
            or ArgumentException or KeyNotFoundException or IndexOutOfRangeException;

    /// <summary>
    /// The paths that differ between two trees (null is the empty tree), as <c>git diff --name-status --no-renames</c>
    /// lists them: every file, symlink and submodule whose id or mode changed, in path order.
    /// </summary>
    public static List<NameStatus> Diff(ObjectReadSession session, ObjectId? oldTree, ObjectId? newTree)
    {
        var list = new List<NameStatus>();
        Walk(session, "", oldTree, newTree, list);
        list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return list;
    }

    /// <summary>
    /// What <c>git diff --name-only</c> lists with git's default rename detection: the paths of <paramref name="changes"/>
    /// less each deleted file that reappears unchanged under another name (an exact rename lists only its new
    /// name). Null when git must decide: a deleted file with no added twin while files were added (it may be an
    /// inexact rename), or more deleted twins of one file than added ones.
    /// </summary>
    public static List<string>? NameOnly(IReadOnlyList<NameStatus> changes)
    {
        var deleted = changes.Where(c => c.Status == 'D').ToList();
        var added = changes.Where(c => c.Status == 'A').ToList();
        if (deleted.Count == 0 || added.Count == 0)
        {
            return [.. changes.Select(c => c.Path)];
        }

        var renamed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var twins in deleted.GroupBy(d => d.Old))
        {
            if (twins.Count() > added.Count(a => a.New == twins.Key))
            {
                return null;
            }

            renamed.UnionWith(twins.Select(d => d.Path));
        }

        return [.. changes.Where(c => !renamed.Contains(c.Path)).Select(c => c.Path)];
    }

    /// <summary>
    /// The commits of <c>git log from..head</c> when <paramref name="from"/> is on <paramref name="head"/>'s
    /// first-parent line and nothing between is a merge or a root, oldest first; empty when they are the same; else null.
    /// </summary>
    public static List<WalkedCommit>? FirstParentRange(ObjectReadSession session, ObjectId from, ObjectId head)
    {
        var walked = new List<WalkedCommit>();
        var at = head;
        while (at != from)
        {
            if (walked.Count >= WalkLimit)
            {
                return null;
            }

            var c = session.ReadCommit(at);
            if (c.Parents.Count != 1)
            {
                return null;
            }

            walked.Add(new WalkedCommit(at, c));
            at = c.Parents[0];
        }

        walked.Reverse();
        return walked;
    }

    /// <summary>
    /// <c>git merge-base a b</c>: Known false when git must be asked (more than one best base, or a walk past
    /// <see cref="WalkLimit"/>); otherwise the base, or null when the two share no history (git exits 1).
    /// Git's own walk: newest commit first, painting each side's ancestors until only stale ones are left.
    /// </summary>
    public static (bool Known, ObjectId? Base) MergeBase(ObjectReadSession session, ObjectId a, ObjectId b)
    {
        if (a == b)
        {
            return (true, a);
        }

        const int one = 1, two = 2, stale = 4, result = 8;
        var flags = new Dictionary<ObjectId, int>();
        var commits = new Dictionary<ObjectId, Commit>();
        var queue = new PriorityQueue<ObjectId, (long, long)>();
        long seq = 0;
        var results = new List<ObjectId>();

        void Push(ObjectId id, int mark)
        {
            if (!commits.TryGetValue(id, out var c))
            {
                c = session.ReadCommit(id);
                commits[id] = c;
            }

            flags[id] = flags.GetValueOrDefault(id) | mark;
            queue.Enqueue(id, (-c.CommitTime.ToUnixTimeSeconds(), seq++));
        }

        Push(a, one);
        Push(b, two);
        var steps = 0;
        while (queue.UnorderedItems.Any(i => (flags[i.Element] & stale) == 0))
        {
            if (++steps > WalkLimit * 2)
            {
                return (false, null);
            }

            var id = queue.Dequeue();
            var mark = flags[id] & (one | two | stale);
            if ((mark & (one | two)) == (one | two))
            {
                if ((flags[id] & result) == 0)
                {
                    flags[id] |= result;
                    results.Add(id);
                }

                mark |= stale;
            }

            foreach (var parent in commits[id].Parents)
            {
                if ((flags.GetValueOrDefault(parent) & mark) != mark)
                {
                    Push(parent, mark);
                }
            }
        }

        var best = results.Where(r => (flags[r] & stale) == 0).ToList();
        return best.Count switch
        {
            0 => (true, null),
            1 => (true, best[0]),
            _ => (false, null),
        };
    }

    /// <summary>
    /// The values of the <paramref name="key"/> trailers of a commit message (key compared ignoring case), in
    /// order, as <c>%(trailers:key=...,valueonly)</c> gives them: from the message's last paragraph when it is a
    /// trailer block (every line a trailer or its continuation, or a quarter of them with a Signed-off-by), never
    /// from the subject paragraph.
    /// </summary>
    public static IReadOnlyList<string> Trailers(string message, string key)
    {
        var lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var firstBlank = lines.FindIndex(l => l.Trim().Length == 0);
        if (firstBlank < 0)
        {
            return []; // one paragraph: the subject, never trailers
        }

        var start = lines.FindLastIndex(l => l.Trim().Length == 0) + 1;
        var block = lines.Skip(start).Where(l => !l.StartsWith('#')).ToList();
        int trailers = 0, others = 0;
        var recognised = false;
        var values = new List<(string Key, string Value)>();
        var lastWasTrailer = false;
        foreach (var line in block)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                if (lastWasTrailer && values.Count > 0)
                {
                    values[^1] = (values[^1].Key, values[^1].Value + "\n" + line.TrimEnd());
                    continue;
                }

                others++;
                continue;
            }

            if (line.StartsWith("Signed-off-by: ", StringComparison.Ordinal)
                || line.StartsWith("(cherry picked from commit ", StringComparison.Ordinal))
            {
                recognised = true;
            }

            var sep = Separator(line);
            if (sep > 0)
            {
                trailers++;
                values.Add((line[..sep].Trim(), line[(sep + 1)..].Trim()));
                lastWasTrailer = true;
            }
            else
            {
                others++;
                lastWasTrailer = false;
            }
        }

        var isBlock = trailers > 0 && (others == 0 || (recognised && trailers * 3 >= others));
        return isBlock
            ? [.. values.Where(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)).Select(v => v.Value)]
            : [];
    }

    // git's find_separator: a token of letters, digits and '-', optional blanks, then ':'.
    private static int Separator(string line)
    {
        var blank = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == ':')
            {
                return i;
            }

            if (!blank && (char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                continue;
            }

            if (i > 0 && (c == ' ' || c == '\t'))
            {
                blank = true;
                continue;
            }

            return -1;
        }

        return -1;
    }

    private static void Walk(ObjectReadSession session, string prefix, ObjectId? oldTree, ObjectId? newTree, List<NameStatus> list)
    {
        if (oldTree == newTree)
        {
            return;
        }

        IReadOnlyList<TreeEntry> olds = oldTree is { } o ? session.ReadTree(o) : [];
        IReadOnlyList<TreeEntry> news = newTree is { } n ? session.ReadTree(n) : [];
        var oldByName = olds.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var newByName = news.ToDictionary(e => e.Name, StringComparer.Ordinal);
        foreach (var name in oldByName.Keys.Union(newByName.Keys, StringComparer.Ordinal))
        {
            oldByName.TryGetValue(name, out var was);
            newByName.TryGetValue(name, out var now);
            var path = prefix.Length == 0 ? name : prefix + "/" + name;
            ObjectId? oldSub = was is { Kind: TreeEntryKind.Tree } ? was.Id : null;
            ObjectId? newSub = now is { Kind: TreeEntryKind.Tree } ? now.Id : null;
            if (oldSub != newSub)
            {
                Walk(session, path, oldSub, newSub, list);
            }

            var oldFile = was is { Kind: not TreeEntryKind.Tree } ? was : null;
            var newFile = now is { Kind: not TreeEntryKind.Tree } ? now : null;
            if (oldFile is null && newFile is null)
            {
                continue;
            }

            if (oldFile is null)
            {
                list.Add(new NameStatus(path, 'A', null, newFile!.Id));
            }
            else if (newFile is null)
            {
                list.Add(new NameStatus(path, 'D', oldFile.Id, null));
            }
            else if (oldFile.Id != newFile.Id || oldFile.Mode.TrimStart('0') != newFile.Mode.TrimStart('0'))
            {
                list.Add(new NameStatus(path, FileType(oldFile.Mode) == FileType(newFile.Mode) ? 'M' : 'T', oldFile.Id, newFile.Id));
            }
        }
    }

    private static int FileType(string mode) => mode switch
    {
        "120000" => 1,
        "160000" => 2,
        _ => 0,
    };
}
