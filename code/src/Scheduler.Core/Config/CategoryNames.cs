// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Config;

/// <summary>
/// The category names the page shows (CFG-13). They come from <c>cfg.categories</c> until categories.json on main
/// has been read, and from categories.json after that - a project with no categories.json keeps the config's.
/// </summary>
public sealed class CategoryNames
{
    private readonly Dictionary<string, string> _names = new(Text.Comparer);

    /// <summary>Start from scheduler.json's <c>categories</c> (L277-278).</summary>
    public CategoryNames(SchedulerConfig config)
    {
        foreach (var pair in config.CategoryNames)
        {
            _names[pair.Key] = pair.Value;
        }
    }

    /// <summary>The names by category letter.</summary>
    public IReadOnlyDictionary<string, string> Names => _names;

    /// <summary>A category's name, or "" when no source has named it.</summary>
    public string NameOf(string letter) => _names.TryGetValue(letter, out var name) ? name : "";

    /// <summary>
    /// What <c>Read-Categories</c> does with categories.json (L1626-1630): its categories replace the config's
    /// names, and a file with no categories in it changes nothing.
    /// </summary>
    public void Read(CategoriesFile categories)
    {
        if (categories.Categories.Count == 0)
        {
            return;
        }

        _names.Clear();
        foreach (var pair in categories.Categories)
        {
            _names[pair.Key] = pair.Value.Name;
        }
    }
}
