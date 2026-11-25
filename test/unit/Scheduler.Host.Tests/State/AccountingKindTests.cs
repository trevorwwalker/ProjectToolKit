// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.State;

/// <summary>accounting.csv's kind column (EST-5, EST-7). Test plan: estimates.md section 7.</summary>
public class AccountingKindTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Local));

    private static AccountingRow Row(string kind) => new()
    {
        Finished = Now, Card = "1357-B-x", Category = "B", Slot = 1, Result = "done", Minutes = 3, LogBytes = 1,
        StopReason = "", Model = "m", CardType = "feature", Version = "3.4.47", Kind = kind,
    };

    // EST-5: the header ends ",kind" and a row's last field is its kind.
    [Fact]
    public void EST_5_KindColumn_IsLast()
    {
        using var dir = new TempDir();
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());

        accounting.Add(Row("design"), null, "Opus");
        accounting.Add(Row(""), null, "Opus");

        var lines = File.ReadAllLines(accounting.FilePath);
        Assert.EndsWith(",model,type,card_type,version,kind", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(",m,Opus,feature,3.4.47,design", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",m,Opus,feature,3.4.47,", lines[2], StringComparison.Ordinal);
    }

    // EST-7: a file with the 1030-A header gains ",kind" on its first line; every row is kept byte for byte.
    [Fact]
    public void EST_7_OldHeader_GainsKind_RowsKeptByteForByte()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        const string oldHeader = "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version";
        string[] rows =
        [
            "2026-11-24T10:00:00,900-A-old,A,1,done,30,5,\"x\",opus,Opus,bug,3.0.1",
            "2026-11-24T10:30:00,901-A-old,A,1,done,10,1,\"\",opus",
        ];
        File.WriteAllLines(path, [oldHeader, .. rows]);
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());

        var runs = accounting.SeedRunStats();

        var lines = File.ReadAllLines(path);
        Assert.Equal(oldHeader + ",kind", lines[0]);
        Assert.Equal(AccountingCsv.Header, lines[0]);
        Assert.Equal(rows, lines.Skip(1));
        Assert.Equal(2, runs.Count);
    }
}
