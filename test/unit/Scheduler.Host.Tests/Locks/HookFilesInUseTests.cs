using Scheduler.Core.Contracts;
using Scheduler.Host.Locks;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1077-B: "git hooks could not be written ... Access to the path is denied". Windows refuses to replace a file
/// a running <c>sh</c> has open (no delete share), so the hooks are left alone when current, retried when
/// changed, and the log says what state they are in when the write fails; the earlier hooks stay in place.
/// </summary>
public sealed class HookFilesInUseTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Lines _log = new();

    private string Hooks => Path.Combine(_dir.Path, "hooks");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_current_hook_that_a_running_git_has_open_does_not_fail_the_write()
    {
        Assert.True(HookFiles.Write(Hooks, "C:/x/scheduler-next.exe", _log));
        using var open = new FileStream(Path.Combine(Hooks, HookFiles.PrePush), FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.True(HookFiles.Write(Hooks, "C:/x/scheduler-next.exe", _log, 1, TimeSpan.Zero));
        Assert.Contains("pre-push current", _log.Last, StringComparison.Ordinal);
    }

    [Fact]
    public void A_changed_hook_in_use_keeps_the_earlier_one_and_the_log_says_so()
    {
        Assert.True(HookFiles.Write(Hooks, "C:/x/scheduler-next.exe", _log));
        var before = File.ReadAllText(Path.Combine(Hooks, HookFiles.PrePush));
        using var open = new FileStream(Path.Combine(Hooks, HookFiles.PrePush), FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(HookFiles.Write(Hooks, "C:/y/scheduler-next.exe", _log, 2, TimeSpan.FromMilliseconds(10)));

        Assert.Equal(before, File.ReadAllText(Path.Combine(Hooks, HookFiles.PrePush)));   // locks still enforced by the earlier hook
        Assert.Contains("pre-push older version kept, still enforcing locks", _log.Last, StringComparison.Ordinal);
        Assert.Contains("commit-msg current", _log.Last, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Hooks, HookFiles.PrePush + ".tmp")));
    }

    [Fact]
    public void A_changed_hook_is_written_once_the_running_git_lets_go()
    {
        Assert.True(HookFiles.Write(Hooks, "C:/x/scheduler-next.exe", _log));
        var open = new FileStream(Path.Combine(Hooks, HookFiles.PrePush), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() => { Thread.Sleep(300); open.Dispose(); }) { IsBackground = true };
        release.Start();

        Assert.True(HookFiles.Write(Hooks, "C:/y/scheduler-next.exe", _log, 300, TimeSpan.FromMilliseconds(100)));
        release.Join();

        Assert.Contains("C:/y/scheduler-next.exe", File.ReadAllText(Path.Combine(Hooks, HookFiles.PrePush)), StringComparison.Ordinal);
    }

    private sealed class Lines : ISchedulerLog
    {
        public string Last { get; private set; } = "";

        public void Write(string line) => Last = line;
    }
}
