using Scheduler.Core.Contracts;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 1065-B, state-cache.md MM-14: the health check's main sha is made in process with git's default length - on
/// a fixture repository and on this checkout - and git is asked only for the first of a run, to confirm it.
/// </summary>
public sealed class HealthShortShaTests
{
    [Fact]
    public void MM_14_MainShortSha_InProcessMatchesGit_AndGitIsAskedOnce()
    {
        using var fx = new GitFixture();
        using var temp = new TempDir();
        fx.Push("one", new Dictionary<string, string?> { ["a.txt"] = "one" });
        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
        var git = new GitRunner();
        var runner = Runner(fx.Repo, temp.Path, git);
        var expected = GitFixture.Git(fx.Repo, "rev-parse", "--short", "origin/main");

        Assert.Equal(expected, runner.ShortInProcess());
        Assert.Equal(expected, runner.MainShortSha());
        Assert.Equal(1, git.Started);
        fx.Push("two", new Dictionary<string, string?> { ["a.txt"] = "two" });
        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
        Assert.Equal(GitFixture.Git(fx.Repo, "rev-parse", "--short", "origin/main"), runner.MainShortSha());
        Assert.Equal(1, git.Started);
    }

    [Fact]
    public void MM_14_ShortInProcess_MatchesGit_OnThisCheckout()
    {
        var top = RepositoryLocator.InProcess(AppContext.BaseDirectory);
        if (top is null)
        {
            return; // not run from a checkout the reader can open
        }

        using var temp = new TempDir();
        var mine = Runner(top, temp.Path, new GitRunner()).ShortInProcess();
        if (mine is null)
        {
            return; // a multi-pack index: git is asked, as before
        }

        Assert.Equal(GitFixture.Git(top, "rev-parse", "--short", "origin/main"), mine);
    }

    private static HealthWorktreeRunner Runner(string repo, string temp, GitRunner git) =>
        new(new SchedulerConfig
        {
            Project = "proj",
            Repo = repo,
            WorkspaceRoot = Path.Combine(temp, "ws"),
            LogDir = Path.Combine(temp, "logs"),
            JobsEnvVar = "PTK_JOBS",
            HealthCommand = "exit 0",
            Slots = 2,
        }, git, new TickProcesses(), new FakeClock(DateTimeOffset.Now), new ListLog(), cores: 8);
}
