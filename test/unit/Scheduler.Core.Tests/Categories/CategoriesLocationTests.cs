// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Categories;
using Xunit;

namespace Scheduler.Core.Tests.Categories;

/// <summary>979-A: categories.json is read when it exists, else lanes.json.</summary>
public class CategoriesLocationTests
{
    private static Func<string, string?> Files(params (string Path, string Text)[] files) =>
        path => files.Where(f => f.Path == path).Select(f => f.Text).FirstOrDefault();

    [Fact]
    public void Categories_BothFiles_CategoriesJsonWins()
    {
        var read = Files(("jobs/categories.json", "new"), ("jobs/lanes.json", "old"));
        Assert.Equal("new", CategoriesLocation.Read("jobs", read));
        Assert.Equal("jobs/categories.json", CategoriesLocation.Rel("jobs", read));
    }

    [Fact]
    public void Categories_OnlyLanesJson_StillRead()
    {
        var read = Files(("jobs/lanes.json", "old"));
        Assert.Equal("old", CategoriesLocation.Read("jobs", read));
        Assert.Equal("jobs/lanes.json", CategoriesLocation.Rel("jobs", read));
    }

    [Fact]
    public void Categories_NeitherFile_Null()
    {
        Assert.Null(CategoriesLocation.Read("jobs", Files()));
    }
}
