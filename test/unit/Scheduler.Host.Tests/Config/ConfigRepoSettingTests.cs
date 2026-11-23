using Scheduler.Core.Config;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// CFG-4: <c>cfg.repo</c> replaces the repository only when none was named on the command line,
/// and the settings that depend on the repository follow it.
/// </summary>
public sealed class ConfigRepoSettingTests
{
    // CFG-4: cfg.repo is the repository when -Repo was not given; -Repo still wins over it.
    [Fact]
    public void Config_CfgRepo_UsedOnlyWhenNoRepoOnCommandLine()
    {
        using var fx = new ConfigFixture();
        var repoA = fx.InitRepo("repoA");
        var repoB = fx.InitRepo("repoB");
        var config = Path.Combine(fx.Root, "cfg", "scheduler.json");
        fx.Write(fx.Root, "cfg/scheduler.json", "{ \"repo\": \"" + repoB.Replace("\\", "\\\\") + "\" }");

        // (a) -Config and no -Repo: the config names the repository.
        var byConfig = fx.Loader().Load(CommandLine.Parse(["-Config", config]));
        Assert.True(byConfig.Ok);
        Assert.Equal(repoB, byConfig.Repo);
        Assert.Equal(repoB, byConfig.Config!.Repo);
        Assert.Equal("repoB", byConfig.Config.Project); // CFG-6's defaults follow the repository that won

        // (b) -Repo on the command line is the repository, whatever the config says.
        var byCommandLine = fx.Loader().Load(CommandLine.Parse(["-Config", config, "-Repo", repoA]));
        Assert.True(byCommandLine.Ok);
        Assert.Equal(fx.Top(repoA), byCommandLine.Repo);
        Assert.Equal(fx.Top(repoA), byCommandLine.Config!.Repo);
        Assert.Equal("repoA", byCommandLine.Config.Project);
    }
}
