using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Host.Observe;

/// <summary>
/// The side-by-side comparison (roadmap item 1): the old scheduler's <c>scheduler.log</c>
/// (<c>[yyyy-MM-dd HH:mm:ss] text</c>) against an observe run's <see cref="ObserveLine"/>s.
/// Starts (with their resume flag) and finishes are matched by card when they are at most
/// the tolerance apart; each one without a match is listed at the observe tick that closes over
/// it, and so is every tick whose state differs from the old scheduler's at that time. Slot
/// numbers are shown but not compared. Only the time the observe run ticked is compared.
/// </summary>
public sealed partial class LogComparison(TimeSpan tolerance)
{
    /// <summary>Four ticks at the default poll of 15 s.</summary>
    public static TimeSpan DefaultTolerance { get; } = TimeSpan.FromSeconds(60);

    private enum Kind
    {
        Start,
        Finish,
    }

    private sealed record Decision(DateTime Time, Kind Kind, int Slot, string Card, bool Resume)
    {
        public bool Same(Decision other, TimeSpan tolerance) =>
            Kind == other.Kind && Resume == other.Resume
            && string.Equals(Card, other.Card, StringComparison.OrdinalIgnoreCase)
            && (Time - other.Time).Duration() <= tolerance;

        public override string ToString() => Kind == Kind.Start
            ? $"slot {Slot} <- {Card}{(Resume ? " (resume)" : "")}"
            : $"slot {Slot} finished: {Card}";
    }

    private sealed record TickLine(int Number, DateTime Time, string State);

    /// <summary>Compare the old scheduler's log lines with the observe run's.</summary>
    public ComparisonReport Compare(IEnumerable<string> oldLines, IEnumerable<string> newLines)
    {
        var ticks = new List<TickLine>();
        var news = new List<Decision>();
        foreach (var line in newLines)
        {
            if (!ObserveLine.TryParse(line, out var time, out var text))
            {
                continue;
            }

            if (NewTick().Match(text) is { Success: true } t)
            {
                ticks.Add(new(int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture), time, t.Groups[2].Value));
            }
            else if (Read(time, text, NewStart(), NewFinish()) is { } d)
            {
                news.Add(d);
            }
        }

        var olds = new List<Decision>();
        var oldStates = new List<(DateTime Time, string State)>();
        foreach (var line in oldLines)
        {
            if (OldLine().Match(line) is not { Success: true } m
                || !DateTime.TryParseExact(m.Groups[1].Value, ObserveLine.TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var text = m.Groups[2].Value;
            if (Read(time, text, OldStart(), OldFinish()) is { } d)
            {
                olds.Add(d);
            }
            else if (OldState(text) is { } state)
            {
                oldStates.Add((time, state));
            }
        }

        if (ticks.Count == 0)
        {
            return new ComparisonReport { Ticks = 0, Tolerance = tolerance, Disagreements = [] };
        }

        var from = ticks[0].Time - tolerance;
        var to = ticks[^1].Time;
        var found = new SortedDictionary<int, List<string>>();
        void Add(DateTime time, string text)
        {
            var at = ticks.FindIndex(t => t.Time >= time);
            var index = at < 0 ? ticks.Count - 1 : at;
            if (!found.TryGetValue(index, out var list))
            {
                found[index] = list = [];
            }

            list.Add(text);
        }

        foreach (var o in olds.Where(o => o.Time > from && o.Time <= to))
        {
            if (!news.Any(n => n.Same(o, tolerance)))
            {
                Add(o.Time, $"old only: {o}");
            }
        }

        foreach (var n in news.Where(n => n.Time <= to))
        {
            if (!olds.Any(o => o.Same(n, tolerance)))
            {
                Add(n.Time, $"new only: {n}");
            }
        }

        for (var i = 0; i < ticks.Count; i++)
        {
            var old = oldStates.LastOrDefault(s => s.Time <= ticks[i].Time).State;
            if (old is not null && !string.Equals(old, ticks[i].State, StringComparison.OrdinalIgnoreCase))
            {
                if (!found.TryGetValue(i, out var list))
                {
                    found[i] = list = [];
                }

                list.Add($"state: old {old}, new {ticks[i].State}");
            }
        }

        return new ComparisonReport
        {
            Ticks = ticks.Count,
            First = ticks[0].Time,
            Last = to,
            Tolerance = tolerance,
            Disagreements = [.. found.Select(f => new TickDisagreement(ticks[f.Key].Number, ticks[f.Key].Time, f.Value))],
        };
    }

    /// <summary>
    /// The state an old scheduler's log line puts it in (its start-up line, PAGE-9's pause, resume
    /// and drain, TIME-10's stop all, HLTH-5's red and green, TIME-15's quota), or null.
    /// </summary>
    public static string? OldState(string text)
    {
        if (OldUp().Match(text) is { Success: true } up)
        {
            return up.Groups[1].Value;
        }

        if (text.StartsWith("paused from the page", StringComparison.Ordinal))
        {
            return "paused";
        }

        if (text.StartsWith("resumed from the page", StringComparison.Ordinal)
            || text.StartsWith("main is green again", StringComparison.Ordinal))
        {
            return "running";
        }

        if (text.StartsWith("draining from the page", StringComparison.Ordinal)
            || text.StartsWith("stop all from the page", StringComparison.Ordinal))
        {
            return "draining";
        }

        if (text.Contains("dispatching paused", StringComparison.Ordinal))
        {
            if (text.Contains(": RED, see ", StringComparison.Ordinal))
            {
                return "paused-red";
            }

            if (text.Contains("out of quota", StringComparison.Ordinal))
            {
                return "paused-quota";
            }
        }

        return null;
    }

    private static Decision? Read(DateTime time, string text, Regex start, Regex finish)
    {
        if (start.Match(text) is { Success: true } s)
        {
            return new(time, Kind.Start, int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture), s.Groups[2].Value, s.Groups[3].Success);
        }

        if (finish.Match(text) is { Success: true } f)
        {
            return new(time, Kind.Finish, int.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture), f.Groups[2].Value, false);
        }

        return null;
    }

    [GeneratedRegex(@"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\] (.*)$")]
    private static partial Regex OldLine();

    [GeneratedRegex(@"^slot (\d+) <- (\S+?)( \(resume\))?, pid ")]
    private static partial Regex OldStart();

    [GeneratedRegex(@"^slot (\d+) finished: (\S+) -> ")]
    private static partial Regex OldFinish();

    [GeneratedRegex(@" up: \d+ slots?, ([a-z-]+),")]
    private static partial Regex OldUp();

    [GeneratedRegex(@"^tick (\d+): state ([a-z-]+),")]
    private static partial Regex NewTick();

    [GeneratedRegex(@"^slot (\d+) <- (\S+?)( \(resume\))?$")]
    private static partial Regex NewStart();

    [GeneratedRegex(@"^slot (\d+) finished: (\S+)$")]
    private static partial Regex NewFinish();
}
