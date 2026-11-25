// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// Card 1032-B: the startup benchmark runs its own pool beside the live one, so the pool's single-instance
/// name (MP-H1) can be set with <c>-PoolName:&lt;name&gt;</c>. Without it the name stays <c>pool</c>.
/// </summary>
public class PoolNameSwitchTests
{
    [Theory]
    [InlineData(new[] { "-Pool", "x.json", "-PoolName:perf1" }, "perf1")]
    [InlineData(new[] { "-Pool", "x.json", "-poolname=perf1" }, "perf1")]
    [InlineData(new[] { "-Pool", "x.json" }, "pool")]
    [InlineData(new[] { "-Pool", "x.json", "-PoolName:" }, "pool")]
    [InlineData(new[] { "-Pool", "x.json", "-PoolName:  " }, "pool")]
    public void The_pool_is_named_by_the_switch_else_pool(string[] args, string expected) =>
        Assert.Equal(expected, PoolNameSwitch.Name(args));
}
