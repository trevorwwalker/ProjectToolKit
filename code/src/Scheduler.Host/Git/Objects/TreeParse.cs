using System.Globalization;
using System.Text;

namespace Scheduler.Host.Git.Objects;

public enum TreeEntryKind
{
    Blob,
    Tree,
    Commit,
}

/// <summary>1061-B (MM-1): one line of a tree, as <c>git ls-tree</c> shows it: mode (as git stores it, so a subtree is <c>40000</c>), name, id.</summary>
public sealed record TreeEntry(string Mode, string Name, ObjectId Id)
{
    public TreeEntryKind Kind => Mode switch
    {
        "40000" or "040000" => TreeEntryKind.Tree,
        "160000" => TreeEntryKind.Commit,
        _ => TreeEntryKind.Blob,
    };
}

/// <summary>1061-B (MM-1): a tree object's entries: <c>&lt;mode&gt; &lt;name&gt;\0&lt;20-byte id&gt;</c>, repeated.</summary>
public static class TreeParse
{
    public static IReadOnlyList<TreeEntry> Parse(ReadOnlySpan<byte> data)
    {
        var entries = new List<TreeEntry>();
        var at = 0;
        while (at < data.Length)
        {
            var space = data[at..].IndexOf((byte)' ');
            if (space < 0)
            {
                throw new GitObjectException("tree: entry has no mode");
            }

            var mode = Encoding.ASCII.GetString(data.Slice(at, space));
            at += space + 1;
            var nul = data[at..].IndexOf((byte)0);
            if (nul < 0 || at + nul + 1 + ObjectId.Length > data.Length)
            {
                throw new GitObjectException("tree: entry cut short");
            }

            var name = Encoding.UTF8.GetString(data.Slice(at, nul));
            at += nul + 1;
            entries.Add(new TreeEntry(mode, name, ObjectId.FromBytes(data.Slice(at, ObjectId.Length))));
            at += ObjectId.Length;
        }

        return entries;
    }
}

/// <summary>1061-B (MM-1): a commit's fields as <c>git cat-file commit</c> prints them; the times are the committer's and author's.</summary>
public sealed record Commit(ObjectId Tree, IReadOnlyList<ObjectId> Parents, string Author, string Committer, string Message)
{
    public DateTimeOffset AuthorTime => CommitParse.TimeOf(Author);

    public DateTimeOffset CommitTime => CommitParse.TimeOf(Committer);

    /// <summary>The first line of the message, as <c>git log --format=%s</c> joins a multi-line subject paragraph.</summary>
    public string Subject => string.Join(' ', Message.Replace("\r", "", StringComparison.Ordinal).Split("\n\n")[0].Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
}

public static class CommitParse
{
    public static Commit Parse(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        var end = text.IndexOf("\n\n", StringComparison.Ordinal);
        var head = end < 0 ? text : text[..end];
        var message = end < 0 ? "" : text[(end + 2)..];
        ObjectId? tree = null;
        var parents = new List<ObjectId>();
        string author = "", committer = "";
        foreach (var line in head.Split('\n'))
        {
            if (line.StartsWith(' '))
            {
                continue; // a continuation line of a multi-line header (gpgsig, mergetag)
            }

            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..];
            switch (key)
            {
                case "tree":
                    tree = ObjectId.Parse(value);
                    break;
                case "parent":
                    parents.Add(ObjectId.Parse(value));
                    break;
                case "author":
                    author = value;
                    break;
                case "committer":
                    committer = value;
                    break;
            }
        }

        return new Commit(tree ?? throw new GitObjectException("commit has no tree"), parents, author, committer, message);
    }

    /// <summary>The <c>object</c> line of a tag object: what the tag points at.</summary>
    public static ObjectId TagTarget(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("object ", StringComparison.Ordinal))
            {
                return ObjectId.Parse(line[7..].Trim());
            }
        }

        throw new GitObjectException("tag has no object line");
    }

    /// <summary>The time at the end of an author or committer line: <c>Name &lt;mail&gt; &lt;seconds&gt; &lt;+hhmm&gt;</c>.</summary>
    public static DateTimeOffset TimeOf(string person)
    {
        var close = person.LastIndexOf('>');
        var parts = person[(close + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return DateTimeOffset.MinValue;
        }

        var offset = TimeSpan.Zero;
        if (parts.Length > 1 && parts[1].Length == 5 && int.TryParse(parts[1].AsSpan(1, 2), out var h) && int.TryParse(parts[1].AsSpan(3, 2), out var m))
        {
            offset = new TimeSpan(h, m, 0) * (parts[1][0] == '-' ? -1 : 1);
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(offset);
    }
}
