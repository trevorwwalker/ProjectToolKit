// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;

namespace Scheduler.Core.Tests.Config;

/// <summary>The <c>locks</c> switch in scheduler.json (design <c>dynamic-locks.md</c>, section 8 card 9; DL-13): on by default (1477-B).</summary>
public sealed class LockSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"locks":false}""")]
    [InlineData("not json {")]
    public void Locks_Off_WhenFalseOrNoUsableConfig(string? json)
    {
        var s = LockSettings.Load(json);

        Assert.False(s.On);
        Assert.Equal(20, s.WaitMinutes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"locks":true}""")]
    [InlineData("""{"locks":"yes please"}""")]
    [InlineData("""{"locks":null}""")]
    public void Locks_On_WhenNotFalse(string json)
    {
        Assert.True(LockSettings.Load(json).On);
    }

    [Fact]
    public void Locks_SwitchedOn_WithItsWaitLimit()
    {
        var s = LockSettings.Load("""{"locks":true,"limits":{"lockWaitMinutes":7}}""");

        Assert.True(s.On);
        Assert.Equal(TimeSpan.FromMinutes(7), s.WaitLimit);
    }

    [Fact]
    public void Locks_WaitLimitBelowOne_IsTheDefault()
    {
        Assert.Equal(20, LockSettings.Load("""{"locks":true,"limits":{"lockWaitMinutes":0}}""").WaitMinutes);
    }

    [Fact]
    public void Locks_OffValueIsOff() => Assert.False(LockSettings.Off.On);
}
