namespace Scheduler.Core.Scopes;

/// <summary>
/// A card's <c>Scope:</c> line (spec B2 SC-1, B3 Q5 as answered on card 320-A): a new line with the
/// rules of today's Locks line. Entries are separated by commas; a folder ends in <c>/</c>; an
/// entry may be marked <c>(add-only)</c>; <c>; except</c> starts the list of paths left out. A path
/// may be a glob (<see cref="PathGlob"/>, 961-A).
/// Example: <c>Scope: src/, docs/known_gaps.md (add-only); except src/old/</c>.
/// </summary>
public static class ScopeLine
{
    private const string AddOnlyMark = "(add-only)";

    /// <summary>The entries of the first Scope line, or null when the card has none.</summary>
    public static IReadOnlyList<ScopeEntry>? Entries(string cardText)
    {
        var line = Find(cardText);
        return line is null ? null : Split(Paths(line)).Select(Entry).ToList();
    }

    /// <summary>
    /// The card's own scope, or null when it has no Scope line or the line is empty or
    /// <c>none</c> (then its area's applies, SC-4).
    /// </summary>
    public static Scope? Parse(string cardText) => ParseValue(Find(cardText));

    /// <summary>
    /// A scope from the text after <c>Scope:</c> or <c>Locks:</c> (job card 560-A); null when it
    /// is null, empty or <c>none</c>. Backticks and notes in parentheses (other than
    /// <c>(add-only)</c>) are dropped first, as Locks lines carry them: <c>src/x/ (wiring only)</c>.
    /// A path may be a glob (<see cref="PathGlob"/>: <c>vm/MemoryMap.*</c>); only one it cannot match
    /// (a character class) gives a scope with a <see cref="Scope.Problem"/> (961-A).
    /// </summary>
    public static Scope? ParseValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var line = Clean(value);
        var entries = Split(Paths(line)).Select(Entry).ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        var except = Split(Excepts(line)).Select(e => Entry(e).Path).ToList();
        var unreadable = entries.Where(e => PathGlob.HasUnsupportedSyntax(e.Path)).Select(e => e.Path)
            .Concat(except.Where(PathGlob.HasUnsupportedSyntax)).ToList();
        return new Scope(entries.Select(e => e.Path).ToList(), except)
        {
            AddOnly = entries.Where(e => e.AddOnly).Select(e => e.Path).ToList(),
            Problem = unreadable.Count == 0
                ? null
                : $"its scope has a pattern it cannot match ({string.Join(", ", unreadable)}):"
                    + " * and ? are matched, [abc] is not",
        };
    }

    private static string? Find(string cardText)
    {
        foreach (var raw in (cardText ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Scope:", StringComparison.Ordinal))
            {
                return line["Scope:".Length..].Trim();
            }
        }

        return null;
    }

    private static int ExceptAt(string line)
    {
        var at = line.IndexOf(';', StringComparison.Ordinal);
        while (at >= 0)
        {
            if (line[(at + 1)..].TrimStart().StartsWith("except", StringComparison.OrdinalIgnoreCase))
            {
                return at;
            }

            at = line.IndexOf(';', at + 1);
        }

        return -1;
    }

    private static string Paths(string line)
    {
        var at = ExceptAt(line);
        return at < 0 ? line : line[..at];
    }

    private static string Excepts(string line)
    {
        var at = ExceptAt(line);
        return at < 0 ? "" : line[(at + 1)..].TrimStart()["except".Length..];
    }

    private static string Clean(string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            value.Replace("`", "", StringComparison.Ordinal),
            @"\s*\((?!add-only\))[^()]*\)",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

    private static List<string> Split(string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !e.Equals("none", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static ScopeEntry Entry(string text)
    {
        var addOnly = text.EndsWith(AddOnlyMark, StringComparison.OrdinalIgnoreCase);
        var path = (addOnly ? text[..^AddOnlyMark.Length] : text).Trim().Replace('\\', '/');
        var kind = PathGlob.IsGlob(path) ? ScopeEntryKind.Pattern
            : path.EndsWith('/') ? ScopeEntryKind.Folder
            : ScopeEntryKind.File;
        return new ScopeEntry(path, kind, addOnly);
    }
}
