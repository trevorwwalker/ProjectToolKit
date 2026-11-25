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
/// 1190-B (request #020, MM-13, CM-12): the move requests and claims waiting together are one commit each, in the order
/// they were written, and one push. A hook on the temporary bare remote counts the pushes that reach it, and can reject
/// the first.
/// </summary>
[ParallelMethods]
public class CardRequestStepBatchTests
{
    private const string Jobs = "jobs";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    private static readonly string[] Active = ["010-A-a", "011-A-b", "012-A-c"];

    private static readonly string[] Queued = ["013-A-d", "014-A-e"];

    private static string Body(string name, string status) =>
        $"# {name[..5]}: x\n\nLane: A\nStatus: {status}\n\n## Task\n\nDo it.\n\n## Questions\n\n## Result\n\n";

    private static (GitFixture Fx, CardRequestStep Step) Setup(bool rejectFirst = false)
    {
        var fx = new GitFixture();
        var files = new Dictionary<string, string?> { ["other.txt"] = "0\n" };
        foreach (var n in Active)
        {
            files[$"{Jobs}/active/{n}.md"] = Body(n, "ready");
        }

        foreach (var n in Queued)
        {
            files[$"{Jobs}/queue/{n}.md"] = Body(n, "ready");
        }

        fx.Push("seed", files);
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var hooks = Path.Combine(fx.Bare, "hooks");
        Directory.CreateDirectory(hooks);
        var script = "#!/bin/sh\necho x >> \"$GIT_DIR/pushes.log\"\n"
            + (rejectFirst ? "if [ -f \"$GIT_DIR/reject-once\" ]; then rm \"$GIT_DIR/reject-once\"; exit 1; fi\n" : "")
            + "exit 0\n";
        File.WriteAllText(Path.Combine(hooks, "pre-receive"), script);
        if (rejectFirst)
        {
            File.WriteAllText(Path.Combine(fx.Bare, "reject-once"), "");
        }

        var root = Path.GetDirectoryName(fx.Repo)!;
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, Path.Combine(root, "slots", "answers"), "origin", "main") { MainModel = () => null };
        var step = new CardRequestStep(committer, CardRequests.Folder(Path.Combine(root, "logs")), Jobs, new FixedClock(Now), null);
        return (fx, step);
    }

    private static int Pushes(GitFixture fx)
    {
        var log = Path.Combine(fx.Bare, "pushes.log");
        return File.Exists(log) ? File.ReadAllLines(log).Length : 0;
    }

    private static string[] Copies(GitFixture fx) =>
        [.. GitFixture.Git(fx.Bare, "ls-tree", "-r", "--name-only", "main", Jobs).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    private static string[] Subjects(GitFixture fx, int count) =>
        [.. GitFixture.Git(fx.Bare, "log", "--reverse", $"-{count}", "--format=%s", "main").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static Card QueuedCard(string name) => CardFiles.Parse(Jobs, $"{Jobs}/queue/{name}.md", Body(name, "ready"))!;

    /// <summary>A worker's finish of <paramref name="name"/> to done/, written <paramref name="minute"/> minutes on.</summary>
    private static void Finish(CardRequestStep step, string name, int minute, int slot) =>
        step.Write(CardRequests.FileName(Now.AddMinutes(minute), slot), $"Card: {name}\nTo: done\nSlot: {slot}\n---\n" + Body(name, "done"));

    /// <summary>The tick of request #020: three cards finished, then two claimed, all waiting together.</summary>
    private static void FiveWaiting(CardRequestStep step)
    {
        Finish(step, Active[0], 1, 1);
        Finish(step, Active[1], 2, 2);
        Finish(step, Active[2], 3, 3);
        step.Write(CardRequests.FileName(Now.AddMinutes(4), 1), CardRequests.ClaimText(Queued[0], 1));
        step.Write(CardRequests.FileName(Now.AddMinutes(5), 2), CardRequests.ClaimText(Queued[1], 2));
    }

    // Request #020: a tick that finishes three cards and claims two was five pushes; it is one, five commits in order.
    [Fact]
    public void ThreeFinishesTwoClaims_OnePush_FiveCommitsInOrder()
    {
        var (fx, step) = Setup();
        using (fx)
        {
            FiveWaiting(step);
            Assert.Equal(5, step.Run());

            Assert.Equal(1, Pushes(fx));
            Assert.Equal(
                ["jobs: done 010-A-a", "jobs: done 011-A-b", "jobs: done 012-A-c", "jobs: start 013-A-d", "jobs: start 014-A-e"],
                Subjects(fx, 5));
            Assert.Equal(
                [.. Queued.Select(n => $"{Jobs}/active/{n}.md"), .. Active.Select(n => $"{Jobs}/done/{n}.md")],
                Copies(fx).Order(StringComparer.Ordinal));
            Assert.Empty(step.Pending());
            Assert.Equal(5, Directory.GetFiles(Path.Combine(step.Folder, "done")).Length);
        }
    }

    // 1066-B's per-item outcome: a request whose card is not on main fails alone; the others go in the same push.
    [Fact]
    public void OneBadRequest_FailsAlone_OthersOnePush()
    {
        var (fx, step) = Setup();
        using (fx)
        {
            var filed = new List<(string Card, bool Applied)>();
            step.Filed = (r, ok) => filed.Add((r.Card, ok));
            Finish(step, Active[0], 1, 1);
            Finish(step, "099-A-gone", 2, 2);
            Finish(step, Active[1], 3, 3);
            Assert.Equal(2, step.Run());

            Assert.Equal(1, Pushes(fx));
            Assert.Equal(["jobs: done 010-A-a", "jobs: done 011-A-b"], Subjects(fx, 2));
            Assert.Equal([(Active[0], true), ("099-A-gone", false), (Active[1], true)], filed);
            Assert.Single(Directory.GetFiles(Path.Combine(step.Folder, "failed"), "*" + CardRequests.Extension));
            Assert.Empty(step.Pending());
        }
    }

    // MM-13: a rejected push redoes the batch one by one from the new main; nothing is dropped and the order holds.
    [Fact]
    public void RejectedPush_RedoneOneByOne_NothingDropped()
    {
        var (fx, step) = Setup(rejectFirst: true);
        using (fx)
        {
            FiveWaiting(step);
            Assert.Equal(5, step.Run());

            Assert.Equal(6, Pushes(fx)); // the batch (rejected), then one push each
            Assert.Equal(
                ["jobs: done 010-A-a", "jobs: done 011-A-b", "jobs: done 012-A-c", "jobs: start 013-A-d", "jobs: start 014-A-e"],
                Subjects(fx, 5));
            Assert.Empty(step.Pending());
        }
    }

    // CM-12: a card's second request waits for the next batch, so its claim lands before its finish.
    [Fact]
    public void SameCardTwice_NextBatch_OrderKept()
    {
        var (fx, step) = Setup();
        using (fx)
        {
            step.WriteClaim(QueuedCard(Queued[0]), 1);
            Finish(step, Active[0], 1, 2);
            Finish(step, Queued[0], 2, 1);
            Assert.Equal(3, step.Run());

            Assert.Equal(2, Pushes(fx));
            Assert.Equal(["jobs: start 013-A-d", "jobs: done 010-A-a", "jobs: done 013-A-d"], Subjects(fx, 3));
            Assert.Contains($"{Jobs}/done/{Queued[0]}.md", Copies(fx));
        }
    }

    // 995-B: the tick's claims written together are applied in one push, and each says it landed.
    [Fact]
    public void TwoClaims_WrittenTogether_OnePush_BothLanded()
    {
        var (fx, step) = Setup();
        using (fx)
        {
            var first = step.WriteClaim(QueuedCard(Queued[0]), 1);
            var second = step.WriteClaim(QueuedCard(Queued[1]), 2);
            Assert.Equal(Scheduler.Core.Tick.ClaimState.Landed, step.Apply(first));
            Assert.Equal(Scheduler.Core.Tick.ClaimState.Landed, step.StateOf(second));
            Assert.Equal(Scheduler.Core.Tick.ClaimState.Landed, step.Apply(second));

            Assert.Equal(1, Pushes(fx));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
