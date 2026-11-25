// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Host.PageActions;

/// <summary>The page's category list (970-C), read from the categories file in its own order.</summary>
internal static class PageCategories
{
    /// <summary>One entry per category; its purpose and colour come from the category's own JSON, as Core keeps that whole.</summary>
    public static IReadOnlyList<PageCategory> From(CategoriesFile? file) =>
        file is null ? [] : [.. file.Categories.Values.Select(Of)];

    private static PageCategory Of(CategoryDefinition category)
    {
        JsonObject? def = null;
        try
        {
            def = JsonNode.Parse(category.Json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            // Not an object: the chip still shows the name.
        }

        return new PageCategory
        {
            Letter = category.Letter,
            Name = category.Name,
            Purpose = def?["purpose"]?.GetValue<string>() ?? "",
            Color = def?["color"]?.GetValue<string>() ?? "",
            OriginalLetter = category.Letter,
            Owns = category.Owns,
            Except = category.Except,
        };
    }
}
