// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// Card 1237-B: 1228-B was settled done while its session still ran and its gated commits were only on
/// <c>wip/1228-B</c>. CM-12: a running worker's request waits for its run to end. CM-23: a finish whose run left
/// commits that main does not have (on the slot's HEAD or a <c>wip/&lt;card&gt;</c> branch) goes to queue/ to wait on the
/// planner (card 1300-B; it was parked as needs-human). CM-24: a move request to done/ with such a wip branch the same.
/// The Seed clone stands for the worker's slot worktree.
/// </summary>
[ParallelMethods]
public class CardRequestStepSettleTests
{
    private const string Jobs = "jobs";
    private const string Name = "010-A-x";

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private static string Body(string status) =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n## Questions\n\n## Result\n\n- 2026-11-24 09:00: all done\n";

    private static (GitFixture Fx, CardRequestStep Step, List<string> Log) Setup()
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/active/{Name}.md"] = Body("ready"), ["code.txt"] = "0\n" });
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

    private static string[] Copies(GitFixture fx) =>
        [.. GitFixture.Git(fx.Bare, "ls-tree", "-r", "--name-only", "main", Jobs).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    private static string Remote(GitFixture fx, string path) => GitFixture.Git(fx.Bare, "show", "main:" + path).Replace("\r\n", "\n");

    private static void Finish(CardRequestStep step, string to) =>
        step.Write(CardRequests.FileName(Now, 2), $"Card: {Name}\nTo: {to}\nSlot: 2\n---\n" + Body(to == "review" ? "review: look at it" : "done"));

    // A commit in the slot (Seed) that is not pushed to main.
    private static void CommitInSlot(GitFixture fx)
    {
        File.WriteAllText(Path.Combine(fx.Seed, "code.txt"), "1\n");
        GitFixture.Git(fx.Seed, "add", "code.txt");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "the card's code");
    }

    private static string? Reason(GitFixture fx) =>
        new UnpushedWork(new GitRunner(), "origin", "main").Reason(fx.Seed, Name, DateTimeOffset.Now.AddMinutes(-10));

    // CM-12 (1228-B): while the worker runs, its To: done waits - main keeps the card in active/ and the request
    // stays; the run of the card ending applies it in that turn.
    [Fact]
    public void RunningWorkersFinish_WaitsUntilItsRunEnds_CM12()
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            step.Running = new HashSet<string>([Name], StringComparer.OrdinalIgnoreCase);
            Finish(step, "done");

            Assert.Equal(0, step.Run());
            Assert.Equal([$"{Jobs}/active/{Name}.md"], Copies(fx));
            Assert.Single(step.Pending());
            Assert.True(step.HasPending(Name));
            Assert.Contains(log, l => l.Contains("waits: 010-A-x's worker is still running (CM-12)", StringComparison.Ordinal));

            Assert.Equal(1, step.Run(Name));
            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
            Assert.Empty(step.Pending());
        }
    }

    // CM-12: another card's running worker does not hold this card's finish, and a running card's claim never waits.
    [Fact]
    public void OtherCardRunning_FinishApplied_CM12()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            step.Running = new HashSet<string>(["020-B-y"], StringComparer.OrdinalIgnoreCase);
            Finish(step, "done");
            Assert.Equal(1, step.Run());
            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
        }
    }

    // CM-23 (1228-B; 1300-B): the run's code is on wip/<card> only; its To: done sends the card to queue/ waiting on
    // the planner, with the reason in words and a Result line saying what was asked for.
    [Fact]
    public void FinishWithCommitsOnWipNotOnMain_WaitsOnPlannerInQueue_CM23()
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            CommitInSlot(fx);
            GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:wip/010-A");
            var reason = Reason(fx);
            Assert.Equal("finished, but its work is not on main: wip/010-A", reason);

            Finish(step, "done");
            Assert.Equal(1, step.NotSettled(Name, reason!));
            Assert.Equal(1, step.Run());

            Assert.Equal([$"{Jobs}/queue/{Name}.md"], Copies(fx));
            var card = Remote(fx, $"{Jobs}/queue/{Name}.md");
            Assert.Contains("Status: waiting: planner (finished, but its work is not on main: wip/010-A)", card);
            Assert.Contains("- 2026-11-24 09:00: all done", card);
            Assert.Contains("- 2026-11-24 09:00 scheduler: the worker's finish asked for done/, but its work is not on main: wip/010-A; it waits on the planner (CM-23).", card);
            Assert.Contains(log, l => l.Contains("asked for done/ but is not settled (CM-23)", StringComparison.Ordinal)
                && l.EndsWith("it goes to queue/ to wait on the planner", StringComparison.Ordinal));
        }
    }

    // CM-23: a review finish with commits that were never pushed anywhere waits on the planner too, naming the slot's worktree.
    [Fact]
    public void ReviewWithLocalCommitOnly_WaitsOnPlannerInQueue_CM23()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            CommitInSlot(fx);
            CommitInSlot2(fx);
            var reason = Reason(fx);
            Assert.Equal($"finished, but its work is not on main: only in the worktree {Path.GetFileName(fx.Seed)}", reason);

            Finish(step, "review");
            Assert.Equal(1, step.NotSettled(Name, reason!));
            Assert.Equal(1, step.Run());
            Assert.Equal([$"{Jobs}/queue/{Name}.md"], Copies(fx));
            var card = Remote(fx, $"{Jobs}/queue/{Name}.md");
            Assert.Contains("Status: waiting: planner (finished, but its work is not on main: only in the worktree", card);
            Assert.Contains("scheduler: the worker's finish asked for review/, but its work is not on main", card);
        }
    }

    // CM-23: a normal finish - the run's commits pushed to main - is settled as before: no reason, the card in done/.
    [Fact]
    public void FinishWithEverythingOnMain_SettledAsBefore_CM23()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            CommitInSlot(fx);
            GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:main");
            Assert.Equal("", Reason(fx));

            Finish(step, "done");
            Assert.False(step.HasPending("020-B-y"));
            Assert.True(step.HasFinish(Name));
            Assert.Equal(1, step.Run());
            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
            Assert.Equal("done", CardStatus(Remote(fx, $"{Jobs}/done/{Name}.md")));
        }
    }

    // CM-23: a wip branch left from before the run started (its work landed another way) does not hold the card,
    // and a run that changed nothing is settled; a park request (To: active) is never rewritten.
    [Fact]
    public void OldWipBranchAndParkRequest_Untouched_CM23()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            CommitInSlot(fx);
            GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:wip/010-A");
            GitFixture.Git(fx.Seed, "reset", "-q", "--hard", "origin/main");
            var reason = new UnpushedWork(new GitRunner(), "origin", "main").Reason(fx.Seed, Name, DateTimeOffset.Now.AddMinutes(10));
            Assert.Equal("", reason);
            Assert.Null(new UnpushedWork(new GitRunner(), "origin", "main").Reason(Path.Combine(fx.Seed, "missing"), Name, Now));

            step.Write(CardRequests.FileName(Now, 2), $"Card: {Name}\nTo: active\nStatus: needs-human: which way?\n");
            Assert.False(step.HasFinish(Name));
            Assert.Equal(0, step.NotSettled(Name, "finished, but its work is not on main: wip/010-A"));
        }
    }

    // CM-24 (1300-B): a move request to done/ that CM-23 did not hold (its wip branch is from an earlier session, so
    // the run made no commit) still waits on the planner while that branch holds work main lacks.
    [Fact]
    public void RequestToDoneWithEarlierWipNotOnMain_WaitsOnPlannerInQueue_CM24()
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            CommitInSlot(fx);
            GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:wip/010-A");
            GitFixture.Git(fx.Seed, "reset", "-q", "--hard", "origin/main");
            Finish(step, "done");
            Assert.Equal(1, step.Run());

            Assert.Equal([$"{Jobs}/queue/{Name}.md"], Copies(fx));
            var card = Remote(fx, $"{Jobs}/queue/{Name}.md");
            Assert.Contains("Status: waiting: planner (finished, but its work is not on main: wip/010-A)", card);
            Assert.Contains("scheduler: a move request asked for done/, but its work is not on main: wip/010-A", card);
            Assert.Contains(log, l => l.Contains("010-A-x: a move request asked for done/", StringComparison.Ordinal));
        }
    }

    private static void CommitInSlot2(GitFixture fx)
    {
        File.WriteAllText(Path.Combine(fx.Seed, "code.txt"), "2\n");
        GitFixture.Git(fx.Seed, "add", "code.txt");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "more of the card's code");
    }

    private static string CardStatus(string text) =>
        text.Split('\n').First(l => l.StartsWith("Status: ", StringComparison.Ordinal))["Status: ".Length..].Trim();

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class ListLog(List<string> lines) : ISchedulerLog
    {
        public void Write(string line) => lines.Add(line);
    }
}
