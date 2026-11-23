namespace Scheduler.Host.Git.Objects;

/// <summary>
/// 1061-B (MM-1, MM-2): where a repository's git data is. <see cref="GitDir"/> is the folder git calls the git
/// directory (a clone's <c>.git</c>, a bare repository itself, or a worktree's own folder under
/// <c>worktrees/</c>); <see cref="CommonDir"/> is the folder its objects, shared refs and config live in (the same
/// folder unless <c>commondir</c> points elsewhere).
/// </summary>
public sealed record GitFolder(string GitDir, string CommonDir)
{
    public string ObjectsDir => Path.Combine(CommonDir, "objects");

    /// <summary>
    /// The git folder of <paramref name="repository"/> (a working folder, a worktree or a bare repository) and the
    /// reason it cannot be read in process, or null when it can.
    /// </summary>
    public static (GitFolder? Folder, string? Refusal) Find(string repository)
    {
        var root = Path.GetFullPath(repository);
        string gitDir;
        var dotGit = Path.Combine(root, ".git");
        if (Directory.Exists(dotGit))
        {
            gitDir = dotGit;
        }
        else if (File.Exists(dotGit))
        {
            var line = File.ReadAllText(dotGit).Trim();
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return (null, "the .git file has no gitdir line");
            }

            gitDir = Path.GetFullPath(Path.Combine(root, line["gitdir:".Length..].Trim()));
        }
        else if (File.Exists(Path.Combine(root, "HEAD")) && Directory.Exists(Path.Combine(root, "objects")))
        {
            gitDir = root;
        }
        else
        {
            return (null, "not a git repository");
        }

        if (!Directory.Exists(gitDir))
        {
            return (null, "the git folder does not exist");
        }

        var common = gitDir;
        var commonFile = Path.Combine(gitDir, "commondir");
        if (File.Exists(commonFile))
        {
            common = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonFile).Trim()));
        }

        var folder = new GitFolder(gitDir, common);
        return (folder, folder.Refusal());
    }

    /// <summary>MM-2: why this repository is read through the git CLI instead, or null.</summary>
    public string? Refusal()
    {
        var configPath = Path.Combine(CommonDir, "config");
        var config = File.Exists(configPath) ? GitConfig.Parse(File.ReadAllText(configPath)) : GitConfig.Empty;
        var version = config.Get("core.repositoryformatversion") is { } v && int.TryParse(v, out var n) ? n : 0;
        if (version > 1)
        {
            return $"repository format version {version}";
        }

        foreach (var (key, value) in config.Entries)
        {
            if (key.StartsWith("extensions.", StringComparison.Ordinal))
            {
                var name = key["extensions.".Length..];
                var refusal = name switch
                {
                    "objectformat" => string.Equals(value, "sha1", StringComparison.OrdinalIgnoreCase) ? null : $"object format {value}",
                    "refstorage" => string.Equals(value, "files", StringComparison.OrdinalIgnoreCase) ? null : $"ref storage {value}",
                    "partialclone" => "partial clone",
                    "noop" or "noop-v1" or "preciousobjects" or "worktreeconfig" => null,
                    _ => version >= 1 ? $"unknown extension {name}" : null,
                };
                if (refusal is not null)
                {
                    return refusal;
                }
            }
            else if (key.StartsWith("remote.", StringComparison.Ordinal)
                && (key.EndsWith(".partialclonefilter", StringComparison.Ordinal) || (key.EndsWith(".promisor", StringComparison.Ordinal) && GitConfig.IsTrue(value))))
            {
                return "promisor remote";
            }
        }

        if (File.Exists(Path.Combine(ObjectsDir, "info", "alternates")))
        {
            return "alternates";
        }

        if (File.Exists(Path.Combine(CommonDir, "shallow")) || File.Exists(Path.Combine(GitDir, "shallow")))
        {
            return "shallow";
        }

        if (Directory.Exists(Path.Combine(CommonDir, "reftable")))
        {
            return "ref storage reftable";
        }

        return Directory.Exists(ObjectsDir) ? null : "no objects folder";
    }
}

/// <summary>
/// 1061-B (MM-2): the few lines of a git config file the reader needs. Keys are <c>section.key</c> or
/// <c>section.subsection.key</c>, section and key lower case as git compares them; includes are not followed.
/// </summary>
public sealed class GitConfig
{
    public static readonly GitConfig Empty = new([]);

    private GitConfig(List<KeyValuePair<string, string>> entries) => Entries = entries;

    public IReadOnlyList<KeyValuePair<string, string>> Entries { get; }

    /// <summary>The last value of <paramref name="key"/>, as git takes it, or null.</summary>
    public string? Get(string key) => Entries.LastOrDefault(e => e.Key == key).Value;

    public static bool IsTrue(string value) =>
        value.Length == 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1";

    public static GitConfig Parse(string text)
    {
        var entries = new List<KeyValuePair<string, string>>();
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[')
            {
                var end = line.LastIndexOf(']');
                var head = (end > 0 ? line[1..end] : line[1..]).Trim();
                var quote = head.IndexOf('"');
                if (quote >= 0)
                {
                    var sub = head[(quote + 1)..].TrimEnd('"').Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
                    section = head[..quote].Trim().ToLowerInvariant() + "." + sub;
                }
                else
                {
                    // [section.sub], the old form: the subsection is lower case too.
                    section = head.ToLowerInvariant();
                }

                var rest = end > 0 ? line[(end + 1)..].Trim() : "";
                if (rest.Length == 0)
                {
                    continue;
                }

                line = rest;
            }

            var eq = line.IndexOf('=');
            var name = (eq < 0 ? line : line[..eq]).Trim().ToLowerInvariant();
            var value = eq < 0 ? "" : Unquote(line[(eq + 1)..].Trim());
            entries.Add(new(section.Length == 0 ? name : section + "." + name, value));
        }

        return new GitConfig(entries);
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && (c == '#' || c == ';'))
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string Unquote(string value) =>
        value.Replace("\"", "", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
}
