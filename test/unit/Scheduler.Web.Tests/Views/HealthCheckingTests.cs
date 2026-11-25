// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>Known gap 930-C: a running check's sha is a field, the health line's words are only a fallback.</summary>
public sealed class HealthCheckingTests
{
    [Fact]
    public void Sha_FieldSet_WinsOverRewordedText()
    {
        var health = new PageHealthView { Text = "a check is under way", CheckingSha = "abc1234" };

        Assert.Equal("abc1234", HealthChecking.Sha(health));
    }

    [Fact]
    public void Sha_FieldEmpty_FallsBackToTheText()
    {
        var health = new PageHealthView { Text = "main abc1234 green at 12:25; checking def5678 (3 min)" };

        Assert.Equal("def5678", HealthChecking.Sha(health));
    }

    [Fact]
    public void Sha_NothingRunning_IsEmpty()
    {
        Assert.Equal("", HealthChecking.Sha(new PageHealthView { Text = "main abc1234 green at 12:25" }));
    }
}
