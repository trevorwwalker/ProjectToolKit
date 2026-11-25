// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// Card 660-B (roadmap item 2): card moves asked for with request files and committed only by the
/// scheduler, in order, through the answers worktree. A temporary bare remote, the Seed clone as
/// a worker pushing to main, and the Repo clone as the developer's checkout.
/// </summary>
[ParallelMethods]
public class CardRequestStepTests
{
    private const string Jobs = "jobs";
    private const string Name = "010-A-x";

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 2, 0, 0, TimeSpan.Zero);

    private static string Body(string status, string result = "") =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n## Questions\n\n## Result\n\n" + result;

    private static (GitFixture Fx, CardRequestStep Step, List<string> Log) Setup(string folder, string text)
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/{folder}/{Name}.md"] = text, ["other.txt"] = "0\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var root = Path.GetDirectoryName(fx.Repo)!;
        var log = new List<string>();
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, Path.Combine(root, "slots", "answers"), "origin", "main", new ListLog(log));
        var step = new CardRequestStep(committer, CardRequests.Folder(Path.Combine(root, "logs")), Jobs, new FixedClock(Now), new ListLog(log));
        return (fx, step, log);
    }

    /// <summary>Every copy of the card on the remote's main.</summary>
    private static string[] Copies(GitFixture fx) =>
        [.. GitFixture.Git(fx.Bare, "ls-tree", "-r", "--name-only", "main", Jobs).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    private static string Remote(GitFixture fx, string path) => GitFixture.Git(fx.Bare, "show", "main:" + path).Replace("\r\n", "\n");

    private static Card QueuedCard(string text) => CardFiles.Parse(Jobs, $"{Jobs}/queue/{Name}.md", text)!;

    // 660-B: the scheduler's claim at launch and the worker's finish, both through requests, leave
    // exactly one copy of the card, in review/, with the worker's text; each request is filed in done\.
    [Fact]
    public void ClaimAndFinish_ThroughRequests_OneCopyWithWorkersText()
    {
        var text = Body("ready");
        var (fx, step, log) = Setup("queue", text);
        using (fx)
        {
            Assert.True(step.Claim(QueuedCard(text), 4));
            Assert.Equal([$"{Jobs}/active/{Name}.md"], Copies(fx));
            Assert.Contains("Claimed: 2026-11-19, lane A", Remote(fx, $"{Jobs}/active/{Name}.md"));

            // The worker journals to the card where it is, then hands its finished card over.
            var worker = Remote(fx, $"{Jobs}/active/{Name}.md")
                .Replace("Status: ready", "Status: review: try it on the PC") + "- 2026-11-19 02:30: all done, gates green\n";
            step.Write(CardRequests.FileName(Now.AddMinutes(30), 4), $"Card: {Name}\r\nTo: review\r\nSlot: 4\r\n---\r\n" + worker.Replace("\n", "\r\n"));
            Assert.Equal(1, step.Run());

            Assert.Equal([$"{Jobs}/review/{Name}.md"], Copies(fx));
            var final = Remote(fx, $"{Jobs}/review/{Name}.md");
            Assert.Contains("Status: review: try it on the PC", final);
            Assert.Contains("- 2026-11-19 02:30: all done, gates green", final);
            Assert.Contains("Claimed: 2026-11-19, lane A", final);
            Assert.Equal("jobs: review 010-A-x", GitFixture.Git(fx.Bare, "log", "-1", "--format=%s", "main").Trim());
            Assert.Empty(step.Pending());
            Assert.Equal(2, Directory.GetFiles(Path.Combine(step.Folder, "done")).Length);
            Assert.Contains(log, l => l.Contains("card move from slot 4: 010-A-x active/ -> review/", StringComparison.Ordinal));
        }
    }

    // 1020-B (DL-8, DL-9): the lock releases are told of each worker's move once it is filed - applied on
    // main or failed - and never of a claim; a move still waiting is pending for its card.
    [Fact]
    public void Filed_WorkerMovesAppliedOrFailed_NotClaims_DL8_DL9()
    {
        var text = Body("ready");
        var (fx, step, _) = Setup("queue", text);
        using (fx)
        {
            var filed = new List<(string Card, CardFolder To, bool Applied)>();
            step.Filed = (r, ok) => filed.Add((r.Card, r.To, ok));
            Assert.True(step.Claim(QueuedCard(text), 4));
            Assert.Empty(filed);

            var worker = Remote(fx, $"{Jobs}/active/{Name}.md").Replace("Status: ready", "Status: done");
            step.Write(CardRequests.FileName(Now.AddMinutes(30), 4), $"Card: {Name}\nTo: done\nSlot: 4\n---\n" + worker);
            Assert.True(step.HasPending(Name));
            Assert.False(step.HasPending("011-A-y"));
            Assert.Equal(1, step.Run());
            Assert.False(step.HasPending(Name));
            Assert.Equal([(Name, CardFolder.Done, true)], filed);

            step.Write(CardRequests.FileName(Now.AddMinutes(31), 3), "Card: 099-A-gone\nTo: done\nSlot: 3\n---\n" + worker);
            step.Run();
            Assert.Equal(("099-A-gone", CardFolder.Done, false), filed[^1]);
        }
    }

    // 995-B: a claim written off the tick says where it went: landed when its card is in active/ on
    // main, failed when the card is not there to claim; applying it again changes nothing.
    [Fact]
    public void WriteClaimAndApply_SaysLandedOrFailed()
    {
        var text = Body("ready");
        var (fx, step, _) = Setup("queue", text);
        using (fx)
        {
            var card = QueuedCard(text);
            var landed = step.WriteClaim(card, 2);
            Assert.Equal(Scheduler.Core.Tick.ClaimState.Landed, step.Apply(landed));
            Assert.Equal([$"{Jobs}/active/{Name}.md"], Copies(fx));
            Assert.Equal(Scheduler.Core.Tick.ClaimState.Landed, step.Apply(landed));

            var gone = step.WriteClaim(card with { Name = "011-A-y" }, 3);
            Assert.Equal(Scheduler.Core.Tick.ClaimState.Failed, step.Apply(gone));
            Assert.Equal([$"{Jobs}/active/{Name}.md"], Copies(fx));
        }
    }

    // 660-B: conflicting requests applied in order - two claims of one card and a finish of a card
    // that has already moved on leave one copy; the losers go to failed\ with their reason.
    [Fact]
    public void ConflictingRequests_InOrder_LosersFailed()
    {
        var text = Body("ready");
        var (fx, step, log) = Setup("queue", text);
        using (fx)
        {
            step.Write(CardRequests.FileName(Now, 2), CardRequests.ClaimText(Name, 2));
            step.Write(CardRequests.FileName(Now.AddSeconds(1), 3), CardRequests.ClaimText(Name, 3));
            step.Write(CardRequests.FileName(Now.AddSeconds(2), 2), $"Card: {Name}\nTo: done\n---\n" + Body("done", "- 1\n"));
            step.Write(CardRequests.FileName(Now.AddSeconds(3), 3), $"Card: {Name}\nTo: review\n---\n" + Body("review", "- 2\n"));

            Assert.Equal(2, step.Run());

            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
            Assert.Contains("- 1", Remote(fx, $"{Jobs}/done/{Name}.md"));
            var failed = Path.Combine(step.Folder, "failed");
            Assert.Contains("already claimed", File.ReadAllText(Path.Combine(failed, "20261119-020001-000-slot3.reason.txt")));
            Assert.Contains("is in done/, not active/", File.ReadAllText(Path.Combine(failed, "20261119-020003-000-slot3.reason.txt")));
            Assert.Equal(2, log.Count(l => l.Contains("failed:", StringComparison.Ordinal)));
        }
    }

    // 660-B: a worker that still moved the card itself - its request finds the card already there
    // and writes only its text; asked again with the same text, nothing is committed.
    [Fact]
    public void WorkerMovedItself_RequestUpdatesInPlace()
    {
        var text = Body("review", "- moved by hand\n");
        var (fx, step, _) = Setup("review", text);
        using (fx)
        {
            var head = GitFixture.Git(fx.Bare, "rev-parse", "main");
            step.Write(CardRequests.FileName(Now, 1), $"Card: {Name}\nTo: review\n---\n" + text);
            Assert.Equal(0, step.Run());
            Assert.Equal(head, GitFixture.Git(fx.Bare, "rev-parse", "main"));
            Assert.Single(Directory.GetFiles(Path.Combine(step.Folder, "done")));

            step.Write(CardRequests.FileName(Now.AddSeconds(1), 1), $"Card: {Name}\nTo: review\n---\n" + text + "- and its Result\n");
            Assert.Equal(1, step.Run());
            Assert.Equal([$"{Jobs}/review/{Name}.md"], Copies(fx));
            Assert.Contains("- and its Result", Remote(fx, $"{Jobs}/review/{Name}.md"));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class ListLog(List<string> lines) : ISchedulerLog
    {
        public void Write(string line) => lines.Add(line);
    }
}
