// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json.Nodes;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Categories;

/// <summary>categories.json read into a <see cref="CategoriesFile"/> (LANE-1, <c>Read-Categories</c> L1622).</summary>
internal static class CategoriesJson
{
    /// <summary>
    /// The file's categories and shared files, or null when it has no <c>categories</c> section (the file is
    /// then ignored, L1626). Invalid JSON throws <see cref="System.Text.Json.JsonException"/>.
    /// Each category's <see cref="CategoryDefinition.Json"/> is its definition as compact JSON, so
    /// formatting is never a change (LANE-2).
    /// </summary>
    public static CategoriesFile? Parse(string text)
    {
        var root = ConfigJson.Root(text);
        var section = ConfigJson.GetObject(root, "lanes");
        if (section is null)
        {
            return null;
        }

        var categories = new Dictionary<string, CategoryDefinition>(Text.Comparer);
        foreach (var property in section)
        {
            var def = property.Value as JsonObject;
            categories[property.Key] = new CategoryDefinition
            {
                Letter = property.Key,
                Name = ConfigJson.GetString(def, "name") ?? "",
                Owns = ConfigJson.GetStringList(def, "owns") ?? [],
                Except = ConfigJson.GetStringList(def, "except") ?? [],
                AlsoOwns = ConfigJson.GetString(def, "alsoOwns") ?? "",
                Json = property.Value?.ToJsonString() ?? "null",
            };
        }

        var shared = ConfigJson.GetObject(root, "shared");
        return new CategoriesFile
        {
            Categories = categories,
            AddOnly = ConfigJson.GetStringList(shared, "addOnly") ?? [],
            Rerecord = ConfigJson.GetStringList(shared, "rerecord") ?? [],
            RerecordRule = ConfigJson.GetString(shared, "rerecordRule") ?? "",
        };
    }
}
