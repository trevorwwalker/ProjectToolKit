using System.Text.Json;

namespace Scheduler.Core.Planner;

/// <summary>
/// The page's "Planner push" switch as <c>planner-state.json</c> holds it (PLAN-1): off unless the
/// file says <c>"push": true</c>; a missing or unreadable file means off.
/// </summary>
public static class PlannerSwitch
{
    /// <summary>The switch from the file's text.</summary>
    public static bool Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (Contracts.Text.Same(p.Name, "push"))
                {
                    return p.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.Number => p.Value.GetRawText() != "0",
                        JsonValueKind.String => p.Value.GetString() is { Length: > 0 },
                        _ => false,
                    };
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    /// <summary>The file's text for the switch.</summary>
    public static string Serialize(bool push) => JsonSerializer.Serialize(new Dictionary<string, bool> { ["push"] = push });
}
