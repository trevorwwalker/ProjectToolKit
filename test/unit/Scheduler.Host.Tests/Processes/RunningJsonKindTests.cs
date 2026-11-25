// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;

namespace Scheduler.Host.Tests.Processes;

/// <summary>running.json keeps kind, cardType and version across a restart (EST-6).</summary>
public class RunningJsonKindTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EST_6_RunningJson_RoundTripsKindCardTypeAndVersion()
    {
        var worker = new RunningWorker
        {
            Slot = 1, Pid = 42, Start = Start, Card = "1357-B-x", Category = "B", Id = "1357-B",
            Kind = "design", CardType = "feature", CardVersion = "3.4.47",
        };

        var back = Assert.Single(RunningJson.Read(RunningJson.Write([worker]))).Worker;

        Assert.Equal("design", back.Kind);
        Assert.Equal("feature", back.CardType);
        Assert.Equal("3.4.47", back.CardVersion);
    }

    [Fact]
    public void EST_6_EntryWithoutThem_ReadsEmpty()
    {
        var text = "[{\"slot\":1,\"pid\":42,\"card\":\"1-A-x\",\"lane\":\"A\",\"id\":\"1-A\",\"started\":\"2026-11-24T12:00:00.0000000+00:00\"}]";

        var back = Assert.Single(RunningJson.Read(text)).Worker;

        Assert.Equal("", back.Kind);
        Assert.Equal("", back.CardType);
        Assert.Equal("", back.CardVersion);
    }
}
