// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Tests.MainModel;
using Scheduler.Core.WebContract;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Views;

namespace Scheduler.Host.Tests.Views;

/// <summary>1131-B (VW-20, VW-22): the views snapshot carries accounting.csv's runs and the running slots.</summary>
public sealed class ViewsSourceRunsTests
{
    private const string Head = "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version";
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 14, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string> Files() => new(StringComparer.Ordinal)
    {
        ["jobs/queue/10-A-alpha-one.md"] = "# 10-A: Alpha one\n\nCategory: A\nStatus: ready\n",
    };

    private static string Row(string finished, string card, int minutes) =>
        $"{finished},{card},A,2,done,{minutes},100,\"\",claude-x,Sonnet,feature,3.0.1";

    [Fact]
    public void VW_22_RunsAppended_AreReadFromWhereTheLastReadStopped()
    {
        using var dir = new TempDir();
        var csv = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllText(csv, Head + "\n" + Row("2026-11-22T10:30:00", "9-A-old", 30) + "\n");
        var source = new ViewsSource("ptk", "jobs", () => new FakeMain(Files()), () => [], accountingPath: csv);

        Assert.True(source.Refresh());
        var run = Assert.Single(source.Current.Snapshot.Runs);
        Assert.Equal("9-A", run.CardId);
        Assert.Equal(30, (run.End!.Value - run.Start).TotalMinutes);
        Assert.Equal(("done", "claude-x", "Sonnet", "2"), (run.Result, run.Model, run.WorkerType, run.Slot));
        var first = source.Current.Snapshot.Version;

        File.AppendAllText(csv, Row("2026-11-22T11:30:00", "10-A-alpha-one", 20) + "\n");
        Assert.True(source.Refresh());
        Assert.Equal(["9-A", "10-A"], source.Current.Snapshot.Runs.Select(r => r.CardId));
        Assert.NotEqual(first, source.Current.Snapshot.Version);
    }

    [Fact]
    public void RunLog_NeverReadsTheOldPartAgain_AndWaitsForAWholeLine()
    {
        using var dir = new TempDir();
        var csv = Path.Combine(dir.Path, "accounting.csv");
        var log = new RunLog(csv);
        Assert.False(log.Refresh());
        Assert.Empty(log.Runs);

        var head = Head + "\n" + Row("2026-11-22T10:30:00", "9-A-old", 30) + "\n";
        File.WriteAllText(csv, head);
        Assert.True(log.Refresh());
        Assert.Equal(head.Length, log.BytesRead);

        var next = Row("2026-11-22T11:30:00", "10-A-alpha-one", 20);
        File.AppendAllText(csv, next[..20]);
        Assert.False(log.Refresh());
        Assert.Single(log.Runs);

        File.AppendAllText(csv, next[20..] + "\n");
        Assert.True(log.Refresh());
        Assert.Equal(2, log.Runs.Count);
        Assert.Equal(head.Length + 20 + next.Length + 1, log.BytesRead); // the half line is read again, the old part never
    }

    [Fact]
    public void VW_22_AMissingFile_IsNoRuns()
    {
        using var dir = new TempDir();
        var source = new ViewsSource("ptk", "jobs", () => new FakeMain(Files()), () => [], accountingPath: Path.Combine(dir.Path, "accounting.csv"));

        Assert.True(source.Refresh());
        Assert.Empty(source.Current.Snapshot.Runs);
        Assert.Null(source.LastError);
    }

    [Fact]
    public void VW_22_AWorkerStartOrEnd_MovesTheVersion_AndTheRunIsInTheSnapshot()
    {
        var slots = new List<PageSlotView>();
        var source = new ViewsSource("ptk", "jobs", () => new FakeMain(Files()), () => slots);
        Assert.True(source.Refresh());
        var idle = source.Current.Snapshot.Version;

        slots = [new PageSlotView { Slot = 3, Card = "10-A-alpha-one", Start = Start, TimeLimit = 60, Model = "claude-x", Type = "Sonnet" }];
        Assert.True(source.Refresh());
        var run = Assert.Single(source.Current.Snapshot.Runs);
        Assert.True(run.Running);
        Assert.Equal(("10-A", "claude-x", "Sonnet", "3"), (run.CardId, run.Model, run.WorkerType, run.Slot));
        Assert.Equal(Start, run.Start);
        Assert.Equal(Start.AddMinutes(60), run.TimeBoxEnd);
        Assert.NotEqual(idle, source.Current.Snapshot.Version);
        Assert.False(source.Refresh());

        slots = [];
        Assert.True(source.Refresh());
        Assert.Empty(source.Current.Snapshot.Runs);
        Assert.Equal(idle, source.Current.Snapshot.Version);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 20, 0, 0, TimeSpan.Zero);

    private static string Done(string finished, string card, int minutes) =>
        $"{finished},{card},A,2,done,{minutes},100,\"\",claude-x,Sonnet,feature,3.0.1";

    [Theory]
    [InlineData(4, 2, 2)]
    [InlineData(2, 4, 2)]
    [InlineData(3, 0, 3)]
    [InlineData(0, 5, 1)]
    public void VW_19_Workers_IsTheSmallerOfWorkspacesAndMaxWorkers(int spaces, int max, int expected)
    {
        var source = new ViewsSource("ptk", "jobs", () => new FakeMain(Files()), () => [], workspaces: () => spaces, maxWorkers: () => max);

        Assert.True(source.Refresh());
        Assert.Equal(expected, source.Current.Snapshot.Workers);
    }

    [Fact]
    public void VW_19_Typicals_ComeFromTheRuns_AndAnAppendedRunChangesThemAndTheVersion()
    {
        using var dir = new TempDir();
        var csv = Path.Combine(dir.Path, "accounting.csv");
        File.WriteAllText(csv, Head + "\n" + Done("2026-11-22T10:00:00", "1-A-a", 10) + "\n" + Done("2026-11-22T11:00:00", "2-A-b", 20) + "\n"
            + Done("2026-11-22T12:00:00", "3-A-c", 30) + "\n");
        var source = new ViewsSource("ptk", "jobs", () => new FakeMain(Files()), () => [], accountingPath: csv, now: () => Now);

        Assert.True(source.Refresh());
        var typical = Assert.Single(source.Current.Snapshot.Typicals);
        Assert.Equal(("A", "claude-x", 3, 20), (typical.Area, typical.Model, typical.Runs, (int)typical.Median));
        var first = source.Current.Snapshot.Version;

        File.AppendAllText(csv, Done("2026-11-22T13:00:00", "4-A-d", 60) + "\n");
        Assert.True(source.Refresh());
        var next = Assert.Single(source.Current.Snapshot.Typicals);
        Assert.Equal(4, next.Runs);
        Assert.NotEqual(first, source.Current.Snapshot.Version);
    }

    [Fact]
    public void VW_19_Typicals_LeaveOutRunsThatDidNotFinishTheCard()
    {
        var runs = new[] { "done", "review", "stopped", "failed" }.Select((r, i) => new Scheduler.Core.Views.ViewRun
        {
            CardId = "5-B", Start = Now.AddHours(-i - 1), End = Now.AddHours(-i).AddMinutes(-30), Result = r, Model = "m", WorkerType = "t", Slot = "1",
        });

        var typicals = ViewsSource.TypicalsOf(runs.Concat(runs).Concat(runs), Now);

        Assert.Equal(6, Assert.Single(typicals).Runs);
    }
}
