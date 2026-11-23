using Scheduler.Core.Config;

namespace Scheduler.Core.Tests.Config;

/// <summary>The <c>locks</c> switch in scheduler.json (design <c>dynamic-locks.md</c>, section 8 card 9; DL-13): off by default.</summary>
public sealed class LockSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"locks":false}""")]
    [InlineData("""{"locks":"yes please"}""")]
    [InlineData("not json {")]
    public void Locks_OffUnlessSwitchedOn(string? json)
    {
        var s = LockSettings.Load(json);

        Assert.False(s.On);
        Assert.Equal(20, s.WaitMinutes);
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
    public void Locks_DefaultIsOff() => Assert.False(LockSettings.Off.On);
}
