// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// 1063-B (state-cache.md MM-9, MM-14): once the repository's model of main is loaded, CFG-3's re-read of main's
/// scheduler.json comes from the model after its gate's fetch, with no git process; with no model it is as before.
/// </summary>
public sealed class ConfigModelTests
{
    [Fact]
    public void Config_ModelLoaded_MainsCopyFromTheModel_NoGitFetchOrShow()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 1 }"); // the stale checkout copy
        var feed = FakeFeed.Over(new Dictionary<string, string> { ["docs/jobs/scheduler.json"] = "{ \"slots\": 3 }" });
        var asked = new List<string>();
        var loader = new ConfigLoader(fx.Git, fx.RemoteRepo, fx.Log, fx.LocalAppData, (r, remote, branch) =>
        {
            asked.Add($"{remote}/{branch}");
            return feed;
        });

        var result = loader.Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Equal(3, result.Config!.Slots);
        Assert.True(result.FromRemote);
        Assert.Equal(["origin/main"], asked);
        Assert.Empty(fx.Fetches);   // the gate fetched, not the CLI reader
        Assert.Empty(fx.Reads);
        Assert.Equal(1, feed.Fetches);
    }

    [Fact]
    public void Config_NoModel_ReadsThroughGitAsBefore()
    {
        using var fx = new ConfigFixture();
        var repo = fx.InitRepo("repo");
        fx.Write(repo, "docs/jobs/scheduler.json", "{ \"slots\": 1 }");
        var loader = new ConfigLoader(fx.Git, fx.RemoteRepo, fx.Log, fx.LocalAppData, (_, _, _) => (IMainFeed?)null);

        var result = loader.Load(CommandLine.Parse(["-Repo", repo]));

        Assert.True(result.Ok);
        Assert.Equal(1, result.Config!.Slots);
        Assert.Single(fx.Fetches);
    }
}
