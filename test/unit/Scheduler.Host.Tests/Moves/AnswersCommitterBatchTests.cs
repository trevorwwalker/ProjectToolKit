// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 1066-B (state-cache.md MM-13): changes ready together are one commit each and one push. A hook on the temporary bare
/// remote counts the pushes that reach it, and can reject the first.
/// </summary>
[ParallelMethods]
public class AnswersCommitterBatchTests
{
    private static (GitFixture Fx, AnswersCommitter Committer) Setup(bool rejectFirst = false)
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { ["other.txt"] = "0\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var hooks = Path.Combine(fx.Bare, "hooks");
        Directory.CreateDirectory(hooks);
        var script = "#!/bin/sh\necho x >> \"$GIT_DIR/pushes.log\"\n"
            + (rejectFirst ? "if [ -f \"$GIT_DIR/reject-once\" ]; then rm \"$GIT_DIR/reject-once\"; exit 1; fi\n" : "")
            + "exit 0\n";
        File.WriteAllText(Path.Combine(hooks, "pre-receive"), script.Replace("\r\n", "\n"));
        if (rejectFirst)
        {
            File.WriteAllText(Path.Combine(fx.Bare, "reject-once"), "");
        }

        var answers = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        return (fx, new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main") { MainModel = () => null });
    }

    private static int Pushes(GitFixture fx)
    {
        var log = Path.Combine(fx.Bare, "pushes.log");
        return File.Exists(log) ? File.ReadAllLines(log).Length : 0;
    }

    private static JobsChange Write(string file) =>
        new() { Message = "write " + file, Writes = new Dictionary<string, string> { [file] = file + "\n" } };

    private static string[] Subjects(GitFixture fx) =>
        GitFixture.Git(fx.Bare, "log", "--format=%s", "main").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // MM-13: three changes ready together are three commits in their order and one push.
    [Fact]
    public void MM_13_ApplyAll_ThreeChanges_OnePush_OneCommitEach_InOrder()
    {
        var (fx, committer) = Setup();
        using (fx)
        {
            var results = committer.ApplyAll([Write("a.txt"), Write("b.txt"), Write("c.txt")]);

            Assert.All(results, r => Assert.True(r.Ok, r.Error));
            Assert.Equal(3, results.Select(r => r.Sha).Distinct().Count());
            Assert.Equal(1, Pushes(fx));
            Assert.Equal(["write c.txt", "write b.txt", "write a.txt", "seed"], Subjects(fx));
            Assert.Equal(results[2].Sha, GitFixture.Git(fx.Bare, "rev-parse", "main").Trim());
        }
    }

    // MM-13: a rejected push redoes the items one by one from the new main; each result is its own, order kept.
    [Fact]
    public void MM_13_ApplyAll_RejectedPush_RetriesOneByOne_NothingDropped()
    {
        var (fx, committer) = Setup(rejectFirst: true);
        using (fx)
        {
            var results = committer.ApplyAll([Write("a.txt"), Write("b.txt"), Write("c.txt")]);

            Assert.All(results, r => Assert.True(r.Ok, r.Error));
            Assert.Equal(4, Pushes(fx)); // the batch (rejected), then one push each
            Assert.Equal(["write c.txt", "write b.txt", "write a.txt", "seed"], Subjects(fx));
        }
    }

    // MM-13: one change that cannot be committed fails alone; the ones around it still land, in order.
    [Fact]
    public void MM_13_ApplyAll_OneBadChange_FailsAlone()
    {
        var (fx, committer) = Setup();
        using (fx)
        {
            var bad = new JobsChange { Message = "move nothing", Moves = [new FileMove { From = "no/such.md", To = "no/where.md" }] };

            var results = committer.ApplyAll([Write("a.txt"), bad, Write("c.txt")]);

            Assert.True(results[0].Ok);
            Assert.False(results[1].Ok);
            Assert.Contains("git mv failed", results[1].Error);
            Assert.True(results[2].Ok);
            Assert.Equal(1, Pushes(fx));
            Assert.Equal(["write c.txt", "write a.txt", "seed"], Subjects(fx));
        }
    }

    // A lone change is as it always was: one commit, one push, its sha.
    [Fact]
    public void MM_13_Apply_OneChange_OnePush()
    {
        var (fx, committer) = Setup();
        using (fx)
        {
            var r = committer.Apply(Write("a.txt"));

            Assert.True(r.Ok, r.Error);
            Assert.Equal(1, Pushes(fx));
            Assert.Equal(r.Sha, GitFixture.Git(fx.Bare, "rev-parse", "main").Trim());
        }
    }
}
