// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Pool;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>Card 1209-B: a pool refuses a second project on a repository an earlier project already uses.</summary>
[ParallelMethods]
public sealed class PoolConfigTests
{
    private static PoolConfig Parse(string repoB) =>
        PoolConfig.Parse($$"""{ "projects": [ { "name": "A", "repo": "C:/r/a" }, { "name": "B", "repo": "{{repoB.Replace("\\", "\\\\")}}" } ] }""");

    [Theory]
    [InlineData("C:/r/a")]
    [InlineData("C:\\r\\a\\")]
    [InlineData("c:/R/A/")]
    [InlineData("C:/r/x/../a")]
    [InlineData("C:/r/./a")]
    public void SameFolder_UnderAnySpelling_LeavesTheSecondOut(string repoB)
    {
        var config = Parse(repoB);
        Assert.Equal(["A"], config.Projects.Select(p => p.Name));
        var line = Assert.Single(config.Skipped);
        Assert.Equal($"pool: B left out: its repository {repoB} is already used by A", line);
    }

    [Fact]
    public void DifferentFolders_BothJoin()
    {
        var config = Parse("C:/r/b");
        Assert.Equal(["A", "B"], config.Projects.Select(p => p.Name));
        Assert.Empty(config.Skipped);
    }
}
