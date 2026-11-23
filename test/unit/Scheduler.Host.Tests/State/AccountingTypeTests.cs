using Scheduler.Core.Accounting;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.State;

/// <summary>accounting.csv's type column (WT-P6). Test plan: worker-types.md.</summary>
public class AccountingTypeTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Local));

    // WT-P6: an old header (before the model column, or before the type column) is rewritten in place
    // with the type column at the end, old rows are kept, and a new row ends with its worker's type.
    [Theory]
    [InlineData("finished,card,lane,slot,result,minutes,log_bytes,stop_reason")]
    [InlineData("finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model")]
    public void Accounting_TypeColumn_OldHeaderRewritten(string oldHeader)
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllLines(path, [oldHeader, "2026-11-19T11:00:00,010-A-old,A,1,done,30,5,\"x\""]);
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());

        accounting.SeedRunStats();
        accounting.Add(new AccountingRow
        {
            Finished = Now, Card = "880-B-wt-host", Category = "B", Slot = 2, Result = "done", Minutes = 7, LogBytes = 9,
            StopReason = "", Model = "sonnet",
        }, null, "Sonnet");

        var lines = File.ReadAllLines(path);
        Assert.Equal(AccountingCsv.Header, lines[0]);
        Assert.EndsWith(",model,type,card_type,version", lines[0], StringComparison.Ordinal);
        Assert.Contains("010-A-old", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",sonnet,Sonnet,,", lines[2], StringComparison.Ordinal);
    }

    // WT-P6: a file that does not exist yet starts with the full header.
    [Fact]
    public void Accounting_NewFile_StartsWithTypeColumn()
    {
        using var dir = new TempDir();
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());

        accounting.Add(new AccountingRow
        {
            Finished = Now, Card = "880-B-x", Category = "B", Slot = 1, Result = "done", Minutes = 1, LogBytes = 1,
            StopReason = "", Model = "m",
        }, null);

        var lines = File.ReadAllLines(accounting.FilePath);
        Assert.Equal(AccountingCsv.Header, lines[0]);
        Assert.EndsWith(",m,,,", lines[1], StringComparison.Ordinal);
    }
}
