// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 1119-B: an answers folder that is a plain folder inside an outer repository (UltraCommand's, inside
/// C:\Projects) is set aside and re-created as a worktree, and no git the committer runs reaches the outer repository.
/// </summary>
public class AnswersCommitterWorktreeTests
{
    // The fixture's folder made an outer repository with one commit; the answers folder a plain folder inside it.
    private static (GitFixture Fx, string Outer, string Answers) Setup()
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { ["jobs/README.md"] = "rules\n" });
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var outer = Path.GetDirectoryName(fx.Repo)!;
        GitFixture.Git(outer, "init", "-q", "-b", "master");
        GitFixture.Git(outer, "commit", "-q", "--allow-empty", "-m", "initial");
        var answers = Path.Combine(outer, "Project-workspaces", "answers");
        Directory.CreateDirectory(Path.Combine(answers, "jobs"));
        File.WriteAllText(Path.Combine(answers, "jobs", "README.md"), "stale\n");
        return (fx, outer, answers);
    }

    [Fact]
    public void AnswersTree_PlainFolderInsideOuterRepository_IsRecreatedAsWorktree()
    {
        var (fx, outer, answers) = Setup();
        using var fixture = fx;
        var log = new ListLog();
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main", log);

        Assert.Equal(answers, committer.AnswersTree());

        Assert.True(GitRunner.IsWorktreeOf(answers, fx.Repo));
        Assert.Equal("rules", File.ReadAllText(Path.Combine(answers, "jobs", "README.md")).TrimEnd());
        var aside = Directory.GetDirectories(Path.GetDirectoryName(answers)!, "answers.not-a-worktree-*");
        Assert.Single(aside);
        Assert.Equal("stale\n", File.ReadAllText(Path.Combine(aside[0], "jobs", "README.md")));
        Assert.Contains(log.Lines, l => l.Contains("was not a worktree", StringComparison.Ordinal));
        Assert.Equal("1", GitFixture.Git(outer, "rev-list", "--count", "HEAD"));
        Assert.Equal("answers", GitFixture.Git(answers, "rev-parse", "--abbrev-ref", "HEAD"));

        // A worktree that is already there is kept as it is.
        Assert.Equal(answers, committer.AnswersTree());
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(answers)!, "answers.not-a-worktree-*"));
    }

    [Fact]
    public void AnswersTree_WorktreeAddFails_LoggedOnceAndNoGitReachesOuterRepository()
    {
        var (fx, outer, answers) = Setup();
        using var fixture = fx;
        var log = new ListLog();
        var git = new GitRunner();
        // No origin/nowhere: the worktree cannot be added.
        var committer = new AnswersCommitter(git, fx.Repo, answers, "origin", "nowhere", log);

        _ = committer.AnswersTree();
        _ = committer.AnswersTree();
        Assert.Single(log.Lines, l => l.Contains("worktree add failed", StringComparison.Ordinal));

        // What the refresh did in UltraCommand: write files into the plain folder and commit them there.
        Directory.CreateDirectory(answers);
        File.WriteAllText(Path.Combine(answers, "card.md"), "x\n");
        Assert.False(GitRunner.IsWorktreeOf(answers, fx.Repo));
        Assert.False(git.Run(answers, "add", "--", "card.md").Ok);
        Assert.False(git.Run(answers, "commit", "-q", "-m", "stray").Ok);
        Assert.False(git.Run(answers, "reset", "-q", "--hard", "HEAD").Ok);
        Assert.Equal("1", GitFixture.Git(outer, "rev-list", "--count", "HEAD"));
        Assert.Equal("", GitFixture.Git(outer, "diff", "--cached", "--name-only"));
        Assert.True(File.Exists(Path.Combine(answers, "card.md")));
    }
}
