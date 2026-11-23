using Scheduler.Core.Config;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// CFG-2 and CFG-6 as card 400-B changes them: the queue's home is <c>jobs\</c>, with
/// <c>docs\jobs\</c> as the fallback, and the default <c>JobsDir</c> is the folder the config was found in.
/// </summary>
public sealed class ConfigJobsDirTests
{
    // CFG-2 / CFG-6, card 400-B: jobs\scheduler.json is used, and JobsDir defaults to jobs.
    [Fact]
    public void Config_FoundInJobs_UsedAndJobsDirDefaultsToJobs()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "jobs/scheduler.json", "{ \"slots\": 5 }");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Equal(Path.Combine(fx.Top(repo), "jobs", "scheduler.json"), result.ConfigPath);
        Assert.Equal(5, result.Config!.Slots);
        Assert.Equal("jobs", result.Config.JobsDir);
    }

    // CFG-2 / CFG-6, card 400-B: with only docs\jobs\scheduler.json, that is used and JobsDir defaults to docs/jobs.
    [Fact]
    public void Config_OnlyInDocsJobs_UsedAndJobsDirDefaultsToDocsJobs()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 6 }");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Equal(Path.Combine(fx.Top(repo), "docs", "jobs", "scheduler.json"), result.ConfigPath);
        Assert.Equal(6, result.Config!.Slots);
        Assert.Equal("docs/jobs", result.Config.JobsDir);
    }

    // CFG-2, card 400-B: both exist, jobs\ wins and the log says docs\jobs\scheduler.json is ignored.
    [Fact]
    public void Config_InBoth_JobsWinsAndDocsJobsIgnoredIsLogged()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "jobs/scheduler.json", "{ \"slots\": 5 }");
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 6 }");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Equal(Path.Combine(fx.Top(repo), "jobs", "scheduler.json"), result.ConfigPath);
        Assert.Equal(5, result.Config!.Slots);
        Assert.Equal("jobs", result.Config.JobsDir);
        Assert.Contains(result.LogLines, l => l.Contains("docs", StringComparison.Ordinal) && l.Contains("is ignored", StringComparison.Ordinal));
    }

    // CFG-2, card 400-B: neither file is fatal, and the message names both paths.
    [Fact]
    public void Config_InNeither_FatalNamingBothPaths()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.False(result.Ok);
        Assert.Contains(Path.Combine("jobs", "scheduler.json"), result.Error, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("docs", "jobs", "scheduler.json"), result.Error, StringComparison.Ordinal);
    }
}
