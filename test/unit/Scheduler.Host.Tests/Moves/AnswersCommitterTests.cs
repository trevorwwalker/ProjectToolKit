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
/// Card changes from the page committed through the answers worktree (A10: MOVE-1, MOVE-4,
/// MOVE-11; Answers-Tree L1150, Local-Edits L1168, Update-Card L1204). Test plan:
/// scheduler-next/test-plans/claims-and-moves.md. A temporary bare remote, the Seed clone as
/// "another clone" pushing to main, and the Repo clone as the developer's checkout.
/// </summary>
[ParallelMethods]
public class AnswersCommitterTests
{
    private const string Jobs = "docs/jobs";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static string CardBody(string status, string notes = "Notes: as planned", string result = "- 2026-11-18 10:00: started") =>
        "# 010-A: x\n\nLane: A\nStatus: " + status + "\n\n## Task\n\n" + notes + "\n\none\ntwo\nthree\nfour\nfive\n\n" +
        "## Questions\n\n- Q1: Go on? [yes -> done | no]\n\n## Dev\n\n## Result\n\n" + result + "\n";

    private static (GitFixture Fx, AnswersCommitter Committer, string Answers) Setup(string folder, string text)
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [$"{Jobs}/{folder}/010-A-x.md"] = text, ["other.txt"] = "0\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var answers = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        return (fx, new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main"), answers);
    }

    private static Card Parse(string folder, string text) =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folder}/010-A-x.md", text) ?? throw new InvalidOperationException("not a card path");

    private static string Remote(GitFixture fx, string path) => GitFixture.Git(fx.Bare, "show", "main:" + path).Replace("\r\n", "\n");

    private static string RemoteHead(GitFixture fx) => GitFixture.Git(fx.Bare, "rev-parse", "main");

    private static string RepoFile(GitFixture fx, string path) => File.ReadAllText(Path.Combine(fx.Repo, path)).Replace("\r\n", "\n");

    // MOVE-1: every try starts from the answers worktree reset to main; only the card's paths are
    // committed; pushed to main; a rejected push starts again, up to 3 tries, then "rejected three times".
    [Fact]
    public void Moves_AllCommits_ViaAnswersWorktree_RetryThreeTimes()
    {
        foreach (var raced in new[] { 2, 3 })
        {
            var text = CardBody("needs-human: go on?");
            var (fx, committer, answers) = Setup("active", text);
            using (fx)
            {
                var card = Parse("active", text);
                var calls = 0;
                var result = committer.UpdateCard(card.Path, main =>
                {
                    calls++;
                    // Reset to main: the worktree is at the remote's main and has nothing of the last try.
                    Assert.Equal(RemoteHead(fx), GitFixture.Git(answers, "rev-parse", "HEAD"));
                    Assert.Equal("", GitFixture.Git(answers, "status", "--porcelain"));
                    // A stray change in the worktree is not the card's and is not committed.
                    File.WriteAllText(Path.Combine(answers, "other.txt"), "stray\n");
                    File.WriteAllText(Path.Combine(answers, "stray.txt"), "stray\n");
                    if (calls <= raced)
                    {
                        // Another clone pushes to main between our reset and our push.
                        fx.Push($"race {calls}", new Dictionary<string, string?> { ["race.txt"] = $"{calls}\n" });
                    }

                    return CardMoves.Answer(card, main, 1, 0, "", Now);
                }, replayLocalEdits: false);

                Assert.Equal(3, calls);
                if (raced == 2)
                {
                    Assert.True(result.Ok, result.Error);
                    Assert.Equal(RemoteHead(fx), result.Sha);
                    var files = GitFixture.Git(fx.Bare, "show", "--no-renames", "--name-only", "--format=", "main").Replace("\r\n", "\n").Split('\n');
                    Assert.Equal(new[] { $"{Jobs}/active/010-A-x.md", $"{Jobs}/done/010-A-x.md" }, files.Order(StringComparer.Ordinal));
                    Assert.Contains("-> **yes** (developer, 2026-11-18", Remote(fx, $"{Jobs}/done/010-A-x.md"));
                    Assert.Equal("0", Remote(fx, "other.txt"));
                    Assert.Equal("2", Remote(fx, "race.txt"));
                }
                else
                {
                    Assert.False(result.Ok);
                    Assert.Contains("rejected three times", result.Error);
                    Assert.Equal("race 3", GitFixture.Git(fx.Bare, "log", "-1", "--format=%s", "main"));
                }
            }
        }
    }

    // MOVE-4: the developer's uncommitted edit to the card in the repository is replayed on main
    // with git apply --3way; main's own change is kept; the repository's copy is then reverted.
    // Driven by an Update-Card action (Done, MOVE-6): the rule is Update-Card's (L1204).
    [Fact]
    public void Moves_DeveloperUncommittedEdit_Replayed3Way()
    {
        var text = CardBody("review: check it");
        var (fx, committer, _) = Setup("review", text);
        using (fx)
        {
            var path = $"{Jobs}/review/010-A-x.md";
            File.WriteAllText(Path.Combine(fx.Repo, path), text.Replace("## Dev\n", "## Dev\n\nLooks right to me.\n"));
            fx.Push("worker", new Dictionary<string, string?> { [path] = text.Replace("Notes: as planned", "Notes: changed on main") });

            var card = Parse("review", text);
            var result = committer.UpdateCard(path, main => CardMoves.Done(card, main, Now));

            Assert.True(result.Ok, result.Error);
            var done = Remote(fx, $"{Jobs}/done/010-A-x.md");
            Assert.Contains("## Dev\n\nLooks right to me.\n", done);
            Assert.Contains("Notes: changed on main", done);
            Assert.Contains("Status: done", done);
            Assert.EndsWith("developer: reviewed, done (from the page).", done);
            Assert.Equal(text, RepoFile(fx, path));
            Assert.Equal("", GitFixture.Git(fx.Repo, "status", "--porcelain"));
        }
    }

    // MOVE-4: an edit that only appends at the end, which git apply --3way cannot merge because main
    // rewrote the last lines, is appended to main's copy.
    [Fact]
    public void Moves_DeveloperAppendOnlyEdit_AppendedWhen3WayFails()
    {
        var text = CardBody("review: check it");
        var (fx, committer, _) = Setup("review", text);
        using (fx)
        {
            var path = $"{Jobs}/review/010-A-x.md";
            File.WriteAllText(Path.Combine(fx.Repo, path), text + "\nP.S. from the developer.\n");
            fx.Push("worker", new Dictionary<string, string?> { [path] = CardBody("review: check it", result: "- 2026-11-18 11:00: rewritten by the worker") });

            var card = Parse("review", text);
            var result = committer.UpdateCard(path, main => CardMoves.Done(card, main, Now));

            Assert.True(result.Ok, result.Error);
            var done = Remote(fx, $"{Jobs}/done/010-A-x.md");
            Assert.Contains("- 2026-11-18 11:00: rewritten by the worker\n\nP.S. from the developer.\n", done);
            Assert.DoesNotContain("10:00: started", done);
            Assert.DoesNotContain("<<<<<<<", done);
            Assert.EndsWith("developer: reviewed, done (from the page).", done);
            Assert.Equal(text, RepoFile(fx, path));
        }
    }

    // MOVE-4, MOVE-11: the developer and main changed the same line: the error "could not be merged
    // with main" for the page's notice, nothing pushed, the developer's edit left where it was.
    [Fact]
    public void Moves_DeveloperConflictingEdit_ErrorNothingCommitted()
    {
        var text = CardBody("review: check it");
        var (fx, committer, answers) = Setup("review", text);
        using (fx)
        {
            var path = $"{Jobs}/review/010-A-x.md";
            var mine = text.Replace("Notes: as planned", "Notes: the developer's");
            File.WriteAllText(Path.Combine(fx.Repo, path), mine);
            fx.Push("worker", new Dictionary<string, string?> { [path] = text.Replace("Notes: as planned", "Notes: the worker's") });
            var head = RemoteHead(fx);

            var card = Parse("review", text);
            var calls = 0;
            var result = committer.UpdateCard(path, main =>
            {
                calls++;
                return CardMoves.Done(card, main, Now);
            });

            Assert.False(result.Ok);
            Assert.Contains("your edit to 010-A-x could not be merged with main", result.Error);
            Assert.Equal(0, calls);
            Assert.Equal(head, RemoteHead(fx));
            Assert.Equal(mine, RepoFile(fx, path));
            Assert.Equal("", GitFixture.Git(answers, "status", "--porcelain"));
        }
    }
}
