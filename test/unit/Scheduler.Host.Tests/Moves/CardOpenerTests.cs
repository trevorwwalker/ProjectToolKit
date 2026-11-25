// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;

namespace Scheduler.Host.Tests.Moves;

/// <summary>Opening a card from the page (MOVE-9, Open-Card/Open-Path L1547-1562).</summary>
public class CardOpenerTests
{
    // MOVE-9: the repository's copy when it has one, else the answers worktree's (reset to main).
    // Bringing the editor to the front is not tested (test-plans/index.md: untestable).
    [Fact]
    public void Moves_OpenCard_RepositoryCopyElseAnswers()
    {
        using var fx = new GitFixture();
        const string Here = "docs/jobs/queue/010-A-x.md";
        const string New = "docs/jobs/queue/020-A-y.md";
        fx.Push("one", new Dictionary<string, string?> { [Here] = "# 010-A\n" });
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        fx.Push("two", new Dictionary<string, string?> { [New] = "# 020-A\n" });
        var answers = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        var launcher = new FakeLauncher();
        var opener = new CardOpener(new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main"), fx.Repo, launcher);

        Assert.Equal("", opener.Open(Here));
        Assert.Equal("", opener.Open(New));
        Assert.Equal("docs/jobs/queue/030-A-z.md not found", opener.Open("docs/jobs/queue/030-A-z.md"));

        Assert.Equal(
            new[] { Path.Combine(fx.Repo, "docs", "jobs", "queue", "010-A-x.md"), Path.Combine(answers, "docs", "jobs", "queue", "020-A-y.md") },
            launcher.Opened);
    }

    private sealed class FakeLauncher : IFileLauncher
    {
        public List<string> Opened { get; } = [];

        public void Open(string file) => Opened.Add(file);
    }
}
