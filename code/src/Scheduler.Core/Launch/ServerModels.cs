// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// What a model server's <c>/v1/models</c> text says about a model (<c>Server-Model</c> L2242,
/// <c>Model-Max</c> L2250): the entry whose id is the model, or the first entry when none is.
/// </summary>
public static class ServerModels
{
    /// <summary>The entry's <c>max_model_len</c>; 0 when the server said nothing usable.</summary>
    public static int MaxModelLen(string? modelsJson, string model)
    {
        if (string.IsNullOrWhiteSpace(modelsJson))
        {
            return 0;
        }

        JsonArray? data;
        try
        {
            data = JsonNode.Parse(modelsJson) is JsonObject root ? root["data"] as JsonArray : null;
        }
        catch (JsonException)
        {
            return 0;
        }

        if (data is null)
        {
            return 0;
        }

        JsonObject? first = null;
        JsonObject? entryOfModel = null;
        foreach (var item in data)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            first ??= entry;
            if (Text.Same(ConfigJson.GetString(entry, "id"), model))
            {
                entryOfModel = entry;
                break;
            }
        }

        return ConfigJson.GetInt(entryOfModel ?? first, "max_model_len") ?? 0;
    }
}
