// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;

namespace Scheduler.Core.Views;

/// <summary>What the developer set on a plan or step by clicking its mark (1125-C).</summary>
public enum ManualMark
{
    /// <summary>Worked out from the children.</summary>
    Automatic,

    /// <summary>A green check whatever the children say.</summary>
    Checked,
}

/// <summary>
/// <c>jobs/plan-marks.json</c>: the marks the developer set, by plan path and step label
/// (<c>{"jobs/plans/x_plan.md": {"3": "checked", "3.1": "checked"}}</c>). The plan itself is keyed <see cref="PlanKey"/>.
/// </summary>
public static class PlanMarkFile
{
    /// <summary>The key of the plan's own mark.</summary>
    public const string PlanKey = "plan";

    /// <summary>The file's name in the jobs folder.</summary>
    public const string FileName = "plan-marks.json";

    /// <summary>The next state when the mark is clicked: automatic, checked, automatic (1215-C).</summary>
    public static ManualMark Next(ManualMark mark) => mark == ManualMark.Automatic ? ManualMark.Checked : ManualMark.Automatic;

    public static string Name(ManualMark mark) => mark switch
    {
        ManualMark.Checked => "checked",
        _ => "automatic",
    };

    public static ManualMark? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "checked" => ManualMark.Checked,
        "automatic" => ManualMark.Automatic,
        _ => null,
    };

    /// <summary>The marks of a file's text; an unreadable or empty file has none, and an unknown state (a stored "cleared" too, 1215-C) is dropped.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> Read(string? text)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, ManualMark>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var plan in doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
            {
                var steps = new Dictionary<string, ManualMark>(StringComparer.Ordinal);
                foreach (var step in plan.Value.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.String))
                {
                    if (Parse(step.Value.GetString()) is { } mark && mark != ManualMark.Automatic)
                    {
                        steps[step.Name] = mark;
                    }
                }

                if (steps.Count > 0)
                {
                    result[plan.Name] = steps;
                }
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }

        return result;
    }

    /// <summary>The file's text for these marks: sorted, indented, LF line ends, a final newline.</summary>
    public static string Write(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> marks)
    {
        var sorted = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (plan, steps) in marks)
        {
            var inner = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (step, mark) in steps.Where(s => s.Value != ManualMark.Automatic))
            {
                inner[step] = Name(mark);
            }

            if (inner.Count > 0)
            {
                sorted[plan] = inner;
            }
        }

        return JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>The text with one mark set; Automatic removes it.</summary>
    public static string With(string? text, string plan, string step, ManualMark mark)
    {
        var marks = Read(text).ToDictionary(p => p.Key, p => (Dictionary<string, ManualMark>)new(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        if (!marks.TryGetValue(plan, out var steps))
        {
            marks[plan] = steps = new Dictionary<string, ManualMark>(StringComparer.Ordinal);
        }

        steps[step] = mark;
        return Write(marks.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, ManualMark>)p.Value, StringComparer.Ordinal));
    }

    /// <summary>The mark set for a plan's step (or <see cref="PlanKey"/>), Automatic when none.</summary>
    public static ManualMark Of(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks, string plan, string step) =>
        marks is not null && marks.TryGetValue(plan, out var steps) && steps.TryGetValue(step, out var mark) ? mark : ManualMark.Automatic;
}

/// <summary>
/// 1213-B: the marks clicked on the page and not yet on main, per project. A click sets one here at once and the
/// commit follows on the background; the views lay these over <c>plan-marks.json</c> (<see cref="Over"/>), so a
/// refresh inside the commit window already shows the click, and the next click cycles from here, not from main.
/// A committed mark stays until main says the same (<see cref="Prune"/>) or <see cref="Keep"/> has passed, so the
/// page does not flick back while main's model catches up; a failed commit drops the marks it carried.
/// </summary>
public sealed class PendingPlanMarks
{
    /// <summary>How long a committed mark is shown when main never comes to say it.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly Dictionary<(string Plan, string Step), Entry> _marks = [];
    private long _version;

    /// <summary>Moves with every change, for a views cache key.</summary>
    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <summary>A mark is waiting for its commit.</summary>
    public bool Uncommitted
    {
        get
        {
            lock (_gate)
            {
                return _marks.Values.Any(e => !e.Committed);
            }
        }
    }

    /// <summary>The pending mark of a plan's step, or null when none.</summary>
    public ManualMark? Of(string plan, string step)
    {
        lock (_gate)
        {
            return _marks.TryGetValue((plan, step), out var e) ? e.Mark : null;
        }
    }

    /// <summary>Sets a click's mark; it waits for a commit.</summary>
    public void Set(string plan, string step, ManualMark mark)
    {
        lock (_gate)
        {
            _marks[(plan, step)] = new Entry(mark, false, DateTimeOffset.MinValue);
            _version++;
        }
    }

    /// <summary>The marks waiting for a commit, in a stable order (plan, then step).</summary>
    public IReadOnlyList<(string Plan, string Step, ManualMark Mark)> ToCommit()
    {
        lock (_gate)
        {
            return [.. _marks.Where(p => !p.Value.Committed).OrderBy(p => p.Key.Plan, StringComparer.Ordinal).ThenBy(p => p.Key.Step, StringComparer.Ordinal)
                .Select(p => (p.Key.Plan, p.Key.Step, p.Value.Mark))];
        }
    }

    /// <summary>The carried marks are on main (at <paramref name="now"/>); one clicked again since keeps waiting.</summary>
    public void Committed(IEnumerable<(string Plan, string Step, ManualMark Mark)> carried, DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var (plan, step, mark) in carried)
            {
                if (_marks.TryGetValue((plan, step), out var e) && !e.Committed && e.Mark == mark)
                {
                    _marks[(plan, step)] = e with { Committed = true, At = now };
                }
            }

            _version++;
        }
    }

    /// <summary>The carried marks' commit failed: they are dropped; one clicked again since stays.</summary>
    public void Failed(IEnumerable<(string Plan, string Step, ManualMark Mark)> carried)
    {
        lock (_gate)
        {
            foreach (var (plan, step, mark) in carried)
            {
                if (_marks.TryGetValue((plan, step), out var e) && !e.Committed && e.Mark == mark)
                {
                    _marks.Remove((plan, step));
                }
            }

            _version++;
        }
    }

    /// <summary>Drops the committed marks main now says (or that are older than <see cref="Keep"/>).</summary>
    public void Prune(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> main, DateTimeOffset now)
    {
        lock (_gate)
        {
            var gone = _marks.Where(p => p.Value.Committed && (PlanMarkFile.Of(main, p.Key.Plan, p.Key.Step) == p.Value.Mark || now - p.Value.At >= Keep))
                .Select(p => p.Key).ToList();
            foreach (var key in gone)
            {
                _marks.Remove(key);
            }

            if (gone.Count > 0)
            {
                _version++;
            }
        }
    }

    /// <summary>Main's marks with the pending ones laid over them (automatic removes a mark).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> Over(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>> main)
    {
        lock (_gate)
        {
            if (_marks.Count == 0)
            {
                return main;
            }

            var result = main.ToDictionary(p => p.Key, p => new Dictionary<string, ManualMark>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (var ((plan, step), e) in _marks)
            {
                if (!result.TryGetValue(plan, out var steps))
                {
                    result[plan] = steps = new Dictionary<string, ManualMark>(StringComparer.Ordinal);
                }

                if (e.Mark == ManualMark.Automatic)
                {
                    steps.Remove(step);
                }
                else
                {
                    steps[step] = e.Mark;
                }
            }

            return result.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, ManualMark>)p.Value, StringComparer.Ordinal);
        }
    }

    private sealed record Entry(ManualMark Mark, bool Committed, DateTimeOffset At);
}
