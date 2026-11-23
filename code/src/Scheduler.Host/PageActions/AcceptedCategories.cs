using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The accepted category definitions in the log folder's <c>categories-accepted.json</c> (LANE-2, LANE-3).
/// Core compares them when it reads categories.json; LANE-3 stores one category's new definition here when
/// the developer accepts it from the page, which is what releases a held category on the next tick.
/// </summary>
public static class AcceptedCategories
{
    /// <summary>Sets one category's accepted definition to <paramref name="json"/>, leaving the others as they are.</summary>
    public static void Store(IStateFiles files, string category, string json)
    {
        var accepted = new Dictionary<string, string>(Load(files), Text.Comparer);
        accepted[category] = json;
        files.Write(StateFile.CategoriesAccepted, Serialize(accepted));
    }

    /// <summary>The definitions accepted so far, by letter; empty when the file is missing or unreadable.</summary>
    public static IReadOnlyDictionary<string, string> Load(IStateFiles files)
    {
        var accepted = new Dictionary<string, string>(Text.Comparer);
        var text = files.Read(StateFile.CategoriesAccepted);
        if (string.IsNullOrWhiteSpace(text))
        {
            return accepted;
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject obj)
            {
                foreach (var property in obj)
                {
                    accepted[property.Key] = property.Value is JsonValue value && value.TryGetValue<string>(out var s)
                        ? s
                        : property.Value?.ToJsonString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable file counts as none, as CategoryRegistry does (L1612).
        }

        return accepted;
    }

    private static string Serialize(IReadOnlyDictionary<string, string> accepted)
    {
        var obj = new JsonObject();
        foreach (var key in accepted.Keys.OrderBy(k => k, Text.SortComparer))
        {
            obj[key] = accepted[key];
        }

        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
