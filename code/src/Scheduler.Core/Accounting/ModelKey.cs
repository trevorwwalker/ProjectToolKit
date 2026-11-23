using Scheduler.Core.Contracts;

namespace Scheduler.Core.Accounting;

/// <summary>
/// The models.json entry a worker's model really is (ACCT-3, <c>Model-Key</c> L2263). A model can
/// be configured under an alias, so after the name itself the server's answer decides, and
/// failing that the longest key the served name starts with.
/// </summary>
public static class ModelKey
{
    /// <summary>
    /// The key of <paramref name="model"/>, or "" when nothing identifies it. The name itself
    /// first (case does not matter, as models.json is read into a case-insensitive table); then
    /// the server's root matched by its last path part against each entry's key, name, source's
    /// last part or served list; then the longest key that root's last part starts with.
    /// </summary>
    public static string Resolve(
        IReadOnlyList<ModelEntry> models,
        string? model,
        string? endpoint,
        ServerModelCache? server = null)
    {
        if (string.IsNullOrEmpty(model))
        {
            return "";
        }

        foreach (var entry in models)
        {
            if (Text.Same(entry.Key, model))
            {
                return entry.Key;
            }
        }

        var root = server?.Root(endpoint, model) ?? "";
        if (string.IsNullOrEmpty(root))
        {
            return "";
        }

        var lower = root.TrimEnd('/', '\\').ToLowerInvariant();
        var tail = lower.Split('/', '\\')[^1];
        var best = "";
        foreach (var entry in models)
        {
            foreach (var name in Names(entry))
            {
                var n = name.ToLowerInvariant();
                if (n.Length != 0 && (n == tail || n == lower))
                {
                    return entry.Key;
                }
            }

            if (tail.StartsWith(entry.Key.ToLowerInvariant(), StringComparison.Ordinal)
                && entry.Key.Length > best.Length)
            {
                best = entry.Key;
            }
        }

        return best;
    }

    /// <summary>The names by which an entry can be recognised on a server (ACCT-3).</summary>
    private static IEnumerable<string> Names(ModelEntry entry)
    {
        yield return entry.Key;
        yield return entry.Name;
        if (entry.Source.Length != 0)
        {
            var source = entry.Source.TrimEnd('/');
            yield return source[(source.LastIndexOfAny(['/', '\\']) + 1)..];
        }

        foreach (var served in entry.Served)
        {
            yield return served;
        }
    }
}
