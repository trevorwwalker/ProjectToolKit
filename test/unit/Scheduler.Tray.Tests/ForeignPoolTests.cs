// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Tray;
using Xunit;

namespace Scheduler.Tray.Tests;

/// <summary>
/// 1264-B (INS-22): a tray attaches to, or stops, only a scheduler run from its own kit folder. A pool or repository
/// run by another folder's scheduler gets a message and nothing else. Events are named by a test pool file under the
/// test folder and the running processes are an injected list, never the real ones.
/// </summary>
public sealed class ForeignPoolTests : IDisposable
{
    private readonly string _dir = Scheduler.TestSupport.TestTmp.NewPath("fpl");
    private readonly string _kitA;
    private readonly string _kitB;

    public ForeignPoolTests()
    {
        Directory.CreateDirectory(_dir);
        _kitA = Path.Combine(_dir, "KitA");
        _kitB = Path.Combine(_dir, "KitB");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string ExeOf(string kit) => Path.Combine(kit, "scheduler-next.exe");

    [Fact]
    public void PoolRunFromAnotherFolder_SaysWhichFolder_AndNothingIsAttachedOrSet()
    {
        var poolFile = Path.Combine(_dir, "pool.json");
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(poolFile));
        var args = TrayArguments.Parse(["-Pool", poolFile]);
        IReadOnlyList<string> processes = [ExeOf(_kitB)];

        var message = TrayApp.ForeignSchedulerMessage(args, _dir, _kitA, AttachedScheduler.EventExists, processes);

        Assert.NotNull(message);
        Assert.StartsWith($"This pool is run by the Switchyard in {_kitB}.", message, StringComparison.Ordinal);
        Assert.Contains("icon", message, StringComparison.Ordinal);
        Assert.DoesNotContain(_kitA + ".", message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(AttachedScheduler.TryAttachPool(poolFile, ExeOf(_kitA), new LogBuffer(), _ => processes));
        Assert.False(running.WaitOne(0));
    }

    [Fact]
    public void RepositoryRunFromAnotherFolder_SaysWhichFolder_AndNothingIsAttachedOrSet()
    {
        var repo = Path.Combine(_dir, "Game");
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(repo));
        var args = TrayArguments.Parse([repo]);
        IReadOnlyList<string> processes = [ExeOf(_kitB)];

        var message = TrayApp.ForeignSchedulerMessage(args, _dir, _kitA, AttachedScheduler.EventExists, processes);

        Assert.NotNull(message);
        Assert.StartsWith($"This project is run by the Switchyard in {_kitB}.", message, StringComparison.Ordinal);
        Assert.Null(AttachedScheduler.TryAttach(repo, ExeOf(_kitA), new LogBuffer(), _ => processes));
        Assert.False(running.WaitOne(0));
    }

    [Fact]
    public async Task OwnFolderSchedulerRunning_IsAttachedAsToday_AndExitLeavingWorkersSetsTheEvent()
    {
        var poolFile = Path.Combine(_dir, "pool.json");
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(poolFile));
        var args = TrayArguments.Parse(["-Pool", poolFile]);
        IReadOnlyList<string> processes = [ExeOf(_kitB), ExeOf(_kitA)];

        Assert.Null(TrayApp.ForeignSchedulerMessage(args, _dir, _kitA, AttachedScheduler.EventExists, processes));
        using var attached = AttachedScheduler.TryAttachPool(poolFile, ExeOf(_kitA), new LogBuffer(), _ => processes);

        Assert.NotNull(attached);
        Assert.True(await attached!.StopAsync(TimeSpan.FromSeconds(1)));
        Assert.True(running.WaitOne(0));
    }

    [Fact]
    public void Folders_AreComparedIgnoringCaseAndATrailingSlash()
    {
        var poolFile = Path.Combine(_dir, "pool.json");
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(poolFile));
        var args = TrayArguments.Parse(["-Pool", poolFile]);
        IReadOnlyList<string> processes = [ExeOf(_kitA.ToUpperInvariant())];

        Assert.True(AttachedScheduler.SameFolder(_kitA + "\\", _kitA.ToLowerInvariant()));
        Assert.True(AttachedScheduler.SameFolder(_kitA + "/", _kitA.ToUpperInvariant()));
        Assert.False(AttachedScheduler.SameFolder(_kitA, _kitB));
        Assert.Null(TrayApp.ForeignSchedulerMessage(args, _dir, _kitA.ToLowerInvariant() + "\\", AttachedScheduler.EventExists, processes));
        using var attached = AttachedScheduler.TryAttachPool(poolFile, ExeOf(_kitA.ToLowerInvariant() + "\\"), new LogBuffer(), _ => processes);
        Assert.NotNull(attached);
    }

    // The event is there but no scheduler-next.exe is visible from any folder (it is ending, or cannot be read):
    // not this tray's to attach to or stop, and no other folder to name, so the tray starts as it does when the event
    // has just gone; the scheduler it starts waits for the pool's mutex and never ends the other (1262-B, point 3).
    [Fact]
    public void EventWithNoVisibleScheduler_IsNotAttached_AndNoMessage()
    {
        var poolFile = Path.Combine(_dir, "pool.json");
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(poolFile));
        var args = TrayArguments.Parse(["-Pool", poolFile]);

        Assert.Null(TrayApp.ForeignSchedulerMessage(args, _dir, _kitA, AttachedScheduler.EventExists, []));
        Assert.Null(AttachedScheduler.TryAttachPool(poolFile, ExeOf(_kitA), new LogBuffer(), _ => []));
        Assert.False(running.WaitOne(0));
    }

    [Fact]
    public void NoEvent_NoMessage_EvenWithAnotherFolderRunning()
    {
        var args = TrayArguments.Parse(["-Pool", Path.Combine(_dir, "pool.json")]);

        Assert.Null(TrayApp.ForeignSchedulerMessage(args, _dir, _kitA, AttachedScheduler.EventExists, [ExeOf(_kitB)]));
    }
}
