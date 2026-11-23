using Scheduler.Host.Observe;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.Tick;
using Xunit;

namespace Scheduler.Host.Tests.Program;

/// <summary>975-B: the repository's shutdown event, which a tray finds to attach to a running scheduler.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RepoShutdownTests
{
    [Fact]
    public void Name_IsTheSameForOneRepositoryHoweverItIsWritten()
    {
        Assert.Equal(RepoShutdown.Name(@"C:\Projects\Game"), RepoShutdown.Name(@"c:\projects\game\"));
        Assert.NotEqual(RepoShutdown.Name(@"C:\Projects\Game"), RepoShutdown.Name(@"C:\Projects\Other"));
        Assert.DoesNotContain('\\', RepoShutdown.Name(@"C:\Projects\Game"));
    }

    [Fact]
    public void ARunningScheduler_HasItsRepositorysEvent_ClearAtStart_AndGoneWhenItEnds()
    {
        using var temp = new TempDir();
        var name = RepoShutdown.Name(temp.Path);
        using (var left = new EventWaitHandle(true, EventResetMode.ManualReset, name))
        {
            // A request left set for the scheduler before is cleared by the next one.
            var composition = Compose(temp.Path, observe: false);
            Assert.True(EventWaitHandle.TryOpenExisting(name, out var found));
            using (found)
            {
                Assert.False(found!.WaitOne(0));
            }

            composition.Dispose();
        }

        Assert.False(EventWaitHandle.TryOpenExisting(name, out _));
    }

    [Fact]
    public void AnObservingRun_IsNotTheRepositorysScheduler()
    {
        using var temp = new TempDir();
        using var composition = Compose(temp.Path, observe: true);
        Assert.False(EventWaitHandle.TryOpenExisting(RepoShutdown.Name(temp.Path), out _));
    }

    private static HostComposition Compose(string repo, bool observe)
    {
        var config = new SchedulerConfigBuilder
        {
            Repo = repo,
            Project = "Repo" + Guid.NewGuid().ToString("N")[..8],
            WorkspaceRoot = Path.Combine(repo, "ws"),
            LogDir = Path.Combine(repo, "logs"),
            WorkerCli = Path.Combine(repo, "no-agent.cmd"),
            Port = TickRig.FreePort(),
        }.Build();
        var (observed, settings) = observe ? ObserveSettings.Apply(config, portGiven: true) : (config, (ObserveSettings?)null);
        return HostComposition.Create(observed, "{}", new SkewClock(), new ListLog(), _ => { }, TimeSpan.FromSeconds(5), settings);
    }
}
