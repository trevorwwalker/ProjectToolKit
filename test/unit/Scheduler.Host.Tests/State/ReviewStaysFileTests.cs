// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Estimates;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.State;

/// <summary>review-stays.csv and its head (EST-16 to EST-19), over a fixture repository in a temporary folder. Test plan: estimates.md section 7.</summary>
public sealed class ReviewStaysFileTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _repo;
    private readonly string _logs;

    public ReviewStaysFileTests()
    {
        _repo = Path.Combine(_temp.Path, "repo");
        _logs = Path.Combine(_temp.Path, "logs");
        Directory.CreateDirectory(_repo);
        GitFixture.Git(_repo, "init", "-q", "-b", "main");
        Commit("start", ("README.md", "x"));
        Commit("1-A-a to review", ("jobs/active/1-A-a.md", null), ("jobs/review/1-A-a.md", "a"));
        Commit("code", ("src/x.cs", "x"));
        Commit("1-A-a done", ("jobs/review/1-A-a.md", null), ("jobs/done/1-A-a.md", "a"));
        Commit("2-B-b to review", ("jobs/review/2-B-b.md", "b"));
    }

    public void Dispose() => _temp.Dispose();

    // Writes (null deletes) the files, commits them and returns the new head.
    private string Commit(string message, params (string Path, string? Text)[] files)
    {
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(_repo, path);
            if (text is null)
            {
                if (File.Exists(full))
                {
                    File.Delete(full);
                }

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        GitFixture.Git(_repo, "add", "-A");
        GitFixture.Git(_repo, "commit", "-q", "--allow-empty", "-m", message);
        return GitFixture.Git(_repo, "rev-parse", "HEAD");
    }

    private string Head => GitFixture.Git(_repo, "rev-parse", "HEAD");

    private string[] Lines => File.ReadAllLines(Path.Combine(_logs, ReviewStaysFile.FileName));

    private static IReadOnlyList<MainCommit>? NoLog(string since) => null;

    // The model's log: one commit after `parent` that moves `card` out of review/ to done/.
    private static Func<string, IReadOnlyList<MainCommit>?> ModelMovesToDone(string parent, string sha, string card) =>
        since => since == parent
            ? [new MainCommit(sha, parent, DateTimeOffset.Now, "jobs: done", [new PathChange($"jobs/review/{card}.md", "1", null), new PathChange($"jobs/done/{card}.md", null, "1")])]
            : null;

    [Fact]
    public void EST_19_NoFile_BackfillsFromTheRoot_Once()
    {
        var git = new GitRunner();
        var file = new ReviewStaysFile(_logs, _repo, "jobs", git);

        Assert.True(file.Advance(Head, NoLog));
        Assert.False(file.Advance(Head, NoLog));

        Assert.Equal(1, file.Walks);
        Assert.Equal(0, git.Started); // in process
        Assert.Equal([ReviewStays.Header], Lines.Take(1));
        var stay = Assert.Single(Lines.Skip(1).Select(ReviewStays.Parse));
        Assert.Equal(("1-A-a", "done"), (stay!.Card, stay.To));
        Assert.Equal("2-B-b", Assert.Single(file.Open).Card);
        Assert.Equal(Head, File.ReadAllText(Path.Combine(_logs, ReviewStaysFile.HeadName)).Trim());
    }

    [Fact]
    public void EST_19_TheGitBackfill_GivesTheSameStays_WithOneProcess()
    {
        var git = new GitRunner();
        var file = new ReviewStaysFile(_logs, _repo, "jobs", git, inProcess: (_, _) => null);

        file.Advance(Head, NoLog);

        Assert.Equal(1, git.Started);
        var stay = Assert.Single(file.Closed);
        Assert.Equal(("1-A-a", "done"), (stay.Card, stay.To));
        Assert.Equal("2-B-b", Assert.Single(file.Open).Card);
    }

    [Fact]
    public void EST_16_ClosedStaysAppend_AndOpenStaysRebuildAtStartUp()
    {
        new ReviewStaysFile(_logs, _repo, "jobs", new GitRunner()).Advance(Head, NoLog);
        Assert.Equal(2, Lines.Length);

        // A restart: the file and head are there; the open stay comes back and nothing is written twice.
        var again = new ReviewStaysFile(_logs, _repo, "jobs", new GitRunner());
        again.Advance(Head, NoLog);

        Assert.Equal(2, Lines.Length);
        Assert.Equal("2-B-b", Assert.Single(again.Open).Card);

        // Main moved while stopped: the stay that closed in the gap is appended once.
        var head = Commit("2-B-b done", ("jobs/review/2-B-b.md", null), ("jobs/done/2-B-b.md", "b"));
        var third = new ReviewStaysFile(_logs, _repo, "jobs", new GitRunner());
        third.Advance(head, NoLog);

        Assert.Equal(3, Lines.Length);
        Assert.Equal(["1-A-a", "2-B-b"], Lines.Skip(1).Select(l => ReviewStays.Parse(l)!.Card));
        Assert.Empty(third.Open);
    }

    [Fact]
    public void EST_17_ATickAtTheHead_ReadsNothing_AndStartsNoProcess()
    {
        var git = new GitRunner();
        var file = new ReviewStaysFile(_logs, _repo, "jobs", git, inProcess: (_, _) => null);
        file.Advance(Head, NoLog);
        var started = git.Started;
        var reads = 0;

        Assert.False(file.Advance(Head, since =>
        {
            reads++;
            return null;
        }));

        Assert.Equal(0, reads);
        Assert.Equal(started, git.Started);
        Assert.Equal(1, file.Walks);
    }

    [Fact]
    public void EST_17_ANewCommitMovingACardOutOfReview_AppendsOneLine_FromTheModel()
    {
        var git = new GitRunner();
        var file = new ReviewStaysFile(_logs, _repo, "jobs", git);
        var head = Head;
        file.Advance(head, NoLog);

        Assert.True(file.Advance("feed", ModelMovesToDone(head, "feed", "2-B-b")));

        Assert.Equal(0, git.Started);
        Assert.Equal(1, file.Walks);
        Assert.Equal(3, Lines.Length);
        Assert.Equal(("2-B-b", "done", "feed"), (file.Closed[^1].Card, file.Closed[^1].To, file.Closed[^1].LeftCommit));
        Assert.Empty(file.Open);
        Assert.Equal("feed", File.ReadAllText(Path.Combine(_logs, ReviewStaysFile.HeadName)).Trim());
    }

    [Fact]
    public void EST_18_AGapTheModelDoesNotReach_IsWalkedOnceInProcess_ThenTheModelServes()
    {
        var git = new GitRunner();
        var file = new ReviewStaysFile(_logs, _repo, "jobs", git);
        file.Advance(Head, NoLog);

        var head = Commit("2-B-b done", ("jobs/review/2-B-b.md", null), ("jobs/done/2-B-b.md", "b"));
        Assert.True(file.Advance(head, NoLog));

        Assert.Equal(2, file.Walks);
        Assert.Equal(0, git.Started);
        Assert.Equal(3, Lines.Length);

        Assert.False(file.Advance("next", since => since == head ? [new MainCommit("next", head, DateTimeOffset.Now, "code", [new PathChange("src/y.cs", null, "1")])] : null));
        Assert.Equal(2, file.Walks);
        Assert.Equal(0, git.Started);
    }

    [Fact]
    public void EST_18_AnInProcessNull_MakesExactlyOneGitLog_AndASecondAdvanceNeither()
    {
        var git = new GitRunner();
        var file = new ReviewStaysFile(_logs, _repo, "jobs", git, inProcess: (_, _) => null);
        file.Advance(Head, NoLog);
        Assert.Equal(1, git.Started);

        var head = Commit("2-B-b done", ("jobs/review/2-B-b.md", null), ("jobs/done/2-B-b.md", "b"));
        Assert.True(file.Advance(head, NoLog));

        Assert.Equal(2, git.Started);
        Assert.Equal("2-B-b", file.Closed[^1].Card);

        file.Advance("next", since => since == head ? [] : null);
        Assert.Equal(2, git.Started);
        Assert.Equal(2, file.Walks);
    }

    [Fact]
    public void EST_18_AHeadNotOnTheLine_IsNoHead_AndTheFileIsWrittenAgain()
    {
        Directory.CreateDirectory(_logs);
        File.WriteAllText(Path.Combine(_logs, ReviewStaysFile.FileName), ReviewStays.Header + "\n2026-01-01T00:00:00,2026-01-01T00:10:00,9-Z-old,done,x,y\n");
        File.WriteAllText(Path.Combine(_logs, ReviewStaysFile.HeadName), new string('0', 40) + "\n");

        var file = new ReviewStaysFile(_logs, _repo, "jobs", new GitRunner());
        file.Advance(Head, NoLog);

        Assert.Equal(["1-A-a"], Lines.Skip(1).Select(l => ReviewStays.Parse(l)!.Card));
    }
}
