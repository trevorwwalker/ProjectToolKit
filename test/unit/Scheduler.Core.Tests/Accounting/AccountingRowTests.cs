// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

public class AccountingRowTests
{
    private static AccountingRow Row(string card = "001-D-test-plan", string stop = "over the 120 min limit") => new()
    {
        Finished = new DateTimeOffset(2026, 11, 18, 11, 5, 0, TimeSpan.FromHours(-8)),
        Card = card,
        Category = "D",
        Slot = 4,
        Result = "done",
        Minutes = 62,
        LogBytes = 12345,
        StopReason = stop,
        Model = "opus",
    };

    // ACCT-1 (Account, schedule.ps1 L844): the header and one row per finished run.
    [Fact]
    public void Acct_Row_FormatAndHeader()
    {
        Assert.Equal(
            "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model",
            AccountingRow.Header);

        var row = Row();
        row = row with { Minutes = AccountingRow.MinutesOf(TimeSpan.FromMinutes(61.7)) };

        // The time is Get-Date -Format s; only stop_reason carries quotes; the model is last.
        Assert.Equal(
            "2026-11-18T11:05:00,001-D-test-plan,D,4,done,62,12345,\"over the 120 min limit\",opus",
            row.ToCsvLine());
        Assert.Equal(9, Fields(row.ToCsvLine()));

        // Minutes are [int] of the run's age (L2812): to the nearest, halves to even, so an
        // (int) cast would not do.
        Assert.Equal(62, AccountingRow.MinutesOf(TimeSpan.FromMinutes(61.7)));
        Assert.Equal(60, AccountingRow.MinutesOf(TimeSpan.FromMinutes(60.5)));
        Assert.Equal(62, AccountingRow.MinutesOf(TimeSpan.FromMinutes(61.5)));
        Assert.Equal(61, AccountingRow.MinutesOf(TimeSpan.FromMinutes(61.4)));
        Assert.Equal(0, AccountingRow.MinutesOf(TimeSpan.Zero));
    }

    // ACCT-1, blocked on C13 (Account, schedule.ps1 L852).
    // Spec C13: today's behaviour, kept until the developer decides (003-D review).
    [Fact]
    public void Acct_Row_QuoteOrCommaInFields()
    {
        // Only stop_reason is quoted and a " in it is not escaped, so the row as written cannot
        // be read back as the same fields.
        var quoted = Row(stop: "killed by \"dev\"").ToCsvLine();
        Assert.Equal(
            "2026-11-18T11:05:00,001-D-test-plan,D,4,done,62,12345,\"killed by \"dev\"\",opus",
            quoted);
        Assert.Contains("\"killed by \"dev\"\"", quoted);

        // A comma in a card name is not quoted either: the row gains a field (L852).
        var comma = Row(card: "010-A,x").ToCsvLine();
        Assert.Equal(
            "2026-11-18T11:05:00,010-A,x,D,4,done,62,12345,\"over the 120 min limit\",opus",
            comma);
        Assert.NotEqual(9, Fields(comma));

        // A plain row still reads back as nine fields, so this is only about these two characters.
        Assert.Equal(9, Fields(Row().ToCsvLine()));
    }

    private static int Fields(string line) => line.Split(',').Length;
}
