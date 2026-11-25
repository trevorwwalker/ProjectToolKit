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
/// Card 1300-B (CM-24; the developer: "cards that haven't pushed to main can't be done"): 1273-D and 1284-D went to
/// done/ from the page while their only commit was on <c>wip/&lt;card&gt;</c>. A move to done/ from the page (an
/// answer whose choice ends <c>-&gt; done</c>, the Done button) or from a move request, while a wip branch of the card
/// holds work main lacks, sends the card to queue/ to wait on the planner; a planner batch to done/ is applied and
/// logged. The Seed clone pushes the wip branch and main.
/// </summary>
[ParallelMethods]
public class AnswersCommitterUnlandedTests
{
    private const string Jobs = "jobs";
    private const string Name = "010-A-x";

    private static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    private static string Body(string status) =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n" +
        "## Questions\n\n- Q1: Accept the fix? [yes -> done | no]\n\n## Result\n\n- 2026-11-24 12:00: parked\n";

    private static (GitFixture Fx, AnswersCommitter Committer, List<string> Log) Setup(string folder, string status)
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/{folder}/{Name}.md"] = Body(status), ["code.txt"] = "0\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var log = new List<string>();
        var answers = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main", new ListLog(log)) { Now = () => Now };
        return (fx, committer, log);
    }

    // The card's fix, pushed to wip/010-A only; the Seed clone is put back on main.
    private static void PushWip(GitFixture fx, string branch = "wip/010-A")
    {
        GitFixture.Git(fx.Seed, "pull", "-q", "origin", "main");
        File.WriteAllText(Path.Combine(fx.Seed, "code.txt"), "1\n");
        GitFixture.Git(fx.Seed, "add", "code.txt");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "the card's fix");
        GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:" + branch);
        GitFixture.Git(fx.Seed, "reset", "-q", "--hard", "origin/main");
    }

    private static Card Parse(string folder) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folder}/{Name}.md", Body("x")) ?? throw new InvalidOperationException("not a card path");

    private static string[] Copies(GitFixture fx) =>
        [.. GitFixture.Git(fx.Bare, "ls-tree", "-r", "--name-only", "main", Jobs).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    private static string Remote(GitFixture fx, string path) => GitFixture.Git(fx.Bare, "show", "main:" + path).Replace("\r\n", "\n");

    private const string Waiting = "Status: waiting: planner (finished, but its work is not on main: wip/010-A)";

    // CM-24 (1273-D, 1284-D): the developer picks "yes -> done" on a parked card whose fix is on wip/010-A only. The
    // card goes to queue/ waiting on the planner, the answer is still written after the question, the Result says
    // who asked for done and what is not on main, and the log says the same in one line.
    [Fact]
    public void AnswerToDone_WithWipNotOnMain_WaitsOnPlannerInQueue_CM24()
    {
        var (fx, committer, log) = Setup("active", "needs-human: accept?");
        using (fx)
        {
            PushWip(fx);
            var card = Parse("active");
            CardEdit? made = null;
            var result = committer.UpdateCard(card.Path, main => CardMoves.Answer(card, main, 1, 0, "", Now), false, e => made = e);

            Assert.True(result.Ok, result.Error);
            Assert.Equal([$"{Jobs}/queue/{Name}.md"], Copies(fx));
            var text = Remote(fx, $"{Jobs}/queue/{Name}.md");
            Assert.Contains(Waiting, text);
            Assert.Contains("-> **yes** (developer, 2026-11-24", text);
            Assert.Contains("- 2026-11-24 15:00 scheduler: an answer from the page asked for done/, but its work is not on main: wip/010-A; it waits on the planner (CM-24).", text);
            Assert.Equal(CardFolder.Queue, made!.Folder);
            Assert.Equal($"{Jobs}/queue/{Name}.md", made.NewPath);
            Assert.Single(log, l => l.Contains("010-A-x: an answer from the page asked for done/, but its work is not on main: wip/010-A; it goes to queue/", StringComparison.Ordinal));
            Assert.StartsWith("jobs: wait 010-A-x", GitFixture.Git(fx.Bare, "log", "-1", "--format=%s", "main"), StringComparison.Ordinal);
        }
    }

    // CM-24: the Done button on a review card whose work is on a wip/<card>-* branch only does the same.
    [Fact]
    public void DoneButtonOnReview_WithWipNotOnMain_WaitsOnPlannerInQueue_CM24()
    {
        var (fx, committer, _) = Setup("review", "review: check it");
        using (fx)
        {
            PushWip(fx, "wip/010-A-retry");
            var card = Parse("review");
            var result = committer.UpdateCard(card.Path, main => CardMoves.Done(card, main, Now), false);

            Assert.True(result.Ok, result.Error);
            Assert.Equal([$"{Jobs}/queue/{Name}.md"], Copies(fx));
            var text = Remote(fx, $"{Jobs}/queue/{Name}.md");
            Assert.Contains("Status: waiting: planner (finished, but its work is not on main: wip/010-A-retry)", text);
            Assert.Contains("scheduler: the Done button asked for done/, but its work is not on main: wip/010-A-retry", text);
            Assert.Contains("developer: reviewed, done (from the page).", text);
        }
    }

    // CM-24, rule 4: everything on main - no wip branch, or a wip branch whose work reached main rebased (another
    // commit, the same files) - is settled done from the answer and from the Done button as before.
    [Fact]
    public void EverythingOnMain_AnswerAndDoneSettledDone_CM24()
    {
        var (fx, committer, _) = Setup("active", "needs-human: accept?");
        using (fx)
        {
            var card = Parse("active");
            Assert.True(committer.UpdateCard(card.Path, main => CardMoves.Answer(card, main, 1, 0, "", Now), false).Ok);
            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
            Assert.Contains("Status: done", Remote(fx, $"{Jobs}/done/{Name}.md"));
        }

        (fx, committer, _) = Setup("review", "review: check it");
        using (fx)
        {
            PushWip(fx);
            fx.Push("the fix, rebased onto main", new Dictionary<string, string?> { ["code.txt"] = "1\n" });
            GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
            Assert.Equal("", new UnpushedWork(new GitRunner(), "origin", "main").Wip(fx.Repo, Name));
            var card = Parse("review");
            Assert.True(committer.UpdateCard(card.Path, main => CardMoves.Done(card, main, Now), false).Ok);
            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
        }
    }

    // CM-24: an answer that does not move the card to done/ (here "no", which leaves it where it is) never asks git
    // about wip branches and is applied as now, though a wip branch holds work.
    [Fact]
    public void AnswerNotToDone_Untouched_CM24()
    {
        var (fx, committer, _) = Setup("active", "needs-human: accept?");
        using (fx)
        {
            PushWip(fx);
            var card = Parse("active");
            Assert.True(committer.UpdateCard(card.Path, main => CardMoves.Answer(card, main, 1, 1, "", Now), false).Ok);
            Assert.Equal([$"{Jobs}/active/{Name}.md"], Copies(fx));
            Assert.DoesNotContain("waiting: planner", Remote(fx, $"{Jobs}/active/{Name}.md"));
        }
    }

    // CM-24, rule 3: a planner batch that moves the card to done/ is the planner's evaluation and is applied, and the
    // log says in one line that it went with work not on main.
    [Fact]
    public void PlannerBatchToDone_AppliedAndLogged_CM24()
    {
        var (fx, committer, log) = Setup("active", "needs-human: accept?");
        using (fx)
        {
            PushWip(fx);
            var result = committer.Apply(new JobsChange
            {
                Moves = [new FileMove { From = $"{Jobs}/active/{Name}.md", To = $"{Jobs}/done/{Name}.md" }],
                Message = "planner: 010-A closed, its fix is not wanted",
            });

            Assert.True(result.Ok, result.Error);
            Assert.Equal([$"{Jobs}/done/{Name}.md"], Copies(fx));
            Assert.Single(log, l => l == "planner: 010-A-x moved to done with work not on main: wip/010-A");
            Assert.Contains("wip/010-A holds work not on main: code.txt", Remote(fx, $"{Jobs}/done/{Name}.md"));
        }
    }

    private sealed class ListLog(List<string> lines) : ISchedulerLog
    {
        public void Write(string line) => lines.Add(line);
    }
}
