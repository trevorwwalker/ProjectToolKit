using Scheduler.Core.Config;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// CFG-3: without <c>-Config</c> the config is re-read from
/// <c>&lt;remote&gt;/&lt;branch&gt;:&lt;jobsDir&gt;/scheduler.json</c> after a fetch, with the
/// remote, branch and jobs folder taken from the checkout's own copy; a failure or bad JSON there
/// falls back to the checkout with the reason logged. A <c>-Config</c> file is used as it is.
/// </summary>
public sealed class ConfigRemoteTests
{
    // CFG-3: no -Config, so the remote branch's copy wins and a fetch happened to get it.
    [Fact]
    public void Config_WithoutConfigArg_RereadsFromRemoteMain()
    {
        using var fx = new ConfigFixture();
        var bare = fx.InitBare("remote.git");
        var seed = fx.InitRepo("seed");
        fx.AddRemote(seed, "origin", bare);
        fx.Write(seed, "docs/jobs/scheduler.json", "{ \"slots\": 3 }");
        fx.Commit(seed, "main's config");
        fx.Push(seed, "origin", "main");

        var repo = fx.InitRepo("repo");
        fx.AddRemote(repo, "origin", bare);
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 1 }"); // the stale checkout copy

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Single(fx.Fetches); // origin/main did not exist locally until the fetch made it
        Assert.Equal(3, result.Config!.Slots);
        Assert.True(result.FromRemote);
        Assert.Equal("origin/main", result.RemoteRef);
        Assert.Contains(fx.Log.Lines, l => l.Contains("origin/main", StringComparison.Ordinal));
    }

    // CFG-3: main's copy is broken, or the remote cannot be reached: the checkout's copy is used and why is logged.
    [Fact]
    public void Config_RemoteConfigInvalid_FallsBackToCheckoutWithReason()
    {
        // (a) main's scheduler.json is not valid JSON.
        using var bad = new ConfigFixture();
        var bare = bad.InitBare("remote.git");
        var seed = bad.InitRepo("seed");
        bad.AddRemote(seed, "origin", bare);
        bad.Write(seed, "docs/jobs/scheduler.json", "{ slots: 3, ");
        bad.Commit(seed, "broken config on main");
        bad.Push(seed, "origin", "main");

        var repo = bad.InitRepo("repo");
        bad.AddRemote(repo, "origin", bare);
        bad.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 1 }");

        var broken = bad.Loader().Load(CommandLine.Parse(["-Repo", repo]));
        Assert.True(broken.Ok);
        Assert.Equal(1, broken.Config!.Slots);
        Assert.False(broken.FromRemote);
        Assert.Contains(bad.Log.Lines, l => l.Contains("invalid JSON", StringComparison.Ordinal)
                                         && l.Contains("origin/main", StringComparison.Ordinal));

        // (b) the remote cannot be reached.
        using var gone = new ConfigFixture();
        var repo2 = gone.InitRepo("repo");
        gone.AddRemote(repo2, "origin", Path.Combine(gone.Root, "gone.git"));
        gone.Write(repo2, "docs/jobs/scheduler.json", "{ \"slots\": 2 }");

        var unreachable = gone.Loader().Load(CommandLine.Parse(["-Repo", repo2]));
        Assert.True(unreachable.Ok);
        Assert.Equal(2, unreachable.Config!.Slots);
        Assert.False(unreachable.FromRemote);
        Assert.Contains(gone.Log.Lines, l => l.Contains("fetch", StringComparison.Ordinal)
                                          && l.Contains("origin/main", StringComparison.Ordinal));
    }

    // CFG-3: the checkout's copy names the remote, branch and jobs folder the config is read from.
    [Fact]
    public void Config_RemoteBranchJobsDirFromCheckoutCopy()
    {
        using var fx = new ConfigFixture();
        var bare = fx.InitBare("up.git");
        var seed = fx.InitRepo("seed");
        fx.AddRemote(seed, "up", bare);
        fx.Write(seed, "work/jobs/scheduler.json", "{ \"slots\": 7 }");
        fx.Commit(seed, "the config under work/jobs");
        fx.Push(seed, "up", "trunk");

        var repo = fx.InitRepo("repo");
        fx.AddRemote(repo, "up", bare);
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"remote\": \"up\", \"branch\": \"trunk\", \"jobsDir\": \"work/jobs/\", \"slots\": 1 }");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Contains(fx.Reads, r => r.Remote == "up" && r.Branch == "trunk" && r.Path == "work/jobs/scheduler.json");
        Assert.Equal(7, result.Config!.Slots);
        Assert.True(result.FromRemote);
    }

    // CFG-3: a -Config file is used as it is; main is never fetched or read.
    [Fact]
    public void Config_ExplicitConfigFile_UsedAsIsNoFetch()
    {
        using var fx = new ConfigFixture();
        var bare = fx.InitBare("remote.git");
        var seed = fx.InitRepo("seed");
        fx.AddRemote(seed, "origin", bare);
        fx.Write(seed, "docs/jobs/scheduler.json", "{ \"slots\": 9 }");
        fx.Commit(seed, "main's config");
        fx.Push(seed, "origin", "main");

        var repo = fx.InitRepo("repo");
        fx.AddRemote(repo, "origin", bare);
        var file = Path.Combine(repo, "myconfig.json");
        fx.Write(repo, "myconfig.json", "{ \"slots\": 5 }");

        var result = fx.Loader().Load(CommandLine.Parse(["-Repo", repo, "-Config", file]));

        Assert.True(result.Ok);
        Assert.Equal(5, result.Config!.Slots);
        Assert.True(result.ExplicitConfig);
        Assert.False(result.FromRemote);
        Assert.Equal(file, result.ConfigPath);
        Assert.Empty(fx.Fetches);
        Assert.Empty(fx.Reads);
        Assert.False(fx.HasRemoteBranch(repo, "origin", "main")); // nothing fetched it
    }
}
