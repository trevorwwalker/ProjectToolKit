using Scheduler.Core.Config;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// CFG-1: which folder is the project's repository - <c>-Repo</c> or the first positional
/// argument resolved to its git top level, else the current directory's top level when no
/// <c>-Config</c> was given, else a failure that names the repository.
/// </summary>
public sealed class ConfigRepositoryTests
{
    // CFG-1: -Repo, and the same folder as the first positional argument, both give the top level.
    [Fact]
    public void Config_RepoArgument_ResolvesToGitTopLevel()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 2 }");
        var deep = Path.Combine(repo, "a", "b");
        Directory.CreateDirectory(deep);
        var top = fx.Top(repo);

        var bySwitch = fx.Loader().Load(CommandLine.Parse(["-Repo", deep]));
        var byPosition = fx.Loader().Load(CommandLine.Parse([deep]));

        Assert.True(bySwitch.Ok);
        Assert.Equal(top, bySwitch.Repo);
        Assert.Equal(top, bySwitch.Config!.Repo);
        Assert.True(byPosition.Ok);
        Assert.Equal(top, byPosition.Repo);
    }

    // CFG-1: with neither -Repo nor -Config, the repository is the current directory's top level.
    [Fact]
    public void Config_NoRepoNoConfig_UsesCurrentDirectoryTopLevel()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "docs/jobs/scheduler.json", "{}");
        var inside = Path.Combine(repo, "src");
        Directory.CreateDirectory(inside);

        var result = fx.Loader().Load(CommandLineOptions.None, currentDirectory: inside);

        Assert.True(result.Ok);
        Assert.Equal(fx.Top(repo), result.Repo);
        Assert.Equal(fx.Top(repo), result.Config!.Repo);
    }

    // CFG-1: nothing names a repository, so start-up fails with "Name the project's repository".
    [Fact]
    public void Config_NoRepoAnywhere_FailsWithNameTheRepository()
    {
        using var fx = new ConfigFixture();
        var outside = fx.OutsideFolder("outside"); // in %TEMP%, which is in no repository

        var result = fx.Loader().Load(CommandLineOptions.None, currentDirectory: outside);

        Assert.False(result.Ok);
        Assert.Null(result.Config);
        Assert.Contains("Name the project's repository", result.Error, StringComparison.Ordinal);
        Assert.Empty(fx.Log.Lines);
    }

    // CFG-1: a -Repo that is not in a git repository is an error naming the folder, and creates nothing.
    [Fact]
    public void Config_NonGitRepoArgument_IsError()
    {
        using var fx = new ConfigFixture();
        var plain = fx.OutsideFolder("plain");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", plain]));

        Assert.False(result.Ok);
        Assert.Null(result.Config);
        Assert.Contains(plain, result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(plain, "*", SearchOption.AllDirectories));
    }
}
