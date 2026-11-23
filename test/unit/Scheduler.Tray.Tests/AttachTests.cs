using System.Text;
using Scheduler.Tray;
using Xunit;

namespace Scheduler.Tray.Tests;

/// <summary>975-B: a tray started next to a running scheduler attaches to it and starts nothing.</summary>
public sealed class AttachTests : IDisposable
{
    private readonly string _dir = Scheduler.TestSupport.TestTmp.NewPath("att");

    public AttachTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void NoSchedulerRunning_NothingToAttachTo()
    {
        Assert.Null(AttachedScheduler.TryAttach(_dir, Path.Combine(_dir, "scheduler-next.exe"), new LogBuffer()));
    }

    [Fact]
    public async Task ASchedulerRunning_IsAttachedTo_AndExitSetsItsShutdownRequest()
    {
        // What a running scheduler holds for its repository (Scheduler.Host's RepoShutdown, the same name).
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(_dir));
        var log = new LogBuffer();
        using var attached = AttachedScheduler.TryAttach(_dir + "\\", Path.Combine(_dir, "scheduler-next.exe"), log);

        Assert.NotNull(attached);
        Assert.Null(attached!.Pid); // no such process: not known, and nothing was started
        Assert.True(await attached.StopAsync(TimeSpan.FromSeconds(1)));
        Assert.True(running.WaitOne(0));
        Assert.Contains(log.Snapshot().Lines, l => l.Contains("asking the attached scheduler", StringComparison.Ordinal));
    }

    [Fact]
    public void LogTail_FindsTheStartLine_AndFollowsNewLinesAndANewDay()
    {
        var logs = Path.Combine(_dir, "jobs", ".logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "scheduler-20261118.log"), "10:00:00 scheduler-next 3.0.19 started for Game: 4 slot(s), page http://localhost:8766/\n10:00:01 tick\n");
        var today = Path.Combine(logs, "scheduler-20261119.log");
        File.WriteAllText(today, "00:00:01 one\n00:00:02 two\n");
        var tail = new SchedulerLogTail(SchedulerLogTail.FolderFor(_dir));

        var (start, recent) = tail.Open(1);

        Assert.True(StartLine.TryParse(start, out var project, out var page));
        Assert.Equal("Game", project);
        Assert.Equal(new Uri("http://localhost:8766/"), page);
        Assert.Equal(new[] { "00:00:02 two" }, recent);
        Assert.Empty(tail.ReadNew());

        File.AppendAllText(today, "00:00:03 three\n00:00:04 hal");
        Assert.Equal(new[] { "00:00:03 three" }, tail.ReadNew());
        File.AppendAllText(today, "f\n");
        Assert.Equal(new[] { "00:00:04 half" }, tail.ReadNew());

        File.WriteAllText(Path.Combine(logs, "scheduler-20261120.log"), "00:00:05 next day\n", Encoding.UTF8);
        Assert.Equal(new[] { "00:00:05 next day" }, tail.ReadNew());
    }
}
