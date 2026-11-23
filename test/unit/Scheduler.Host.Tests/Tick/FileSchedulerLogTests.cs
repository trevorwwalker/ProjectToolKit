using Scheduler.Core.Contracts;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// The scheduler's own log written to a file (690-B): every line the console and the tray's View log
/// show also reaches <c>jobs/.logs/scheduler-YYYYMMDD.log</c>, a new file at each local midnight,
/// flushed line by line so a hang or a kill keeps everything up to it; dated files older than seven
/// days go at start and at the day change; and a file that cannot be written is said once instead of
/// stopping the scheduler. The spec has no rule for the scheduler's own log file, so the card is the
/// authority for these tests.
/// </summary>
public class FileSchedulerLogTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 19, 7, 40, 12, DateTimeKind.Local));

    // The console's lines reach the dated file unchanged - same text, same time stamp - and each line
    // is on disk before Write returns.
    [Fact]
    public void Log690B_ConsoleLines_LandInTheDatedFileAsWritten()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var console = new List<string>();
        var log = new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), console.Add);

        log.Write("scheduler started: 2 slots");
        log.Write("tick: green");

        // Read while the log is still alive: nothing waits in a buffer for the process to close.
        var file = Path.Combine(logs.Path, "scheduler-20261119.log");
        Assert.Equal(
            ["07:40:12 scheduler started: 2 slots", "07:40:12 tick: green"],
            File.ReadAllLines(file));
        Assert.Equal(File.ReadAllLines(file), console);

        // The state folder's undated file is what -Compare reads (390-B); it keeps working.
        Assert.Equal(File.ReadAllLines(file), File.ReadAllLines(Path.Combine(state.Path, "scheduler.log")));
    }

    // A new file at midnight: the lines either side of it are in the file of their own day.
    [Fact]
    public void Log690B_Midnight_StartsANewDatedFile()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var clock = new FakeClock(new DateTimeOffset(new DateTime(2026, 11, 19, 23, 59, 50, DateTimeKind.Local)));
        var log = new FileSchedulerLog(state.Path, logs.Path, clock);

        log.Write("before midnight");
        clock.Advance(TimeSpan.FromSeconds(25));
        log.Write("after midnight");

        Assert.Equal(
            ["23:59:50 before midnight"],
            File.ReadAllLines(Path.Combine(logs.Path, "scheduler-20261119.log")));
        Assert.Equal(
            ["00:00:15 after midnight"],
            File.ReadAllLines(Path.Combine(logs.Path, "scheduler-20261120.log")));
    }

    // At start the dated files older than the seven days that end today go; the rest, and any file
    // whose name carries no date, stay.
    [Fact]
    public void Log690B_Start_RemovesDatedFilesOlderThanSevenDays_KeepsTheRest()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var today = DateOnly.FromDateTime(Now.LocalDateTime);
        var kept = new[] { today, today.AddDays(-1), today.AddDays(-6) };
        var gone = new[] { today.AddDays(-7), today.AddDays(-8), today.AddDays(-40) };
        foreach (var day in kept.Concat(gone))
        {
            File.WriteAllText(Path.Combine(logs.Path, $"scheduler-{day:yyyyMMdd}.log"), "old\n");
        }

        File.WriteAllText(Path.Combine(logs.Path, "scheduler-notes.log"), "not a daily log\n");
        File.WriteAllText(Path.Combine(logs.Path, "010-B-20261118-071500.log"), "a worker log\n");

        new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now));

        foreach (var day in kept)
        {
            Assert.True(File.Exists(Path.Combine(logs.Path, $"scheduler-{day:yyyyMMdd}.log")), $"day {day} was kept");
        }

        foreach (var day in gone)
        {
            Assert.False(File.Exists(Path.Combine(logs.Path, $"scheduler-{day:yyyyMMdd}.log")), $"day {day} was removed");
        }

        Assert.True(File.Exists(Path.Combine(logs.Path, "scheduler-notes.log")), "an undated scheduler- file was left alone");
        Assert.True(File.Exists(Path.Combine(logs.Path, "010-B-20261118-071500.log")), "a worker log was left alone");
    }

    // The other half of the retention rule: a scheduler that runs across midnight prunes then too.
    [Fact]
    public void Log690B_DayChange_RemovesDatedFilesOlderThanSevenDays()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var clock = new FakeClock(Now);
        var log = new FileSchedulerLog(state.Path, logs.Path, clock);
        log.Write("tick: green");

        var today = DateOnly.FromDateTime(Now.LocalDateTime);
        var stale = Path.Combine(logs.Path, $"scheduler-{today.AddDays(-7):yyyyMMdd}.log");
        File.WriteAllText(stale, "old\n");

        clock.Advance(TimeSpan.FromHours(24));
        log.Write("next day");

        Assert.False(File.Exists(stale), "the seven-day-old file went at the day change");
        Assert.True(
            File.Exists(Path.Combine(logs.Path, $"scheduler-{today.AddDays(1):yyyyMMdd}.log")),
            "the new day has its own file");
    }

    // A locked log file (another process has it open, or the disk is full) is said once in the log
    // the developer can see, and the scheduler keeps going.
    [Fact]
    public void Log690B_LogFileLocked_WarnsOnceAndCarriesOn()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var console = new List<string>();
        var log = new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), console.Add);
        var file = Path.Combine(logs.Path, "scheduler-20261119.log");
        File.WriteAllText(file, "");
        using var held = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

        log.Write("first");
        log.Write("second");
        log.Write("third");

        Assert.Contains("07:40:12 first", console, StringComparer.Ordinal);
        Assert.Contains("07:40:12 third", console, StringComparer.Ordinal);
        Assert.Single(console, line => line.Contains("is not being written", StringComparison.Ordinal));
        Assert.Contains("scheduler-20261119.log", console.Single(line => line.Contains("is not being written", StringComparison.Ordinal)), StringComparison.Ordinal);

        // The failure is the file's only: the rest of the log still works, here the state folder's file.
        Assert.Equal(3, File.ReadAllLines(Path.Combine(state.Path, "scheduler.log")).Length);
    }

    // A log folder that cannot be made at all is the same case: one line about it, no exception.
    [Fact]
    public void Log690B_LogFolderCannotBeMade_WarnsOnceAndCarriesOn()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var blocked = Path.Combine(logs.Path, ".logs");
        File.WriteAllText(blocked, "a file where the log folder should be");
        var console = new List<string>();
        var log = new FileSchedulerLog(state.Path, blocked, new FakeClock(Now), console.Add);

        log.Write("first");
        log.Write("second");

        Assert.Equal(2, console.Count(line => line.Contains("07:40:12 ", StringComparison.Ordinal)));
        Assert.Single(console, line => line.Contains("is not being written", StringComparison.Ordinal));
    }

    // jobs/.logs under the repository, git ignored (690-B).
    [Fact]
    public void Log690B_RollingFolder_IsLogsUnderTheJobsFolder()
    {
        var folder = FileSchedulerLog.RollingFolder(new SchedulerConfig { Repo = @"C:\repos\Proj", JobsDir = "jobs/" });

        Assert.Equal(Path.Combine(@"C:\repos\Proj", "jobs", ".logs"), folder);
    }

    // 1117-B: the undated file is capped. Past the limit scheduler.log becomes scheduler.1.log and the
    // next line starts a new scheduler.log, so the pool's log stops growing without end.
    [Fact]
    public void Log1117B_FlatFileOverTheLimit_RollsTo_1_AndANewFileStarts()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var flat = Path.Combine(state.Path, "scheduler.log");
        File.WriteAllText(flat, new string('x', 2_000));

        var log = new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), flatMaxBytes: 1_000);
        log.Write("after the roll");

        Assert.Equal(new string('x', 2_000), File.ReadAllText(Path.Combine(state.Path, "scheduler.1.log")));
        Assert.Equal(["07:40:12 after the roll"], File.ReadAllLines(flat));
    }

    // Keeping three old files: .1 goes to .2, .2 to .3, and the copy that was .3 is deleted.
    [Fact]
    public void Log1117B_Roll_ShiftsTheOldCopies_AndDropsTheFourth()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        File.WriteAllText(Path.Combine(state.Path, "scheduler.log"), new string('n', 2_000));
        File.WriteAllText(Path.Combine(state.Path, "scheduler.1.log"), "one\n");
        File.WriteAllText(Path.Combine(state.Path, "scheduler.2.log"), "two\n");
        File.WriteAllText(Path.Combine(state.Path, "scheduler.3.log"), "three\n");

        new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), flatMaxBytes: 1_000);

        Assert.StartsWith(new string('n', 100), File.ReadAllText(Path.Combine(state.Path, "scheduler.1.log")));
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(state.Path, "scheduler.2.log")));
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(state.Path, "scheduler.3.log")));
        Assert.False(File.Exists(Path.Combine(state.Path, "scheduler.4.log")), "a fourth old copy was kept");
        Assert.All(
            Directory.GetFiles(state.Path, "scheduler*.log"),
            file => Assert.DoesNotContain("three", File.ReadAllText(file), StringComparison.Ordinal));
    }

    // Q1, answered yes by the developer: a copy no cap could have made - the pool's gigabyte file
    // (1104-B), which is OversizeFactor times over the limit or worse - goes with the roll instead of
    // sitting in the queue until three more rolls push it out. A copy a normal roll left behind stays.
    [Fact]
    public void Log1117B_Roll_DeletesAnOversizedOldCopy_AndKeepsTheOrdinaryOnes()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        // Limit 1_000, so a copy over 4_000 bytes could only have grown that big without a cap.
        File.WriteAllText(Path.Combine(state.Path, "scheduler.log"), new string('n', 2_000));
        File.WriteAllText(Path.Combine(state.Path, "scheduler.1.log"), new string('o', 9_000));
        File.WriteAllText(Path.Combine(state.Path, "scheduler.2.log"), new string('m', 1_500));
        var console = new List<string>();

        new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), console.Add, flatMaxBytes: 1_000);

        Assert.StartsWith(new string('n', 100), File.ReadAllText(Path.Combine(state.Path, "scheduler.1.log")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(state.Path, "scheduler.2.log")), "the oversize copy was kept");
        Assert.Equal(new string('m', 1_500), File.ReadAllText(Path.Combine(state.Path, "scheduler.3.log")));
        Assert.Single(console, line => line.Contains("scheduler.2.log", StringComparison.Ordinal)
            && line.Contains("was deleted with the roll", StringComparison.Ordinal));
    }

    // The pool's own case: the file that is already gigabytes becomes the first copy and is deleted by
    // that same roll, so the space comes back at the first start rather than after three more rolls.
    [Fact]
    public void Log1117B_Roll_DeletesTheOversizedFileItJustRolled()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var flat = Path.Combine(state.Path, "scheduler.log");
        File.WriteAllText(flat, new string('x', 9_000));
        var console = new List<string>();

        var log = new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), console.Add, flatMaxBytes: 1_000);
        log.Write("after the roll");

        Assert.Empty(Directory.GetFiles(state.Path, "scheduler.*.log"));
        Assert.Equal(["07:40:12 after the roll"], File.ReadAllLines(flat));
        Assert.Single(console, line => line.Contains("scheduler.1.log", StringComparison.Ordinal)
            && line.Contains("was deleted with the roll", StringComparison.Ordinal));
    }

    // A roll that cannot finish deletes nothing: history is only thrown away by a roll that worked, and
    // the oversize copy waits for the next check the way the roll itself does.
    [Fact]
    public void Log1117B_RollBlocked_DeletesNothingUntilTheRollWorks()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var flat = Path.Combine(state.Path, "scheduler.log");
        File.WriteAllText(flat, new string('n', 2_000));
        var oversize = Path.Combine(state.Path, "scheduler.1.log");
        File.WriteAllText(oversize, new string('o', 9_000));
        var held = Path.Combine(state.Path, "scheduler.2.log");
        File.WriteAllText(held, new string('m', 1_500));
        var console = new List<string>();
        var clock = new FakeClock(Now);

        FileSchedulerLog log;
        using (new FileStream(held, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            // Nothing can shift, so nothing may be deleted either.
            log = new FileSchedulerLog(state.Path, logs.Path, clock, console.Add, flatMaxBytes: 1_000);
            Assert.StartsWith(new string('o', 100), File.ReadAllText(oversize), StringComparison.Ordinal);
            Assert.DoesNotContain(console, line => line.Contains("was deleted", StringComparison.Ordinal));
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        log.Write("third");

        Assert.StartsWith(new string('n', 100), File.ReadAllText(oversize), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(state.Path, "scheduler.2.log")), "the oversize copy kept its gigabytes");
        Assert.Equal(new string('m', 1_500), File.ReadAllText(Path.Combine(state.Path, "scheduler.3.log")));
        Assert.Equal(["07:41:12 third"], File.ReadAllLines(flat));
        Assert.Single(console, line => line.Contains("was deleted with the roll", StringComparison.Ordinal));
    }

    // A copy another process holds stops the roll: it is said once for that check, the lines keep
    // being written where they were, and the next check rolls the file.
    [Fact]
    public void Log1117B_RollBlockedByALockedCopy_WarnsOnceAndIsRetriedAtTheNextCheck()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var flat = Path.Combine(state.Path, "scheduler.log");
        File.WriteAllText(flat, new string('n', 2_000));
        var held = Path.Combine(state.Path, "scheduler.1.log");
        File.WriteAllText(held, "one\n");
        var console = new List<string>();
        var clock = new FakeClock(Now);

        // Another process has the first copy open, so the roll at start cannot finish.
        FileSchedulerLog log;
        using (new FileStream(held, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            log = new FileSchedulerLog(state.Path, logs.Path, clock, console.Add, flatMaxBytes: 1_000);
            log.Write("first");
            log.Write("second");

            Assert.Single(console, line => line.Contains("did not roll over", StringComparison.Ordinal));
            var warning = console.Single(line => line.Contains("did not roll over", StringComparison.Ordinal));
            Assert.Contains("scheduler.log", warning, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(state.Path, "scheduler.2.log")), "the roll went half way");
            Assert.StartsWith(new string('n', 100), File.ReadAllText(flat), StringComparison.Ordinal);
            Assert.Contains("07:40:12 second", File.ReadAllText(flat), StringComparison.Ordinal);
        }

        // The next check finds the copy free and rolls it.
        clock.Advance(TimeSpan.FromMinutes(1));
        log.Write("third");

        Assert.StartsWith(new string('n', 100), File.ReadAllText(held), StringComparison.Ordinal);
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(state.Path, "scheduler.2.log")));
        Assert.Equal(["07:41:12 third"], File.ReadAllLines(flat));
        Assert.Single(console, line => line.Contains("did not roll over", StringComparison.Ordinal));
    }

    // The size is looked at once a minute, not per line: a minute that passes the limit twice over
    // still rolls the file once, at the check.
    [Fact]
    public void Log1117B_SizeCheck_IsOnceAMinute_NotPerLine()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var flat = Path.Combine(state.Path, "scheduler.log");
        File.WriteAllText(flat, new string('a', 1_500));
        var clock = new FakeClock(Now);

        var log = new FileSchedulerLog(state.Path, logs.Path, clock, flatMaxBytes: 1_000);
        Assert.False(File.Exists(flat), "the file left over the limit did not roll at start");

        for (var line = 0; line < 200; line++)
        {
            log.Write($"tick {line}");
        }

        Assert.Equal(200, File.ReadAllLines(flat).Length);
        Assert.Single(Directory.GetFiles(state.Path, "scheduler.*.log"));

        clock.Advance(TimeSpan.FromMinutes(1));
        log.Write("next check");

        Assert.Equal(["07:41:12 next check"], File.ReadAllLines(flat));
        Assert.Equal(2, Directory.GetFiles(state.Path, "scheduler.*.log").Length);
    }

    // The dated files and their seven-day rule are untouched by the cap: they live in their own
    // folder and are kept or deleted by the day, never by size.
    [Fact]
    public void Log1117B_Roll_LeavesTheDatedFilesAndTheirRetentionAlone()
    {
        using var state = new TempDir();
        using var logs = new TempDir();
        var today = DateOnly.FromDateTime(Now.LocalDateTime);
        var dated = Path.Combine(logs.Path, $"scheduler-{today:yyyyMMdd}.log");
        File.WriteAllText(dated, "today\n");
        var stale = Path.Combine(logs.Path, $"scheduler-{today.AddDays(-7):yyyyMMdd}.log");
        File.WriteAllText(stale, "stale\n");
        File.WriteAllText(Path.Combine(state.Path, "scheduler.log"), new string('x', 2_000));

        var log = new FileSchedulerLog(state.Path, logs.Path, new FakeClock(Now), flatMaxBytes: 1_000);
        log.Write("a line");

        Assert.False(File.Exists(stale), "the seven-day rule stopped working");
        Assert.Equal(["today", "07:40:12 a line"], File.ReadAllLines(dated));
        Assert.Empty(Directory.GetFiles(logs.Path, "scheduler.*.log"));
    }
}
