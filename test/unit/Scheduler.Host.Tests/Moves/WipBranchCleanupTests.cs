using Scheduler.Core.Contracts;
using Scheduler.Core.Moves;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// Card 1049-B: when a card moves to done/, its wip/ branch on the remote is deleted if its work is on main
/// (equal to main's, or main changed the file later), kept and reported if not, and left alone for any other card.
/// </summary>
[ParallelMethods]
public class WipBranchCleanupTests
{
    private const string Jobs = "jobs";
    private const string Name = "010-A-x";
    private const string Branch = "wip/010-A";

    private static readonly DateTimeOffset Now = new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);

    private static string Body(string status) =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\nDo it.\n\n## Questions\n\n## Result\n\n";

    private static (GitFixture Fx, CardRequestStep Step, List<string> Log) Setup()
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/active/{Name}.md"] = Body("ready"), ["f.txt"] = "1\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var root = Path.GetDirectoryName(fx.Repo)!;
        var log = new List<string>();
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, Path.Combine(root, "slots", "answers"), "origin", "main", new ListLog(log));
        return (fx, new CardRequestStep(committer, CardRequests.Folder(Path.Combine(root, "logs")), Jobs, new FixedClock(Now), new ListLog(log)), log);
    }

    /// <summary>A worker's unmerged branch: <paramref name="files"/> committed on top of main and pushed as <paramref name="branch"/>.</summary>
    private static void PushBranch(GitFixture fx, string branch, IDictionary<string, string> files)
    {
        GitFixture.Git(fx.Seed, "checkout", "-q", "-b", "tmp-" + branch.Replace('/', '-'));
        foreach (var (path, text) in files)
        {
            File.WriteAllText(Path.Combine(fx.Seed, path), text);
        }

        GitFixture.Git(fx.Seed, "add", "-A");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "wip");
        GitFixture.Git(fx.Seed, "push", "-q", "origin", $"HEAD:{branch}");
        GitFixture.Git(fx.Seed, "checkout", "-q", "main");
    }

    private static bool BranchExists(GitFixture fx, string branch) =>
        GitFixture.Git(fx.Bare, "branch", "--list", branch).Length > 0;

    private static void Finish(CardRequestStep step, string to)
    {
        step.Write(CardRequests.FileName(Now.AddMinutes(1), 4), $"Card: {Name}\r\nTo: {to}\r\nSlot: 4\r\n---\r\n" + Body(to == "done" ? "done" : "review: look").Replace("\n", "\r\n"));
        Assert.Equal(1, step.Run());
    }

    // 1049-B: a branch whose files equal main's is deleted when its card is done.
    [Fact]
    public void BranchEqualToMain_IsDeletedAtDone()
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            PushBranch(fx, Branch, new Dictionary<string, string> { ["f.txt"] = "2\n" });
            fx.Push("landed", new Dictionary<string, string?> { ["f.txt"] = "2\n" });
            Finish(step, "done");
            Assert.False(BranchExists(fx, Branch));
            Assert.Contains(log, l => l.Contains("deleted wip/010-A", StringComparison.Ordinal));
        }
    }

    // 1049-B: a branch whose file main changed afterwards (superseded) is deleted.
    [Fact]
    public void BranchSupersededOnMain_IsDeletedAtDone()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            PushBranch(fx, Branch, new Dictionary<string, string> { ["f.txt"] = "2\n" });
            fx.Push("later", new Dictionary<string, string?> { ["f.txt"] = "3\n" });
            Finish(step, "done");
            Assert.False(BranchExists(fx, Branch));
        }
    }

    // 1049-B: a branch with a file main never got is kept and the card's Result says so.
    [Fact]
    public void BranchWithWorkNotOnMain_IsKeptAndReported()
    {
        var (fx, step, log) = Setup();
        using (fx)
        {
            PushBranch(fx, Branch, new Dictionary<string, string> { ["new.txt"] = "x\n", ["f.txt"] = "1\n" });
            Finish(step, "done");
            Assert.True(BranchExists(fx, Branch));
            var card = GitFixture.Git(fx.Bare, "show", $"main:{Jobs}/done/{Name}.md");
            Assert.Contains("wip/010-A holds work not on main: new.txt", card);
            Assert.Contains(log, l => l.Contains("wip/010-A kept", StringComparison.Ordinal));
        }
    }

    // 1049-B: a card that goes to review keeps its branch.
    [Fact]
    public void BranchOfCardInReview_IsUntouched()
    {
        var (fx, step, _) = Setup();
        using (fx)
        {
            PushBranch(fx, Branch, new Dictionary<string, string> { ["f.txt"] = "2\n" });
            fx.Push("landed", new Dictionary<string, string?> { ["f.txt"] = "2\n" });
            Finish(step, "review");
            Assert.True(BranchExists(fx, Branch));
        }
    }

    // 1049-B: only wip/<number>-<letter> branches are ever deleted.
    [Theory]
    [InlineData("010-A-x", "wip/010-A")]
    [InlineData("1049-B-wip-branch-cleanup", "wip/1049-B")]
    [InlineData("notes", "")]
    [InlineData("x-1049-B", "")]
    public void BranchOf_NamesOnlyNumberLetterBranches(string card, string expected) =>
        Assert.Equal(expected, WipBranches.BranchOf(card));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class ListLog(List<string> lines) : ISchedulerLog
    {
        public void Write(string line) => lines.Add(line);
    }
}
