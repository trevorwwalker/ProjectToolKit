// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json.Nodes;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Pool;

/// <summary>MP-4: each project's weight, from the top-level config's <c>projects</c> list.</summary>
public static class PoolWeights
{
    /// <summary>
    /// The weight of every project in <c>{"projects": [{"name": ..., "weight": 70}, ...]}</c>, by
    /// name (case-insensitive, G-1). A number in quotes counts, as elsewhere in the config. A
    /// project without a name is skipped. A project without a weight gets the smallest weight of
    /// the others (spec B3 Q5); when none has one, every project weighs 1.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Parse(string? json)
    {
        var weights = new Dictionary<string, int>(Text.Comparer);
        if (ConfigJson.Root(json)?["projects"] is not JsonArray projects)
        {
            return weights;
        }

        var missing = new List<string>();
        foreach (var project in projects.OfType<JsonObject>())
        {
            var name = ConfigJson.GetString(project, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (ConfigJson.GetInt(project, "weight") is int w)
            {
                weights[name] = w;
            }
            else
            {
                missing.Add(name);
            }
        }

        var smallest = weights.Count > 0 ? weights.Values.Min() : 1;
        foreach (var name in missing)
        {
            weights.TryAdd(name, smallest);
        }

        return weights;
    }
}
