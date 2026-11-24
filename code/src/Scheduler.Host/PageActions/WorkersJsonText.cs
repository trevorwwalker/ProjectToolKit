using System.Text;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.PageActions;

/// <summary>
/// workers.json as the scheduler writes it from the page (MOVE-10, <c>Workers-Text</c> L2633):
/// hand-formatted text that stays readable, in the line endings the copy on main already has
/// (<c>Commit-Repo-File</c> L2649).
/// </summary>
public static class WorkersJsonText
{
    private const string About =
        "The scheduler's workers: how many there are, and each one's name, model server (an OpenAI-style /v1 endpoint), "
        + "model, metrics address and the categories it takes (letters from categories.json or lanes.json; empty means any category), and the agent it runs "
        + "(an entry of scheduler.json's agents; empty means its worker section). An empty field uses the worker section of scheduler.json. "
        + "The scheduler reads this file from main; changes made from its page are committed and pushed for you. "
        + "Which workers are enabled is kept on the scheduler's PC, not here.";

    /// <summary>The file's text with LF endings; <see cref="WithLineEndings"/> gives it main's own.</summary>
    public static string Build(int slots, IReadOnlyDictionary<int, WorkerDefinition> workers)
    {
        var lines = new List<string>
        {
            "{",
            $"  \"about\": {Quote(About)},",
            $"  \"slots\": {slots.ToString(System.Globalization.CultureInfo.InvariantCulture)},",
            "  \"workers\": {",
        };

        for (var slot = 1; slot <= slots; slot++)
        {
            var w = workers.GetValueOrDefault(slot);
            var comma = slot < slots ? "," : "";
            lines.Add(
                $"    \"{slot}\": {{ \"name\": {Quote(w?.Name ?? "")}, \"agent\": {Quote(w?.Agent ?? "")},"
                + $" \"endpoint\": {Quote(w?.Endpoint ?? "")}, \"model\": {Quote(w?.Model ?? "")},"
                + $" \"metricsUrl\": {Quote(w?.MetricsUrl ?? "")}, \"lanes\": {Quote(w?.Categories ?? "")} }}{comma}");
        }

        lines.AddRange(["  }", "}", ""]);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The text with the line endings <paramref name="mainText"/> has: CRLF when main's copy has
    /// them, LF otherwise. Null main (a file not there yet) gets LF.
    /// </summary>
    public static string WithLineEndings(string text, string? mainText) =>
        text.Replace("\r\n", "\n").Replace("\n", mainText is not null && mainText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");

    // Json-Str (L2629): the two characters that would end the string.
    private static string Quote(string text) =>
        '"' + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}
