// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json.Nodes;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Estimates;
using Scheduler.Core.Health;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Processes;
using Scheduler.Host.Tests.Slots;
using static Scheduler.Host.Tests.Processes.ProcessKit;

namespace Scheduler.Host.Tests.State;

/// <summary>EST-27 to EST-30: the estimates file and the pool's rows. Test plan: estimates.md section 7.</summary>
public sealed class EstimatesFileTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly ListLog _log = new();

    public void Dispose() => _temp.Dispose();

    private string Folder(string name)
    {
        var dir = Path.Combine(_temp.Path, name);
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private string EstimatesPath(string name) => Path.Combine(_temp.Path, name + "-repo", "jobs", ".logs", "estimates.json");

    // One accounting.csv with `count` done rows of `card` (one finished a minute apart), all today.
    private void Accounting(string logDir, int count, string card = "12-A-x", string type = "Opus")
    {
        var lines = new List<string> { AccountingCsv.Header };
        for (var i = 0; i < count; i++)
        {
            var when = _clock.Now.LocalDateTime.AddHours(-1).AddMinutes(-i).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            lines.Add($"{when},{card},A,1,done,10,10,\"\",claude-opus-5-5,{type},feature,,design");
        }

        File.WriteAllLines(Path.Combine(logDir, "accounting.csv"), lines);
    }

    private EstimatesFile File_(string name, string logDir, EstimatesPool? pool = null) =>
        new(name, logDir, EstimatesPath(name), _clock, "9.9.9", _log, pool);

    [Fact]
    public void EST_29_AFinishingRun_WritesTheFile_AndNothingElseDoes()
    {
        var logs = Folder("logs");
        Accounting(logs, 3);
        var file = File_("P", logs);
        Assert.False(File.Exists(file.Path));

        var running = new Dictionary<int, RunningWorker> { [2] = Worker(2, 777, "010-A", _clock.Now.AddMinutes(-5)) };
        var accounting = new AccountingCsv(logs, _clock, _log);
        new WorkerFinisher(_clock, _log, accounting, new HealthCheck(new SchedulerConfig(), _clock, _log, new NoHealthRunner()),
                new RecordingProcesses(), new SlotFiles(logs), file)
            .Finish(running[2], "done", "m", running);

        Assert.True(File.Exists(file.Path));
        Assert.Equal(1, file.Writes);
        var report = EstimatesReport.FromJson(File.ReadAllText(file.Path));
        Assert.Equal("P", report!.Project);
        Assert.Equal("9.9.9", report.Scheduler);
        Assert.Equal(file.Json, File.ReadAllText(file.Path)); // the route's text is the file's
        Assert.False(File.Exists(file.Path + ".tmp"));
    }

    [Fact]
    public void EST_29_AnIdleTick_DoesNotWriteTheFile_AReviewStayDoes()
    {
        var logs = Folder("logs");
        Accounting(logs, 3);
        var file = File_("P", logs);
        file.Changed(); // start-up
        Assert.Equal(1, file.Writes);

        var stays = new ReviewStaysFile(logs, _temp.Path, "jobs", new GitRunner(), inProcess: (_, to) =>
            [new MainCommit(to, "p", _clock.Now, "jobs: review", [new PathChange("jobs/review/1-A-a.md", null, "1")])]);
        stays.Changed += file.Changed;
        var before = File.GetLastWriteTimeUtc(file.Path);
        var wrote = file.Writes;

        Assert.True(stays.Advance("c1", _ => null));
        Assert.Equal(wrote + 1, file.Writes);

        // The idle ticks: the snapshot's commit is the head, so nothing is read and nothing written.
        var after = File.GetLastWriteTimeUtc(file.Path);
        for (var i = 0; i < 3; i++)
        {
            Assert.False(stays.Advance("c1", _ => null));
        }

        Assert.Equal(wrote + 1, file.Writes);
        Assert.Equal(after, File.GetLastWriteTimeUtc(file.Path));
        Assert.True(after >= before);
    }

    [Fact]
    public void EST_30_TheWriteGoesThroughTmpAndAMove_AHeldTargetLogsOnce_AndTheNextTriggerSucceeds()
    {
        var logs = Folder("logs");
        Accounting(logs, 3);
        var file = File_("P", logs);
        file.Changed();
        Assert.False(File.Exists(file.Path + ".tmp"));
        var first = File.ReadAllText(file.Path);

        // Held open without delete sharing: the move fails, the old file stays whole and the temporary one stays behind.
        using (var held = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            file.Changed();
            file.Changed();
            Assert.Equal(first, new StreamReader(held).ReadToEnd());
        }

        Assert.True(File.Exists(file.Path + ".tmp"));
        Assert.Single(_log.Lines, l => l.StartsWith("estimates:", StringComparison.Ordinal));
        Assert.Equal(1, file.Writes);

        file.Changed();

        Assert.Equal(2, file.Writes);
        Assert.False(File.Exists(file.Path + ".tmp"));
        Assert.NotNull(EstimatesReport.FromJson(File.ReadAllText(file.Path)));
    }

    [Fact]
    public void EST_28_TheFolderIsCreated_WhenItIsMissing()
    {
        var logs = Folder("logs");
        var file = File_("P", logs);
        Assert.False(Directory.Exists(Path.GetDirectoryName(file.Path)));

        file.Changed();

        Assert.True(File.Exists(file.Path)); // no accounting.csv yet: an empty report, not an error
        Assert.Empty(EstimatesReport.FromJson(File.ReadAllText(file.Path))!.RunTime);
    }

    [Fact]
    public void EST_27_TwoMembersWithTheSameLogFolder_AreReadOnce()
    {
        var logs = Folder("shared");
        Accounting(logs, 3);
        var pool = new EstimatesPool(_clock, "9.9.9", _log);
        var a = File_("A", logs, pool);
        var b = File_("B", Path.Combine(logs, "..", "SHARED"), pool); // the same folder, spelled another way

        a.Changed();

        Assert.Single(pool.LogFolders());
        Assert.Equal(1, pool.Folders.Reads);
        Assert.NotNull(a.Json);
        Assert.NotNull(b.Json);
        Assert.True(File.Exists(a.Path));
        Assert.True(File.Exists(b.Path)); // a change in any member rebuilds every member's file (EST-29)

        // Nothing changed on disk: another trigger reads nothing again.
        b.Changed();
        Assert.Equal(1, pool.Folders.Reads);
    }

    [Fact]
    public void EST_27_Card12AOfTwoProjects_IsTwoCards()
    {
        var one = Folder("one");
        var two = Folder("two");
        Accounting(one, 1, "12-A-x");
        Accounting(two, 1, "12-A-y");
        var pool = new EstimatesPool(_clock, "9.9.9", _log);
        var a = File_("A", one, pool);
        _ = File_("B", two, pool);

        a.Changed();

        var outcome = JsonNode.Parse(pool.Json!)!["outcomes"]!.AsArray().Single()!;
        Assert.Equal(2, (int)outcome["cards"]!);
        Assert.Null(JsonNode.Parse(pool.Json!)!["project"]);
    }

    [Fact]
    public void EST_31_AMemberLeaving_RebuildsTheOthers()
    {
        var one = Folder("one");
        var two = Folder("two");
        Accounting(one, 3);
        Accounting(two, 3);
        var pool = new EstimatesPool(_clock, "9.9.9", _log);
        var a = File_("A", one, pool);
        _ = File_("B", two, pool);
        a.Changed();
        Assert.Equal(6, JsonNode.Parse(a.Json!)!["rows"]!["pool"]!.GetValue<int>());

        pool.Remove("B");

        Assert.Equal(3, JsonNode.Parse(a.Json!)!["rows"]!["pool"]!.GetValue<int>());
        Assert.Null(pool.For("B"));
    }
}
