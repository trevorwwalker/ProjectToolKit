// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Pool;
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Pool;

/// <summary>1193-A: the pool file's <c>toolkit</c> is the default of its projects; a project's own wins.</summary>
public class PoolToolkitDefaultTests
{
    [Fact]
    public void Toolkit_PoolValueIsParsed()
    {
        Assert.Equal("kit", PoolConfig.Parse("""{"toolkit":"kit"}""").Toolkit);
    }

    [Fact]
    public void Toolkit_ProjectWithNone_TakesThePoolDefault()
    {
        Assert.Equal("pool-kit", ProjectParts.ToolkitSetting("{}", "pool-kit"));
        Assert.Equal("pool-kit", ProjectParts.ToolkitSetting("""{"toolkit":""}""", "pool-kit"));
    }

    [Fact]
    public void Toolkit_ProjectsOwnSetting_Wins()
    {
        Assert.Equal("own-kit", ProjectParts.ToolkitSetting("""{"toolkit":"own-kit"}""", "pool-kit"));
    }

    [Fact]
    public void Toolkit_NeitherSet_IsNull()
    {
        Assert.Null(ProjectParts.ToolkitSetting("{}", ""));
        Assert.Null(ProjectParts.ToolkitSetting("not json", null));
    }
}
