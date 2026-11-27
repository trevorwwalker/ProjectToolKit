// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// Card 1492-B: one answer to "is this branch's work on main", for CM-23 (a worker's finish), CM-24 (a move to done/)
/// and the wip sweep: landed when the branch is an ancestor of main, or no commit of it is left to apply by patch
/// equivalence (a rebase gave the commits new ids), or every file it changed is on main (1049-B, a squash). A landed
/// branch is deleted on the remote when its card reaches done/ or review/; a branch with work main lacks keeps the card
/// waiting on the planner, naming at most three of its commits.
/// </summary>
[ParallelMethods]
public class PatchEquivalentWipTests
{
    private const string Jobs = "jobs";
    private const string Name = "010-A-x";
    private const string Branch = "wip/010-A";
    private const string Sha = "[0-9a-f]{7,}";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);

    private static string Body(string status) =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n## Questions\n\n## Result\n\n";

    private static (GitFixture Fx, CardRequestStep Step, List<string> Log) Setup()
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/active/{Name}.md"] = Body("ready"), ["f.txt"] = "1\n" });
        foreach (var dir in new[] { fx.Repo, fx.Seed })
        {
            GitFixture.Git(dir, "config", "user.name", "scheduler");
            GitFixture.Git(dir, "config", "user.email", "s@s");
            GitFixture.Git(dir, "config", "commit.gpgsign", "false");
            GitFixture.Git(dir, "config", "core.autocrlf", "false");
        }

        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var root = Path.GetDirectoryName(fx.Repo)!;
        var log = new List<string>();
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, Path.Combine(root, "slots", "answers"), "origin", "main", new ListLog(log));
        return (fx, new CardRequestStep(committer, CardRequests.Folder(Path.Combine(root, "logs")), Jobs, new FixedClock(Now), new ListLog(log)), log);
    }

    // The worker's commits, each a new file, on a side branch of Seed pushed as wip/010-A; Seed goes back to main.
    private static void PushWip(GitFixture fx, int commits)
    {
        GitFixture.Git(fx.Seed, "checkout", "-q", "-b", "work");
        for (var i = 1; i <= commits; i++)
        {
            File.WriteAllText(Path.Combine(fx.Seed, $"code{i}.txt"), $"{i}\n");
            GitFixture.Git(fx.Seed, "add", "-A");
            GitFixture.Git(fx.Seed, "commit", "-q", "-m", $"the card's code {i}");
        }

        GitFixture.Git(fx.Seed, "push", "-q", "origin", $"HEAD:{Branch}");
        GitFixture.Git(fx.Seed, "checkout", "-q", "main");
    }

    private static void PushMain(GitFixture fx) => GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:main");

    // The worker rebased onto a main that moved on and pushed: main has the same patches under new ids.
    private static void Rebased(GitFixture fx)
    {
        PushWip(fx, 2);
        fx.Push("another card's work", new Dictionary<string, string?> { ["other.txt"] = "o\n" });
        GitFixture.Git(fx.Seed, "cherry-pick", "work~1", "work");
        PushMain(fx);
    }

    private static void Ancestor(GitFixture fx)
    {
        PushWip(fx, 1);
        GitFixture.Git(fx.Seed, "merge", "-q", "--ff-only", "work");
        PushMain(fx);
    }

    // Two commits on the branch reach main as one: no patch is equivalent, but every file is main's (1049-B's rule).
    private static void Squashed(GitFixture fx)
    {
        PushWip(fx, 2);
        GitFixture.Git(fx.Seed, "merge", "-q", "--squash", "work");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "squashed");
        PushMain(fx);
    }

    private static void Unmerged(GitFixture fx, int commits)
    {
        PushWip(fx, commits);
        fx.Push("another card's work", new Dictionary<string, string?> { ["other.txt"] = "o\n" });
    }

    private static bool BranchExists(GitFixture fx) => GitFixture.Git(fx.Bare, "branch", "--list", Branch).Length > 0;

    private static string Remote(GitFixture fx, string path) => GitFixture.Git(fx.Bare, "show", "main:" + path).Replace("\r\n", "\n");

    private static string? Reason(GitFixture fx, GitRunner? git = null) =>
        new UnpushedWork(git ?? new GitRunner(), "origin", "main").Reason(fx.Seed, Name, DateTimeOffset.Now.AddMinutes(-10));

    private static void Finish(CardRequestStep step, string to) =>
        step.Write(CardRequests.FileName(Now.AddMinutes(1), 2), $"Card: {Name}\nTo: {to}\nSlot: 2\n---\n" + Body(to == "done" ? "done" : "review: look"));

    // 1492-B: a branch rebased onto main is landed: CM-23 finds nothing, the card goes where it asked, and its branch
    // is deleted on the remote with the log line.
    [Theory]
    [InlineData("done")]
    [InlineData("review")]
    public void RebasedBranch_IsLanded_CardSettled_BranchDeleted(string to)
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            Rebased(fx);
            Assert.NotEqual("", GitFixture.Git(fx.Seed, "rev-list", $"origin/main..origin/{Branch}").Trim()); // not an ancestor
            var reason = Reason(fx);
            Assert.Equal("", reason);

            Finish(step, to);
            Assert.Equal(1, step.Run());

            Assert.Contains($"Status: {(to == "done" ? "done" : "review: look")}", Remote(fx, $"{Jobs}/{to}/{Name}.md"));
            Assert.False(BranchExists(fx));
            Assert.Contains(log, l => l.Contains("deleted wip/010-A on origin: its work is on main", StringComparison.Ordinal));
        }
    }

    // 1492-B: an ancestor branch is landed.
    [Fact]
    public void AncestorBranch_IsLanded()
    {
        var (fx, _, _) = Setup();
        using (fx)
        {
            Ancestor(fx);
            Assert.Equal("", Reason(fx));
            Assert.Equal("", new UnpushedWork(new GitRunner(), "origin", "main").Wip(fx.Seed, Name));
        }
    }

    // 1492-B: one commit main lacks keeps today's move to queue/, and the Status and the log name it.
    [Fact]
    public void OneUnmergedCommit_CardWaitsOnPlanner_NamingIt()
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            Unmerged(fx, 1);
            var reason = Reason(fx);
            Assert.Matches($"^finished, but its work is not on main: wip/010-A: {Sha} the card's code 1$", reason);

            Finish(step, "done");
            Assert.Equal(1, step.NotSettled(Name, reason!));
            Assert.Equal(1, step.Run());

            var card = Remote(fx, $"{Jobs}/queue/{Name}.md");
            Assert.Matches($@"Status: waiting: planner \(finished, but its work is not on main: wip/010-A: {Sha} the card's code 1\)", card);
            Assert.Contains(log, l => System.Text.RegularExpressions.Regex.IsMatch(l, $"not settled \\(CM-23\\): finished, but its work is not on main: wip/010-A: {Sha} the card's code 1;"));
            Assert.True(BranchExists(fx));
        }
    }

    // 1492-B: four commits main lacks: three are named, then "and 1 more".
    [Fact]
    public void FourUnmergedCommits_NameThreeAndOneMore()
    {
        var (fx, _, _) = Setup();
        using (fx)
        {
            Unmerged(fx, 4);
            Assert.Matches(
                $"^finished, but its work is not on main: wip/010-A: {Sha} the card's code 1; {Sha} the card's code 2; {Sha} the card's code 3; and 1 more$",
                Reason(fx));
        }
    }

    // 1492-B, the card's question for the developer: changes that reached main by patch and were then reverted there
    // are landed by patch equivalence (their commits are in main's history), though 1049-B's file rule alone kept
    // such a branch. Under the developer's rule ("landed when git cherry shows no commit left") the branch goes.
    [Fact]
    public void RevertedOnMain_IsLandedByPatchEquivalence()
    {
        var (fx, _, _) = Setup();
        using (fx)
        {
            PushWip(fx, 1);
            GitFixture.Git(fx.Seed, "cherry-pick", "work");
            GitFixture.Git(fx.Seed, "revert", "--no-edit", "HEAD");
            PushMain(fx);
            Assert.Equal("", Reason(fx));
            Assert.Equal(["code1.txt"], new WipBranches(new GitRunner(), "origin", "main").NotOnMain(fx.Seed, $"origin/{Branch}", "origin/main"));
        }
    }

    [Fact]
    public void Named_ListsAtMostThree()
    {
        Assert.Equal("wip/1-A", UnpushedWork.Named("wip/1-A", []));
        Assert.Equal("wip/1-A: a x; b y", UnpushedWork.Named("wip/1-A", ["a x", "b y"]));
        Assert.Equal("wip/1-A: a; b; c; and 2 more", UnpushedWork.Named("wip/1-A", ["a", "b", "c", "d", "e"]));
    }

    // 1492-B: CM-23, CM-24 and the sweep give the same answer for each fixture (the sweep runs last: it deletes).
    [Theory]
    [InlineData("rebased", true)]
    [InlineData("ancestor", true)]
    [InlineData("squashed", true)]
    [InlineData("one", false)]
    [InlineData("four", false)]
    public void Cm23Cm24AndSweep_Agree(string fixture, bool landed)
    {
        var (fx, _, _) = Setup();
        using (fx)
        {
            switch (fixture)
            {
                case "rebased": Rebased(fx); break;
                case "ancestor": Ancestor(fx); break;
                case "squashed": Squashed(fx); break;
                case "one": Unmerged(fx, 1); break;
                default: Unmerged(fx, 4); break;
            }

            Assert.Equal(landed, Reason(fx) == "");
            Assert.Equal(landed, new UnpushedWork(new GitRunner(), "origin", "main").Wip(fx.Seed, Name) == "");

            var sweepLog = new List<string>();
            var repo = new SweepRepo("p", fx.Seed, "origin", "main", () => [Name], new ListLog(sweepLog));
            new WipSweep(new FixedClock(Now)).Sweep([repo], Now);
            Assert.Equal(landed, !BranchExists(fx));
            Assert.Contains(sweepLog, l => l.Contains(landed ? "deleted wip/010-A on origin: its work is on main" : "wip/010-A kept: holds work not on main", StringComparison.Ordinal));
        }
    }

    // 1492-B cost: with no finish waiting the tick's guard (HasFinish) reads only the requests folder; CM-23's check of
    // a run whose commits are all on main by ancestry costs one git process.
    [Fact]
    public void NoFinishWaiting_NoGit_AndLandedByAncestryCostsOne()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            Assert.False(step.HasFinish(Name));
            Ancestor(fx);
            var git = new GitRunner();
            Assert.Equal("", Reason(fx, git));
            Assert.Equal(1, git.Started);
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
