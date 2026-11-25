// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// CFG-2: the config file is <c>-Config</c> when given, else
/// <c>&lt;repo&gt;\docs\jobs\scheduler.json</c>, and a file that is not there is fatal at start-up.
/// </summary>
public sealed class ConfigFileTests
{
    // CFG-2: with -Repo only, the config is the repository's docs\jobs\scheduler.json.
    [Fact]
    public void Config_DefaultConfigPath_IsRepoDocsJobsSchedulerJson()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 4 }");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Equal(Path.Combine(fx.Top(repo), "docs", "jobs", "scheduler.json"), result.ConfigPath);
        Assert.False(result.ExplicitConfig);
        Assert.Equal(4, result.Config!.Slots);
    }

    // CFG-2: no config file at all - default path or -Config - stops start-up, with nothing run.
    [Fact]
    public void Config_MissingConfigFile_IsFatal()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");

        // The repository has no docs\jobs\scheduler.json.
        var noDefault = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));
        Assert.False(noDefault.Ok);
        Assert.Null(noDefault.Config);
        Assert.Contains("No scheduler config at", noDefault.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("docs", "jobs", "scheduler.json"), noDefault.Error, StringComparison.Ordinal);

        // -Config naming a file that is not there.
        var missing = Path.Combine(fx.Root, "elsewhere", "scheduler.json");
        var given = fx.Loader().Load(CommandLine.Parse(["-Config", missing]), currentDirectory: repo);
        Assert.False(given.Ok);
        Assert.Null(given.Config);
        Assert.Contains(missing, given.Error, StringComparison.Ordinal);

        // CFG-15: start-up failed before anything else could run - no git, so no fetch either.
        Assert.Empty(fx.Fetches);
        Assert.Empty(fx.Log.Lines);
    }
}
