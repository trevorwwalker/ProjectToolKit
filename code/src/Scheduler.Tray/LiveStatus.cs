using System.Text.Json;

namespace Scheduler.Tray;

/// <summary>What the tooltip takes from <c>/live.json</c>: <c>{t, pageDone, page, slots:{n:{log, stats, time}}}</c>, one slot per running worker.</summary>
public static class LiveStatus
{
    /// <summary>The number of running workers, or null when <paramref name="json"/> is not the live feed.</summary>
    public static int? Workers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("slots", out var slots)
                && slots.ValueKind == JsonValueKind.Object
                    ? slots.EnumerateObject().Count()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
