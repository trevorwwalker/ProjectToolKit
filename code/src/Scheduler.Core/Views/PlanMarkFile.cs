using System.Text.Json;

namespace Scheduler.Core.Views;

/// <summary>What the developer set on a plan or step by clicking its mark (1125-C).</summary>
public enum ManualMark
{
    /// <summary>Worked out from the children.</summary>
    Automatic,

    /// <summary>A green check whatever the children say.</summary>
    Checked,

    /// <summary>An empty box whatever the children say.</summary>
    Cleared,
}

/// <summary>
/// <c>jobs/plan-marks.json</c>: the marks the developer set, by plan path and step label
/// (<c>{"jobs/plans/x_plan.md": {"3": "checked", "3.1": "cleared"}}</c>). The plan itself is keyed <see cref="PlanKey"/>.
/// </summary>
public static class PlanMarkFile
{
    /// <summary>The key of the plan's own mark.</summary>
    public const string PlanKey = "plan";

    /// <summary>The file's name in the jobs folder.</summary>
    public const string FileName = "plan-marks.json";

    /// <summary>The next state when the mark is clicked: automatic, checked, cleared, automatic.</summary>
    public static ManualMark Next(ManualMark mark) => mark switch
    {
        ManualMark.Automatic => ManualMark.Checked,
        ManualMark.Checked => ManualMark.Cleared,
        _ => ManualMark.Automatic,
    };

    public static string Name(ManualMark mark) => mark switch
    {
        ManualMark.Checked => "checked",
        ManualMark.Cleared => "cleared",
        _ => "automatic",
    };

    public static ManualMark? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "checked" => ManualMark.Checked,
        "cleared" => ManualMark.Cleared,
        "automatic" => ManualMark.Automatic,
        _ => null,
    };

    /// <summary>The marks of a file's text; an unreadable or empty file has none, and an unknown state is dropped.</summary>
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
