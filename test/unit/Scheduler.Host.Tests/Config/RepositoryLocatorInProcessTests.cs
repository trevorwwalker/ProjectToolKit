using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// 1065-B, state-cache.md MM-14: the repository a folder belongs to is found in process - the same top level as
/// <c>git rev-parse --show-toplevel</c>, spelt as on disk however the folder was written - with no git started;
/// inside a git folder it is still git's answer (none).
/// </summary>
public sealed class RepositoryLocatorInProcessTests : IDisposable
{
    private readonly GitFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public void MM_14_TopLevel_InProcessMatchesGit_AndStartsNoGit()
    {
        var sub = Path.Combine(_fx.Seed, "Some", "Deeper");
        Directory.CreateDirectory(sub);
        var git = new GitRunner();
        var locator = new RepositoryLocator(git);
        var byGit = GitFixture.Git(sub, "rev-parse", "--show-toplevel").Replace('/', '\\');

        Assert.Equal(byGit, locator.TopLevel(sub));
        Assert.Equal(byGit, locator.TopLevel(sub.ToLowerInvariant()));
        Assert.Equal(byGit, locator.TopLevel(_fx.Seed + "\\"));
        Assert.Equal(0, git.Started);
    }

    [Fact]
    public void MM_14_InsideTheGitFolder_IsLeftToGit_WhichKnowsNone()
    {
        var hooks = Path.Combine(_fx.Seed, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        Assert.Null(RepositoryLocator.InProcess(hooks));
        Assert.Null(new RepositoryLocator(new GitRunner()).TopLevel(hooks));
        Assert.Null(RepositoryLocator.InProcess(_fx.Bare));
    }
}
