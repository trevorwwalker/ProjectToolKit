// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Xunit;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>EST-8: accounting.csv's rows read by the header's column names.</summary>
public sealed class AccountingRowsTests
{
    private const string Header12 = "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version";
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EST_8_StopReasonWithCommaInQuotes_KeepsColumnsAligned()
    {
        var read = AccountingRows.Parse([
            Header12 + ",kind",
            "2026-11-24T10:00:00,5-A-x,A,1,stopped,7,10,\"stopped, then killed\",opus,Opus,bug,3.4.41,docs",
        ]);

        var row = Assert.Single(read.Rows);
        Assert.Equal("stopped, then killed", row["stop_reason"]);
        Assert.Equal("opus", row["model"]);
        Assert.Equal("3.4.41", row["version"]);
        Assert.Equal("docs", row["kind"]);
        Assert.Equal(7, row.Minutes);
        Assert.Equal(0, read.Skipped);
    }

    [Fact]
    public void EST_8_VersionLast_ModelIsStillTheModelColumn()
    {
        var lines = new[]
        {
            Header12,
            "2026-11-24T10:00:00,5-A-x,A,1,done,20,10,\"\",claude-sonnet-5-5,Sonnet,bug,3.4.41",
            "2026-11-24T10:30:00,6-A-y,A,1,done,30,10,\"\",claude-sonnet-5-5,Sonnet,feature,",
        };

        var runs = AreaDurations.RunsFromRows(lines, Now);

        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal("claude-sonnet-5-5", r.Model));
    }

    [Fact]
    public void EST_8_BadTimeOrMinutes_IsSkippedAndCounted()
    {
        var read = AccountingRows.Parse([
            Header12,
            "2026-11-24T10:00:00,5-A-x,A,1,done,20,10,\"\",opus,Opus,,",
            "not a time,6-A-y,A,1,done,20,10,\"\",opus,Opus,,",
            "2026-11-24T10:00:00,7-A-z,A,1,done,x,10,\"\",opus,Opus,,",
            "a line that is not a row",
        ]);

        Assert.Single(read.Rows);
        Assert.Equal(3, read.Skipped);
    }

    [Fact]
    public void EST_8_EightFieldRow_ReadsModelAndLaterColumnsAsEmpty()
    {
        var read = AccountingRows.Parse([
            Header12 + ",kind",
            "2026-11-20T10:00:00,5-A-x,A,1,done,20,10,\"\"",
        ]);

        var row = Assert.Single(read.Rows);
        Assert.Equal("", row["model"]);
        Assert.Equal("", row["kind"]);
        Assert.Equal("done", row["result"]);
    }

    [Fact]
    public void EST_8_FileWithAndWithoutKindColumn_BothRead()
    {
        var without = AccountingRows.Parse([Header12, "2026-11-24T10:00:00,5-A-x,A,1,done,20,10,\"\",opus,Opus,bug,1.0.0"]);
        var with = AccountingRows.Parse([Header12 + ",kind", "2026-11-24T10:00:00,5-A-x,A,1,done,20,10,\"\",opus,Opus,bug,1.0.0,build"]);

        Assert.Equal("", Assert.Single(without.Rows)["kind"]);
        Assert.Equal("build", Assert.Single(with.Rows)["kind"]);
    }

    [Fact]
    public void EST_8_NoHeader_ReadsTheOriginalNineColumns()
    {
        var read = AccountingRows.Parse(["2026-11-24T10:00:00,5-A-x,A,1,done,20,10,\"\",opus"]);

        Assert.Equal("opus", Assert.Single(read.Rows)["model"]);
    }
}
