// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Contracts;

public class CfgTests
{
    // CFG-5
    [Fact]
    public void Config_CfgHelper_NullAndEmptyMeanDefault_ZeroAndFalseAreValues()
    {
        Assert.Equal("D", Cfg.Value((string?)null, "D"));
        Assert.Equal("D", Cfg.Value("", "D"));
        Assert.Equal(0, Cfg.Value((int?)0, 7));
        Assert.False(Cfg.Value((bool?)false, true));
        Assert.Equal("x", Cfg.Value("x", "D"));

        // The same five values untyped, as they come out of a JSON document.
        Assert.Equal("D", Cfg.Value((object?)null, "D"));
        Assert.Equal("D", Cfg.Value((object?)"", "D"));
        Assert.Equal(0, Cfg.Value((object?)0, "D"));
        Assert.Equal(false, Cfg.Value((object?)false, "D"));
        Assert.Equal("x", Cfg.Value((object?)"x", "D"));

        // Null for a value type is the default too.
        Assert.Equal(7, Cfg.Value((int?)null, 7));
        Assert.True(Cfg.Value((bool?)null, true));
    }
}
