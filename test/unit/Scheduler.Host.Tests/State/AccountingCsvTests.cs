using System.Globalization;
using Scheduler.Core.Accounting;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.State;

/// <summary>
/// accounting.csv in the log folder at start-up: the old header rewritten in place and the last
/// 48 hours read into the figures (ACCT-4, <c>Seed-RunStats</c> L863), and the last ten runs read
/// back as Recent events with the log that belongs to each (ACCT-5, <c>Seed-Recent</c> L812).
/// Test plan: accounting.md.
/// </summary>
public class AccountingCsvTests
{
    // The rows are written and read as local wall-clock text, so the tests do not depend on the PC's zone.
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local));

    // ACCT-4: the header without ,model is rewritten in place, the rows are kept, and only the
    // rows of the last 48 hours reach the figures.
    [Fact]
    public void Acct_Startup_OldHeaderRewritten_Last48hLoaded()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllLines(path,
        [
            "finished,card,lane,slot,result,minutes,log_bytes,stop_reason",
            OldRow(Now.AddHours(-72), "010-A-old", "A", 1, "done", 30, "time limit"),
            OldRow(Now.AddHours(-47), "020-B-mid", "B", 2, "review", 45, "page stop"),
            OldRow(Now.AddHours(-1), "030-C-new", "C", 3, "done", 12, "LANE-RESULT: done"),
        ]);

        var stats = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog()).SeedRunStats();

        var lines = File.ReadAllLines(path);
        Assert.Equal(AccountingCsv.Header, lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains("010-A-old", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"time limit\"", lines[1], StringComparison.Ordinal);

        Assert.Equal(2, stats.Count);
        Assert.Equal("020-B", stats[0].Card);
        Assert.Equal("review", stats[0].Result);
        Assert.Equal(45, stats[0].Minutes);
        Assert.Equal("", stats[0].Model);
        Assert.Equal("030-C", stats[1].Card);
        Assert.Equal(Now.AddHours(-1).ToOffset(Now.Offset), stats[1].Finished);
    }

    // ACCT-5: the last ten rows, newest first, each with the newest log that began no later than
    // a minute after the run finished.
    [Fact]
    public void Acct_SeedRecent_LastTenWithLogs()
    {
        using var dir = new TempDir();
        var rows = new List<string> { AccountingCsv.Header };
        for (var i = 1; i <= 12; i++)
        {
            rows.Add(Row(Now.AddMinutes(-120 + (10L * i)), $"010-A-run{i:D3}", 3, "review", 20 + i, "gpt"));
        }

        File.WriteAllLines(Path.Combine(dir.Path, "accounting.csv"), rows);

        // Three logs for card 010-A: one from before them all, one 30 s after the newest finished,
        // one 2 min after it.
        var before = Stamp(Now.AddMinutes(-30));
        var soon = Stamp(Now.AddSeconds(30));
        var late = Stamp(Now.AddMinutes(2));
        foreach (var stamp in new[] { before, soon, late })
        {
            File.WriteAllText(Path.Combine(dir.Path, $"010-A-{stamp}.log"), "log");
        }

        var recent = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog()).SeedRecent();

        Assert.Equal(10, recent.Count);
        Assert.Equal(Now, recent[0].When);
        Assert.Equal("010-A-run012", recent[0].Card);
        Assert.Equal(3, recent[0].Slot);
        Assert.Equal("review", recent[0].Result);
        Assert.Equal(32, recent[0].Minutes);
        Assert.Equal($"010-A-{soon}.log", Path.GetFileName(recent[0].Log));
        Assert.Equal($"010-A-{before}.log", Path.GetFileName(recent[1].Log));
        Assert.Equal($"010-A-{before}.log", Path.GetFileName(recent[3].Log));
        Assert.Null(recent[4].Log);
        Assert.DoesNotContain(recent, e => Path.GetFileName(e.Log) == $"010-A-{late}.log");
        Assert.Equal(Enumerable.Range(3, 10).Select(i => $"010-A-run{i:D3}"), recent.Select(e => e.Card).Reverse());
    }

    // ACCT-5 (931-A, developer 2026-11-19): a card number is a positive integer of any width, so
    // the log of a four-digit card is a log like any other: <id>-<start>.log is read for the row.
    [Fact]
    public void Acct_SeedRecent_CardIdOfAnyWidth()
    {
        using var dir = new TempDir();
        File.WriteAllLines(Path.Combine(dir.Path, "accounting.csv"),
        [
            AccountingCsv.Header,
            Row(Now.AddMinutes(-20), "007-B-run", 4, "review", 15, "opus"),
            Row(Now.AddMinutes(-10), "1000-A-run", 3, "done", 20, "opus"),
        ]);
        File.WriteAllText(Path.Combine(dir.Path, $"007-B-{Stamp(Now.AddMinutes(-20).AddSeconds(30))}.log"), "leading zero output");
        File.WriteAllText(Path.Combine(dir.Path, $"1000-A-{Stamp(Now.AddMinutes(-10).AddSeconds(30))}.log"), "four digit output");

        var recent = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog()).SeedRecent();

        // Newest row first, each with the log whose id is the card's number exactly as written.
        Assert.Equal(2, recent.Count);
        Assert.Equal("1000-A-run", recent[0].Card);
        Assert.Equal($"1000-A-{Stamp(Now.AddMinutes(-10).AddSeconds(30))}.log", Path.GetFileName(recent[0].Log));
        Assert.Equal("007-B-run", recent[1].Card);
        Assert.Equal($"007-B-{Stamp(Now.AddMinutes(-20).AddSeconds(30))}.log", Path.GetFileName(recent[1].Log));
    }

    // ACCT-6 (450-B): the figures the page shows are the seeded runs plus every run added since.
    [Fact]
    public void Acct_Runs_SeededThenEveryAddedRun()
    {
        using var dir = new TempDir();
        File.WriteAllLines(Path.Combine(dir.Path, "accounting.csv"),
        [
            "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model",
            Row(Now.AddHours(-1), "010-A-old", 1, "done", 12, "opus"),
        ]);
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());
        accounting.SeedRunStats();

        accounting.Add(new AccountingRow
        {
            Finished = Now,
            Card = "020-B-new",
            Category = "B",
            Slot = 2,
            Result = "review",
            Minutes = 30,
            LogBytes = 0,
            StopReason = "",
            Model = "sonnet",
        }, view: null);

        Assert.Equal(["010-A", "020-B"], accounting.Runs.Select(r => r.Card));
        Assert.Equal("sonnet", accounting.Runs[1].Model);
        Assert.Equal(30, accounting.Runs[1].Minutes);
    }

    // TREND-3 (630-A, 770-B): the Trends window reads the file's own rows for the last 14 local
    // days, which reach further back than the 48 h the figures keep (ACCT-4).
    [Fact]
    public void Trend_Last14Days_FromFile()
    {
        using var dir = new TempDir();
        File.WriteAllLines(Path.Combine(dir.Path, "accounting.csv"),
        [
            AccountingCsv.Header,
            Row(Now.AddDays(-20), "010-A-old", 1, "done", 30, "opus"),
            Row(Now.AddDays(-13).AddSeconds(-1), "020-B-mid", 2, "review", 45, "sonnet"),
            Row(Now, "030-C-new", 3, "done", 12, "sonnet"),
        ]);

        var runs = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog()).TrendRuns(Now);

        Assert.Equal(["020-B", "030-C"], runs.Select(r => r.Card));
        Assert.Equal("sonnet", runs[1].Model);
        Assert.Equal(45, runs[0].Minutes);
        Assert.Equal("review", runs[0].Result);
    }

    // TREND-3: the lines are kept from one page rebuild to the next and re-read only when the file's
    // last write moves.
    [Fact]
    public void Trend_CachedUntilTheFileChanges()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        var written = new DateTime(2026, 11, 18, 11, 0, 0, DateTimeKind.Utc);
        var one = Row(Now.AddHours(-1), "010-A-one", 1, "done", 10, "opus");
        File.WriteAllLines(path, [AccountingCsv.Header, one]);
        File.SetLastWriteTimeUtc(path, written);
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());
        Assert.Single(accounting.TrendRuns(Now));

        // New rows under the old stamp are not seen: the cached lines still answer.
        File.WriteAllLines(path, [AccountingCsv.Header, one, Row(Now.AddHours(-2), "020-B-two", 2, "done", 20, "opus")]);
        File.SetLastWriteTimeUtc(path, written);
        Assert.Single(accounting.TrendRuns(Now));

        // The stamp moves, the file is read again.
        File.SetLastWriteTimeUtc(path, written.AddMinutes(1));
        Assert.Equal(2, accounting.TrendRuns(Now).Count);
    }

    // TREND-3: a file that is not there, or is held open by another program, leaves the chart on the
    // figures Host already holds in memory - no throw, and the log says so.
    [Fact]
    public void Trend_UnreadableFile_FallsBackToFigures()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllLines(path, [AccountingCsv.Header, Row(Now.AddHours(-1), "010-A-one", 1, "done", 10, "opus")]);
        var log = new ListLog();
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), log);
        accounting.SeedRunStats();

        File.Delete(path);
        Assert.Equal(["010-A"], accounting.TrendRuns(Now).Select(r => r.Card));

        File.WriteAllLines(path,
        [
            AccountingCsv.Header,
            Row(Now.AddHours(-1), "010-A-one", 1, "done", 10, "opus"),
            Row(Now.AddHours(-3), "020-B-two", 2, "done", 20, "opus"),
        ]);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Single(accounting.TrendRuns(Now));
        }

        Assert.Contains(log.Lines, l => l.Contains("for the trends", StringComparison.Ordinal));
    }

    // TREND-3: a file with nothing in it is as good as none, as it was when the snapshot read it.
    [Fact]
    public void Trend_EmptyFile_FallsBackToFigures()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllLines(path, [AccountingCsv.Header, Row(Now.AddHours(-1), "010-A-one", 1, "done", 10, "opus")]);
        var accounting = new AccountingCsv(dir.Path, new FakeClock(Now), new ListLog());
        accounting.SeedRunStats();
        File.WriteAllLines(path, []);

        Assert.Equal(["010-A"], accounting.TrendRuns(Now).Select(r => r.Card));
    }

    private static string OldRow(DateTimeOffset finished, string card, string category, int slot, string result, int minutes, string why) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6},\"{7}\"",
            finished.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            card,
            category,
            slot,
            result,
            minutes,
            1024,
            why);

    private static string Row(DateTimeOffset finished, string card, int slot, string result, int minutes, string model) =>
        new AccountingRow
        {
            Finished = finished,
            Card = card,
            Category = card.Split('-')[1],   // 931-A: the card number is any width, so the category is after the dash
            Slot = slot,
            Result = result,
            Minutes = minutes,
            LogBytes = 1024,
            StopReason = "page stop",
            Model = model,
        }.ToCsvLine();

    private static string Stamp(DateTimeOffset when) => when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    // 1118-B: a project that has never run a worker has no accounting.csv: empty trends, and nothing in the log.
    [Fact]
    public void A_missing_file_gives_empty_figures_and_no_log_line()
    {
        using var dir = new TempDir();
        var log = new ListLog();
        var csv = new AccountingCsv(dir.Path, new FakeClock(Now), log);

        Assert.Empty(csv.SeedRunStats());
        Assert.Empty(csv.TrendRuns(Now));
        Assert.Empty(csv.TrendRuns(Now));
        Assert.Empty(csv.SeedRecent());
        Assert.Empty(log.Lines);
    }

    // 1118-B: another read error is logged once until its text changes.
    [Fact]
    public void A_read_error_is_logged_once()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllText(path, AccountingCsv.Header + "\n");
        var log = new ListLog();
        var csv = new AccountingCsv(dir.Path, new FakeClock(Now), log);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            for (var i = 0; i < 4; i++)
            {
                csv.TrendRuns(Now);
            }
        }

        Assert.Single(log.Lines, l => l.Contains("for the trends", StringComparison.Ordinal));
    }
}
