using Scheduler.Core.Contracts;

namespace Scheduler.Core.Categories;

/// <summary>A worker's own category for its prompt (LANE-5, <c>Category-Brief</c> L1680).</summary>
public static class CategoryBrief
{
    /// <summary>
    /// What the category owns, its exceptions, what else it may touch and the shared files; "" when
    /// categories.json has not been read or has no such category.
    /// </summary>
    public static string For(CategoriesFile? categories, string letter, string categoriesRel)
    {
        if (categories is null || !categories.Categories.TryGetValue(letter, out var d))
        {
            return "";
        }

        var t = $"Category {letter} ({d.Name}) owns: {string.Join(", ", d.Owns)}";
        if (d.Except.Count > 0)
        {
            t += $"; but not: {string.Join(", ", d.Except)}";
        }

        if (d.AlsoOwns.Length > 0)
        {
            t += $"; also {d.AlsoOwns}";
        }

        t += ".";
        if (categories.AddOnly.Count > 0)
        {
            t += $" Any category may add to (not rewrite): {string.Join(", ", categories.AddOnly)}.";
        }

        if (categories.Rerecord.Count > 0)
        {
            var rule = categories.RerecordRule.Length > 0 ? $" ({categories.RerecordRule})" : "";
            t += $" Any category may re-record, for its own gated change: {string.Join(", ", categories.Rerecord)}{rule}.";
        }

        return $"{t} All categories are defined in {categoriesRel}.";
    }
}
