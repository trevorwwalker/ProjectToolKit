using System.Globalization;

namespace Scheduler.Host.Observe;

/// <summary>What <see cref="LogComparison"/> found (roadmap item 1).</summary>
public sealed record ComparisonReport
{
    /// <summary>The observe ticks compared.</summary>
    public required int Ticks { get; init; }

    /// <summary>The first observe tick's time, when there was one.</summary>
    public DateTime? First { get; init; }

    /// <summary>The last observe tick's time, when there was one.</summary>
    public DateTime? Last { get; init; }

    /// <summary>How far apart the same decision may be in the two logs.</summary>
    public required TimeSpan Tolerance { get; init; }

    /// <summary>Every tick that disagrees, in order.</summary>
    public required IReadOnlyList<TickDisagreement> Disagreements { get; init; }

    /// <summary>The report as text.</summary>
    public IReadOnlyList<string> Lines()
    {
        if (Ticks == 0 || First is not { } first || Last is not { } last)
        {
            return ["no observe ticks in the new log: was it written by scheduler-next -Observe?"];
        }

        var lines = new List<string>
        {
            $"compared {Ticks} observe tick(s), {Time(first)} to {Time(last)}, with the old scheduler's log; the same decision may be {Tolerance.TotalSeconds:0} s apart",
            Disagreements.Count == 0 ? "every tick agrees" : $"{Disagreements.Count} tick(s) disagree:",
        };
        foreach (var d in Disagreements)
        {
            lines.Add($"tick {d.Tick} at {Time(d.Time)}:");
            lines.AddRange(d.Differences.Select(x => "  " + x));
        }

        return lines;
    }

    private static string Time(DateTime t) => t.ToString(ObserveLine.TimeFormat, CultureInfo.InvariantCulture);
}
