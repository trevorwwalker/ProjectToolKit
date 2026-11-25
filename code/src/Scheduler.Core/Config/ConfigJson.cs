// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Config;

/// <summary>
/// scheduler.json read the way PowerShell reads it (G-1): property names match case-insensitively,
/// a property that is not there is <c>$null</c>, and <see cref="Cfg"/> decides what counts as "not
/// given" (CFG-5). Invalid JSON throws; the caller decides what a bad config means (CFG-3).
/// </summary>
internal static class ConfigJson
{
    /// <summary>
    /// The file's root object, or null when the text is blank or is not an object at all
    /// (ConvertFrom-Json of an empty file gives <c>$null</c>, and every setting then falls back).
    /// </summary>
    public static JsonObject? Root(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonNode.Parse(text) as JsonObject;
    }

    /// <summary>Whether the file has this key at all, even with a null value. WRK-4.</summary>
    public static bool Has(JsonObject? obj, string name)
    {
        if (obj is null)
        {
            return false;
        }

        foreach (var property in obj)
        {
            if (Text.Same(property.Key, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A text setting, or null when the file does not say one.</summary>
    public static string? GetString(JsonObject? obj, string name) => AsString(Get(obj, name));

    /// <summary>A number, or null. A quoted number counts as one, as <c>[int](Cfg ...)</c> did.</summary>
    public static int? GetInt(JsonObject? obj, string name)
    {
        var node = Get(obj, name);
        if (node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var number))
        {
            return number;
        }

        var text = AsString(node);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>A switch, or null. <c>"true"</c> and <c>"false"</c> as text mean what they say.</summary>
    public static bool? GetBool(JsonObject? obj, string name)
    {
        var node = Get(obj, name);
        switch (node?.GetValueKind())
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
            {
                var text = AsString(node);
                return bool.TryParse(text, out var parsed) ? parsed : null;
            }
        }
    }

    /// <summary>A section of the file, or null.</summary>
    public static JsonObject? GetObject(JsonObject? obj, string name) => Get(obj, name) as JsonObject;

    /// <summary>
    /// A list of text: one string is a list of one, as <c>@(Cfg $cfg.x ...)</c> was. Null when the
    /// file does not say one. Entries are dropped when <paramref name="dropEmpty"/> (L235).
    /// </summary>
    public static IReadOnlyList<string>? GetStringList(JsonObject? obj, string name, bool dropEmpty = false)
    {
        var node = Get(obj, name);
        if (node is null)
        {
            return null;
        }

        var list = new List<string>();
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var text = AsString(item) ?? "";
                if (!dropEmpty || text.Length > 0)
                {
                    list.Add(text);
                }
            }
        }
        else
        {
            var text = AsString(node);
            if (text is null)
            {
                return null;
            }

            if (!dropEmpty || text.Length > 0)
            {
                list.Add(text);
            }
        }

        return list;
    }

    /// <summary>
    /// A command line as the file holds it - a list of arguments, or one string - joined the way
    /// the script joined them before matching or passing them on (<c>$WorkerArgs -join ' '</c>).
    /// </summary>
    public static string? GetArgs(JsonObject? obj, string name)
    {
        var list = GetStringList(obj, name);
        return list is null ? null : string.Join(" ", list);
    }

    /// <summary>A section of name=text pairs: an <c>env</c> (L212, L229) or <c>cfg.categories</c>.</summary>
    public static IReadOnlyDictionary<string, string> GetMap(JsonObject? obj, string name)
    {
        var map = new Dictionary<string, string>(Text.Comparer);
        var section = GetObject(obj, name);
        if (section is null)
        {
            return map;
        }

        foreach (var property in section)
        {
            map[property.Key] = AsString(property.Value) ?? "";
        }

        return map;
    }

    private static JsonNode? Get(JsonObject? obj, string name)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var property in obj)
        {
            if (Text.Same(property.Key, name))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? AsString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.Number => node.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,

            // An object or an array as text: what [string]$p.Value made of it.
            _ => node.ToJsonString(),
        };
    }
}
