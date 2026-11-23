using System.Text.RegularExpressions;

namespace Scheduler.Host.PageActions;

/// <summary>Reads a card's text for the review panel (roadmap item 6, card 650-C).</summary>
public static partial class ReviewText
{
    /// <summary>The body of the <c>## Result</c> section, or empty when the card has none.</summary>
    public static string ResultSection(string cardText)
    {
        var lines = cardText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("## Result", StringComparison.Ordinal));
        if (start < 0)
        {
            return "";
        }

        var rest = lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', rest).Trim();
    }

    /// <summary>
    /// The repository files the text names (a path with a slash or an extension, bare or in
    /// backticks) that <paramref name="exists"/> confirms, in order of first mention.
    /// </summary>
    public static IReadOnlyList<string> Links(string text, Func<string, bool> exists) =>
        PathToken().Matches(text)
            .Select(m => m.Value.Replace('\\', '/'))
            .Where(p => !p.StartsWith('/') && !p.Contains("..", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(exists)
            .ToList();

    [GeneratedRegex(@"(?<![\w./\\-])[\w.-]+(?:[/\\][\w. -]*[\w-])*\.[A-Za-z0-9]{1,5}(?![\w/])")]
    private static partial Regex PathToken();
}
