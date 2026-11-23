using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Core.Views;

/// <summary>One <c>## n.</c> item of the roadmap, which is a plan of its own (VW-13).</summary>
public sealed record RoadmapItem(int Number, string Title, string Status)
{
    /// <summary>The name the Plans view gives it: "Roadmap item n: title".</summary>
    public string Name => $"Roadmap item {Number}: {Title}";
}

/// <summary>VW-13: a plan's status line, and the roadmap's items.</summary>
public static class PlanStatus
{
    public const int Length = 200;
    public const string None = "no status line";

    private static readonly Regex StatusLine = new(@"^\s*(\*{0,2}Status\b.*)$", RegexOptions.IgnoreCase);
    private static readonly Regex ItemHead = new(@"^##\s+(\d+)\.\s*(.*?)\s*$");
    private static readonly Regex RoadmapLine = new(@"^\s*Roadmap:\s*items?\s+(\d+)", RegexOptions.IgnoreCase);

    /// <summary>The first line starting with <c>Status:</c>, <c>*Status</c> or <c>**Status</c>, cut at 200 characters.</summary>
    public static string Of(string? text)
    {
        foreach (var line in Lines(text))
        {
            var m = StatusLine.Match(line);
            if (m.Success)
            {
                return Cut(m.Groups[1].Value.Trim());
            }
        }

        return None;
    }

    /// <summary>Each <c>## n.</c> item of the roadmap with its own <c>*Status ...*</c> line (none: "no status line").</summary>
    public static IReadOnlyList<RoadmapItem> RoadmapItems(string? text)
    {
        var items = new List<RoadmapItem>();
        int number = 0;
        string title = "", status = "";
        bool open = false;
        foreach (var line in Lines(text))
        {
            var head = ItemHead.Match(line);
            if (head.Success)
            {
                if (open)
                {
                    items.Add(new RoadmapItem(number, title, status.Length == 0 ? None : status));
                }

                open = true;
                number = int.Parse(head.Groups[1].Value, CultureInfo.InvariantCulture);
                title = head.Groups[2].Value;
                status = "";
            }
            else if (open && status.Length == 0)
            {
                var m = StatusLine.Match(line);
                if (m.Success)
                {
                    status = Cut(m.Groups[1].Value.Trim());
                }
            }
        }

        if (open)
        {
            items.Add(new RoadmapItem(number, title, status.Length == 0 ? None : status));
        }

        return items;
    }

    /// <summary>The roadmap item a design document names with its <c>Roadmap: item n</c> line, or null (list it on its own).</summary>
    public static int? RoadmapItemOf(string? text)
    {
        foreach (var line in Lines(text))
        {
            var m = RoadmapLine.Match(line);
            if (m.Success)
            {
                return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static string Cut(string s) => s.Length <= Length ? s : s[..Length];

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Split('\n');
}
