// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>1030-A: the card_type and version columns, appended last.</summary>
public class AccountingCardTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static AccountingRow Row() => new()
    {
        Finished = new DateTimeOffset(2026, 11, 21, 11, 5, 0, TimeSpan.Zero),
        Card = "1030-A-x",
        Category = "A",
        Slot = 1,
        Result = "done",
        Minutes = 5,
        LogBytes = 1,
        StopReason = "",
        Model = "sonnet",
        CardType = "bug",
        Version = "3.0.61",
    };

    [Fact]
    public void Row_WritesCardTypeAndVersionLast()
    {
        Assert.Equal(",card_type,version", AccountingRow.CardColumns);
        Assert.Equal(",bug,3.0.61", Row().CardFields());
        Assert.Equal(",,", (Row() with { CardType = "", Version = "" }).CardFields());
        Assert.Equal(",bug,a b", (Row() with { Version = "a,b" }).CardFields());
    }

    [Fact]
    public void RunsFromRows_ReadsTypeAndVersionByName_OldRowsUntyped()
    {
        var lines = new[]
        {
            "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version",
            "2026-11-21T10:00:00,900-A-old,A,1,done,10,1,\"\",opus",                      // before the type column
            "2026-11-21T10:30:00,901-A-old,A,1,done,10,1,\"\",opus,Sonnet",               // worker type, no card type
            "2026-11-21T11:00:00,1030-A-x,A,1,done,5,1,\"\",sonnet,Sonnet,bug,3.0.61",
            "2026-11-21T11:30:00,1031-A-y,A,1,done,7,1,\"\",opus,Opus,feature,",
        };

        var runs = Trends.RunsFromRows(lines, Now, 7, TimeZoneInfo.Utc);

        Assert.Equal(4, runs.Count);
        Assert.Equal(["", "", "bug", "feature"], runs.Select(r => r.CardType));
        Assert.Equal(["opus", "opus", "sonnet", "opus"], runs.Select(r => r.Model));
        Assert.Equal("3.0.61", runs[2].Version);
        Assert.Equal("", runs[3].Version);
    }

    [Fact]
    public void RunsFromRows_NoHeader_StillReadsOldFiles()
    {
        var runs = Trends.RunsFromRows(
            ["2026-11-21T10:00:00,900-A-old,A,1,done,10,1,\"\",opus"], Now, 7, TimeZoneInfo.Utc);
        Assert.Equal("opus", Assert.Single(runs).Model);
        Assert.Equal("", runs[0].CardType);
    }

    [Fact]
    public void ByType_GroupsFeatureBugUntyped()
    {
        RunStat Run(string card, string type, int minutes) => new()
        {
            Finished = Now, Result = "done", Minutes = minutes, Model = "m", Card = card, CardType = type,
        };

        var totals = Trends.ByType([Run("1-A", "bug", 5), Run("2-A", "", 7), Run("3-A", "feature", 10), Run("1-A", "bug", 3)]);

        Assert.Equal(["feature", "bug", ""], totals.Select(t => t.CardType));
        Assert.Equal([1, 2, 1], totals.Select(t => t.Runs));
        Assert.Equal([10, 8, 7], totals.Select(t => t.Minutes));
        Assert.Equal([1, 1, 1], totals.Select(t => t.Cards));
    }
}
