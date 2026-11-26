// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Config;

/// <summary>
/// Where the category names shown on the page come from: <c>cfg.categories</c> until categories.json on main has
/// been read, and categories.json after that (CFG-13, L277-278 and <c>Read-Categories</c> L1622-1645).
/// Test plan: scheduler-next/test-plans/config.md.
/// </summary>
public class CategoryNamesTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    private static CategoriesFile CategoriesFileOf(params (string Letter, string Name)[] categories)
    {
        var map = new Dictionary<string, CategoryDefinition>(Text.Comparer);
        foreach (var (letter, name) in categories)
        {
            map[letter] = new CategoryDefinition { Letter = letter, Name = name };
        }

        return new CategoriesFile { Categories = map };
    }

    // 1486-A: scheduler.json's names under "categories", else "lanes"; "categories" wins.
    [Fact]
    public void Config_CategoryNames_EitherKey()
    {
        Assert.Equal("core", Load("""{"categories":{"A":"core"}}""").CategoryNames["A"]);
        Assert.Equal("core", Load("""{"lanes":{"A":"core"}}""").CategoryNames["A"]);
        Assert.Equal("new", Load("""{"lanes":{"A":"old"},"categories":{"A":"new"}}""").CategoryNames["A"]);
    }

    // CFG-13: cfg.categories before the first Read-Categories, categories.json's names after it.
    [Fact]
    public void Config_CategoryNames_FromConfigUntilCategoriesJsonRead()
    {
        var cfg = Load("""{"lanes":{"A":"core","B":"host"}}""");
        var names = new CategoryNames(cfg);

        // Before: the two of scheduler.json.
        Assert.Equal(new[] { "A", "B" }, names.Names.Keys.OrderBy(k => k, Text.SortComparer).ToArray());
        Assert.Equal("core", names.Names["A"]);
        Assert.Equal("host", names.NameOf("b"));
        Assert.Equal("", names.NameOf("C"));

        // After the first Read-Categories: the categories of main, including the new one.
        names.Read(CategoriesFileOf(("A", "core"), ("B", "host"), ("C", "web")));
        Assert.Equal(new[] { "A", "B", "C" }, names.Names.Keys.OrderBy(k => k, Text.SortComparer).ToArray());
        Assert.Equal("web", names.Names["C"]);

        // It is the names that change, and a category dropped from categories.json is dropped here too.
        names.Read(CategoriesFileOf(("A", "logic")));
        Assert.Equal(new[] { "A" }, names.Names.Keys.ToArray());
        Assert.Equal("logic", names.Names["A"]);

        // A categories.json with no categories in it says nothing: what is there stands (L1626).
        names.Read(new CategoriesFile());
        Assert.Equal("logic", names.Names["A"]);

        // A project with no cfg.categories starts with none and waits for categories.json.
        var none = new CategoryNames(Load("{}"));
        Assert.Empty(none.Names);
        none.Read(CategoriesFileOf(("A", "core")));
        Assert.Equal("core", none.Names["A"]);
    }
}
