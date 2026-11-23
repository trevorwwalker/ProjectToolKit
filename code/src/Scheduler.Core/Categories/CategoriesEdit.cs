using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.Scopes;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Categories;

/// <summary>
/// The Categories panel's Save (992-B), the part that needs no git: what is wrong with the rows, which
/// letters the file on main has, and the file's new text. Host commits it.
/// </summary>
public static class CategoriesEdit
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // The file is read by people: keep apostrophes, "<" and accents as they are typed.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The rows with their letters trimmed and in capitals, and their path lists without blank lines.</summary>
    public static IReadOnlyList<PageCategory> Normalise(IReadOnlyList<PageCategory> rows) =>
        [.. rows.Select(row => row with
        {
            Letter = row.Letter.Trim().ToUpperInvariant(),
            OriginalLetter = row.OriginalLetter.Trim().ToUpperInvariant(),
            Name = row.Name.Trim(),
            Purpose = row.Purpose.Trim(),
            Color = row.Color.Trim(),
            Owns = Paths(row.Owns),
            Except = Paths(row.Except),
        })];

    /// <summary>
    /// Why the rows cannot be saved, one reason each, or none: a letter that is not one of A-Z, a letter
    /// used twice, or a path or glob the scope rules cannot use (961-A). Overlaps are not here: they warn.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<PageCategory> rows, IReadOnlyList<string> addOnly)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(addOnly);
        var problems = new List<string>();
        if (rows.Count == 0)
        {
            problems.Add("there must be at least one category");
        }

        foreach (var row in rows)
        {
            if (row.Letter.Length != 1 || row.Letter[0] is < 'A' or > 'Z')
            {
                problems.Add($"\"{row.Letter}\" is not a category letter: use one letter, A to Z");
            }
        }

        problems.AddRange(rows
            .GroupBy(row => row.Letter, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1)
            .Select(group => $"letter {group.Key} is used by more than one category"));

        foreach (var row in rows)
        {
            foreach (var path in row.Owns.Concat(row.Except))
            {
                if (PathProblem(path) is { } why)
                {
                    problems.Add($"category {row.Letter}: {why}");
                }
            }
        }

        foreach (var path in addOnly)
        {
            if (PathProblem(path) is { } why)
            {
                problems.Add($"shared: {why}");
            }
        }

        return problems;
    }

    /// <summary>
    /// What is wrong with one path or glob of a category (961-A: a path, a folder ending in <c>/</c>, or a
    /// glob with <c>*</c>, <c>?</c> or <c>**</c>, relative to the repository), or null when it is usable.
    /// </summary>
    public static string? PathProblem(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Contains('\\', StringComparison.Ordinal))
        {
            return $"{path} uses \\: write paths with /";
        }

        if (path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
        {
            return $"{path} is not relative to the repository";
        }

        if (path.Split('/').Any(part => part == ".."))
        {
            return $"{path} leaves the repository (..)";
        }

        if (PathGlob.HasUnsupportedSyntax(path) || path.Contains(']', StringComparison.Ordinal) || path.Contains('{', StringComparison.Ordinal))
        {
            return $"{path} has a pattern that cannot be matched: * and ? are matched, [abc] and {{a,b}} are not";
        }

        return null;
    }

    /// <summary>The categories the text defines, in its order, or null when it is no categories file.</summary>
    public static CategoriesFile? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return CategoriesJson.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The categories file with <paramref name="rows"/> as its categories, in their order, and
    /// <paramref name="addOnly"/> as its shared add-only list. Everything else in <paramref name="main"/>
    /// - the file's about, the shared section's other lists, a category's other fields - is kept: a
    /// row takes its fields from the category it was loaded as.
    /// </summary>
    public static string Text(string? main, IReadOnlyList<PageCategory> rows, IReadOnlyList<string> addOnly)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(addOnly);
        JsonObject root;
        try
        {
            root = (string.IsNullOrWhiteSpace(main) ? null : JsonNode.Parse(main) as JsonObject) ?? [];
        }
        catch (JsonException)
        {
            root = [];
        }

        var old = root["lanes"] as JsonObject;
        var section = new JsonObject();
        foreach (var row in rows)
        {
            var def = old is not null && row.OriginalLetter.Length > 0 && old[row.OriginalLetter] is JsonObject was
                ? (JsonObject)was.DeepClone()
                : [];
            def["name"] = row.Name;
            if (row.Purpose.Length > 0 || def.ContainsKey("purpose"))
            {
                def["purpose"] = row.Purpose;
            }

            if (row.Color.Length > 0)
            {
                def["color"] = row.Color;
            }
            else
            {
                def.Remove("color");
            }

            def["owns"] = Array(row.Owns);
            def["except"] = Array(row.Except);
            section[row.Letter] = def;
        }

        root["lanes"] = section;
        var shared = root["shared"] as JsonObject ?? [];
        shared["addOnly"] = Array(addOnly);
        root["shared"] = shared;
        return root.ToJsonString(Indented) + "\n";
    }

    private static JsonArray Array(IEnumerable<string> values) => [.. values.Select(v => (JsonNode?)JsonValue.Create(v))];

    private static List<string> Paths(IEnumerable<string> values) =>
        [.. values.Select(v => v.Trim()).Where(v => v.Length > 0)];
}
