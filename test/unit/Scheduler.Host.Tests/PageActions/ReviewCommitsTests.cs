using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.PageActions;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// REV-1: the review panel lists the commits on the remote's branch whose message contains the card's
/// id, from the card's last "jobs: start" commit on; with no such commit, all of them.
/// docs/design/tdd/scheduler/spec.md (Review panel). 650-C could not add these (docs/known_gaps.md, 650-C (e)).
/// </summary>
[ParallelMethods]
public class ReviewCommitsTests
{
    private const string Card = "730-B";

    // The panel over the fixture's clone, built the way HostPageData builds it.
    private static ReviewCommits Panel(GitFixture fx)
    {
        fx.Main().Fetch();
        return new ReviewCommits(new GitRunner(), fx.Repo, "origin/main");
    }

    // A commit with a file of its own, so every commit in the fixture has a diff to list.
    private static void Commit(GitFixture fx, string message) =>
        fx.Push(message, new Dictionary<string, string?> { ["notes/" + Guid.NewGuid().ToString("N") + ".md"] = "note\n" });

    private static List<string> Subjects(IReadOnlyList<ReviewCommit> commits) => [.. commits.Select(c => c.Subject)];

    // REV-1: the card's commits oldest first, from the claim on; what named the card before the claim is left out.
    [Fact]
    public void ListsTheCardCommitsFromTheJobsStartCommitOldestFirst()
    {
        using var fx = new GitFixture();
        Commit(fx, "docs: plan 730-B for the review panel");
        Commit(fx, "jobs: start 730-B-review-panel-tests");
        Commit(fx, "feat: 730-B reads the commit list");
        Commit(fx, "jobs: done 730-B-review-panel-tests");

        Assert.Equal(
            ["jobs: start 730-B-review-panel-tests", "feat: 730-B reads the commit list", "jobs: done 730-B-review-panel-tests"],
            Subjects(Panel(fx).For(Card)));
    }

    // REV-1: with no claim commit, every commit naming the card is listed.
    [Fact]
    public void WithNoJobsStartListsEveryCommitNamingTheCard()
    {
        using var fx = new GitFixture();
        Commit(fx, "docs: plan 730-B");
        Commit(fx, "feat: the 730-B panel");

        Assert.Equal(["docs: plan 730-B", "feat: the 730-B panel"], Subjects(Panel(fx).For(Card)));
    }

    // REV-1: another card's commits are left out, even between this card's own.
    [Fact]
    public void LeavesOutTheCommitsOfOtherCards()
    {
        using var fx = new GitFixture();
        Commit(fx, "jobs: start 730-B-review-panel-tests");
        Commit(fx, "jobs: done 650-C-review-panel");
        Commit(fx, "test: 730-B covers the list");

        Assert.Equal(["jobs: start 730-B-review-panel-tests", "test: 730-B covers the list"], Subjects(Panel(fx).For(Card)));
    }

    // REV-1: a card sent back and started again lists only what came since the second claim.
    [Fact]
    public void WithTwoJobsStartListsFromTheLastOne()
    {
        using var fx = new GitFixture();
        Commit(fx, "jobs: start 730-B-review-panel-tests");
        Commit(fx, "feat: 730-B first attempt");
        Commit(fx, "jobs: review 730-B-review-panel-tests");
        Commit(fx, "jobs: start 730-B-review-panel-tests");
        Commit(fx, "test: 730-B second attempt");

        Assert.Equal(["jobs: start 730-B-review-panel-tests", "test: 730-B second attempt"], Subjects(Panel(fx).For(Card)));
    }

    // REV-1: the claim is a commit whose subject begins with "jobs: start"; a note that only quotes it is not one.
    [Fact]
    public void OnlyACommitBeginningJobsStartCutsTheList()
    {
        using var fx = new GitFixture();
        Commit(fx, "docs: plan 730-B");
        Commit(fx, "docs: 730-B note quoting jobs: start in its subject");

        Assert.Equal(2, Panel(fx).For(Card).Count);
    }

    // REV-1: the whole message is matched, so a commit naming the card only in its body is listed by its first line.
    [Fact]
    public void ListsACommitThatNamesTheCardInTheBodyOnly()
    {
        using var fx = new GitFixture();
        Commit(fx, "jobs: start 730-B-review-panel-tests");
        Commit(fx, "docs: journal line\n\nThe Result of this commit names the list for 730-B.");

        Assert.Equal(["jobs: start 730-B-review-panel-tests", "docs: journal line"], Subjects(Panel(fx).For(Card)));
    }

    // REV-1: each commit carries the files it changed, with their added and removed line counts.
    [Fact]
    public void ListsEachFilesAddedAndRemovedLines()
    {
        using var fx = new GitFixture();
        fx.Push("jobs: start 730-B-review-panel-tests", new Dictionary<string, string?>
        {
            ["code/Panel.cs"] = "a\nb\nc\n",
            ["code/Other.cs"] = "one\n",
        });
        fx.Push("feat: 730-B edits one line", new Dictionary<string, string?> { ["code/Panel.cs"] = "a\nx\nc\n" });

        var commits = Panel(fx).For(Card);
        var claim = commits.Single(c => c.Subject == "jobs: start 730-B-review-panel-tests");
        Assert.Contains(new ReviewFile("code/Panel.cs", 3, 0), claim.Files);
        Assert.Contains(new ReviewFile("code/Other.cs", 1, 0), claim.Files);

        var edit = commits.Single(c => c.Subject == "feat: 730-B edits one line");
        Assert.Equal([new ReviewFile("code/Panel.cs", 1, 1)], edit.Files);
    }

    // REV-1: only the remote's branch is read: a commit that reaches another branch of the remote is not listed.
    [Fact]
    public void LeavesOutCommitsThatOnlyReachAnotherBranch()
    {
        using var fx = new GitFixture();
        Commit(fx, "jobs: start 730-B-review-panel-tests");
        File.WriteAllText(Path.Combine(fx.Seed, "notes", "wip.md"), "wip\n");
        GitFixture.Git(fx.Seed, "add", "-A");
        GitFixture.Git(fx.Seed, "commit", "-q", "-m", "feat: 730-B work that never landed");
        GitFixture.Git(fx.Seed, "push", "-q", "origin", "HEAD:refs/heads/wip/730-B");
        GitFixture.Git(fx.Seed, "reset", "-q", "--hard", "origin/main");

        Assert.Equal(["jobs: start 730-B-review-panel-tests"], Subjects(Panel(fx).For(Card)));
    }

    // REV-1: a card with no commits yet gives no commits, and so does a remote branch that is not there.
    [Fact]
    public void GivesNoCommitsWhenNothingNamesTheCard()
    {
        using var fx = new GitFixture();
        Commit(fx, "jobs: start 650-C-review-panel");

        Assert.Empty(Subjects(Panel(fx).For(Card)));
        Assert.Empty(new ReviewCommits(new GitRunner(), fx.Repo, "origin/no-such-branch").For(Card));
    }

    // An empty card id would match every commit in the repository; the panel asks for none of them.
    [Fact]
    public void AnEmptyCardIdGivesNoCommits()
    {
        using var fx = new GitFixture();
        Commit(fx, "jobs: start 730-B-review-panel-tests");

        Assert.Empty(Panel(fx).For(""));
    }
}
