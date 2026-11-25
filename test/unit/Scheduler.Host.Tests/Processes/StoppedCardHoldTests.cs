// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Processes;

/// <summary>A card whose worker was stopped or killed from the page waits on you (WT-P2, Q4). Test plan: worker-types.md.</summary>
[ParallelMethods]
public class StoppedCardHoldTests
{
    private const string Jobs = "docs/jobs";

    private const string Path = Jobs + "/active/880-B-x.md";

    private static string CardText(string status) =>
        "# 880-B: x\n\nLane: B\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n## Questions\n\n## Result\n\n- started\n";

    private static (GitFixture Fx, StoppedCardHold Hold, Card Card) Setup(string status)
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [Path] = CardText(status), ["other.txt"] = "0\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var answers = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main");
        var card = CardFiles.Parse(Jobs, Path, CardText(status))!;
        return (fx, new StoppedCardHold(committer, new FakeClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero)), new ListLog()), card);
    }

    private static string Remote(GitFixture fx) => GitFixture.Git(fx.Bare, "show", "main:" + Path).Replace("\r\n", "\n");

    // WT-P2: only a worker stopped or killed from the page holds its card.
    [Fact]
    public void Page_StopFromPage_OnlyPageStopsHold()
    {
        RunningWorker W(string why) => new() { Slot = 1, Pid = 1, Start = DateTimeOffset.Now, Card = "880-B-x", Category = "B", Id = "880-B", StopWhy = why };

        Assert.True(StoppedCardHold.Applies(W("stopped from the page")));
        Assert.True(StoppedCardHold.Applies(W("killed from the page")));
        Assert.False(StoppedCardHold.Applies(W("stop all from the page")));
        Assert.False(StoppedCardHold.Applies(W("over the 90 min limit")));
        Assert.False(StoppedCardHold.Applies(W("")));
    }

    // WT-P2 (Q4): the card stays in active/ with a Status the page lists under Waiting on you, and
    // the page's Resume (MOVE-7) accepts it.
    [Fact]
    public void Page_StopFromPage_CardWaitsOnYou()
    {
        var (fx, hold, card) = Setup("ready");
        using (fx)
        {
            Assert.True(hold.Hold(card));

            var text = Remote(fx);
            Assert.Contains("Status: " + StoppedCardHold.Status, text, StringComparison.Ordinal);
            var held = CardFiles.Parse(Jobs, Path, text)!;
            Assert.True(CardWaits.OnDeveloper(held));
            Assert.Equal(CardFolder.Active, held.Folder);
            Assert.True(Scheduler.Core.Moves.CardMoves.Resume(held, text, DateTimeOffset.UnixEpoch).Ok);
        }
    }

    // A worker that finished before the stop took effect (its Status moved on) keeps what it set.
    [Theory]
    [InlineData("done")]
    [InlineData("review: check it")]
    [InlineData("needs-human: which?")]
    public void Page_StopFromPage_FinishedCardKeepsItsStatus(string status)
    {
        var (fx, hold, card) = Setup(status);
        using (fx)
        {
            Assert.False(hold.Hold(card));

            Assert.Contains("Status: " + status, Remote(fx), StringComparison.Ordinal);
        }
    }
}
