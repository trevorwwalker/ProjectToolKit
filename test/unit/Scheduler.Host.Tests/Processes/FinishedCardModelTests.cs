// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Processes;

/// <summary>
/// 1063-B (state-cache.md MM-9, MM-14): <see cref="FinishedCardCheck"/> takes the blob ids from the repository's
/// model of main and its fetch from the model's gate: the same flags as through git (395-B, 410-A), no git process.
/// </summary>
public class FinishedCardModelTests
{
    private const string Active = "jobs/active/7-B-x.md";
    private const string Text = "# 7-B: x\n\nCategory: B\nStatus: ready\n";

    [Fact]
    public void FinishedWithoutUpdatingTheCard_FlaggedFromTheModel_NoGit()
    {
        var feed = FakeFeed.Over(new Dictionary<string, string> { [Active] = Text });
        var git = new CountingMain();
        var check = new FinishedCardCheck(git, "jobs") { Feed = feed };
        check.Observe("7-B-x");

        // the worker's move is a pure rename: the remote has it, the model sees it after the gate's fetch
        feed.Main.Stage(new Dictionary<string, string?> { [Active] = null, ["jobs/done/7-B-x.md"] = Text });
        feed.Gate.WorkerEnded();

        Assert.Equal(FinishedCardCheck.Note, check.Check("7-B-x", "done"));
        var flag = Assert.Single(check.Flagged);
        Assert.Equal(CardFolder.Done, flag.Folder);
        Assert.Equal(feed.Main.Snapshot!.Files["jobs/done/7-B-x.md"], flag.Stamp);
        Assert.Equal(1, feed.Fetches);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public void CardUpdatedOnTheMove_NotFlagged()
    {
        var feed = FakeFeed.Over(new Dictionary<string, string> { [Active] = Text });
        var check = new FinishedCardCheck(new CountingMain(), "jobs") { Feed = feed };
        check.Observe("7-B-x");

        feed.Main.Stage(new Dictionary<string, string?> { [Active] = null, ["jobs/review/7-B-x.md"] = Text + "\n## Result\n\ndone\n" });

        Assert.Null(check.Check("7-B-x", "review"));
        Assert.Empty(check.Flagged);
    }
}
