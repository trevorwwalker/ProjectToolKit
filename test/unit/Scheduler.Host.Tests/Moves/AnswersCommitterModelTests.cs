using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;
using Scheduler.Host.Moves;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Moves;

/// <summary>
/// 1064-B (state-cache.md MM-11, section 7's real-git set): a push through the answers worktree is handed to the
/// repository's model of main, which moves to the pushed commit with the blobs as committed - the same model a load
/// at that commit gives - and a rejected push leaves it as it was.
/// </summary>
[ParallelMethods]
public class AnswersCommitterModelTests
{
    private const string Jobs = "docs/jobs";
    private const string Queued = Jobs + "/queue/010-A-x.md";
    private const string Active = Jobs + "/active/010-A-x.md";
    private const string Added = Jobs + "/queue/011-B-y.md";
    private const string Card = "# 010-A: x\n\nCategory: A\nStatus: ready\n\n## Result\n";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static (GitFixture Fx, AnswersCommitter Committer, MainModels Models, MainModelsRepository Repo) Setup()
    {
        var fx = new GitFixture();
        fx.Push("seed", new Dictionary<string, string?> { [Queued] = Card, ["other.txt"] = "0\n" });
        GitFixture.Git(fx.Repo, "config", "user.name", "scheduler");
        GitFixture.Git(fx.Repo, "config", "user.email", "s@s");
        GitFixture.Git(fx.Repo, "config", "commit.gpgsign", "false");
        GitFixture.Git(fx.Repo, "config", "core.autocrlf", "true");   // the writer's text is not what is committed
        GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
        var answers = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slots", "answers");
        var models = new MainModels(1);
        var repo = models.Join(new MainModelsJoin(fx.Repo, "origin", "main", Jobs, TimeSpan.FromSeconds(15), false,
            new GitRunner(), new ListLog(), new FakeClock(DateTimeOffset.Now)));
        Assert.True(repo.Loading.Wait(Wait), "load did not end");
        Assert.NotNull(repo.Model);
        var committer = new AnswersCommitter(new GitRunner(), fx.Repo, answers, "origin", "main") { MainModel = () => repo.Model };
        return (fx, committer, models, repo);
    }

    private static JobsChange Change() => new()
    {
        Message = "jobs: start 010-A-x",
        Moves = [new FileMove { From = Queued, To = Active }],
        Writes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Added] = "# 011-B: y\r\n\r\nCategory: B\r\nStatus: ready\r\n",   // written CRLF, committed LF
        },
    };

    [Fact]
    public void MM_11_DirectApplyEqualsALoadAtThePushedCommit()
    {
        var (fx, committer, models, repo) = Setup();
        using var _ = fx;
        try
        {
            var model = repo.Model!;
            var before = model.Commit;

            var pushed = committer.Apply(Change());

            Assert.True(pushed.Ok, pushed.Error);
            Assert.Equal(GitFixture.Git(fx.Bare, "rev-parse", "main"), pushed.Sha);
            Assert.Same(model, repo.Model);
            Assert.Equal(pushed.Sha, model.Commit);   // moved by the writer: no tick, no ref read ran
            Assert.Equal(before, model.Commits(before)?.Single().Parent);

            using var objects = new MainObjectsSource(MainObjects.TryOpen(fx.Repo, out var refusal) ?? throw new InvalidOperationException(refusal));
            var fresh = new RepositoryModel(objects, new MainLayout([Jobs]), new FakeClock(DateTimeOffset.Now));
            Assert.True(fresh.Load(pushed.Sha));

            Assert.Equal(fresh.Entries(Jobs).Select(e => (e.Path, e.BlobId)), model.Entries(Jobs).Select(e => (e.Path, e.BlobId)));
            Assert.Equal(fresh.Cards(Jobs).Select(c => (c.Path, c.Folder, c.Status)), model.Cards(Jobs).Select(c => (c.Path, c.Folder, c.Status)));
            Assert.Equal("# 011-B: y\n\nCategory: B\nStatus: ready\n", model.Text(Added));   // as committed, not as written
            Assert.Equal(GitFixture.Git(fx.Bare, "show", "main:" + Added).Replace("\r\n", "\n").TrimEnd(), model.Text(Added)!.TrimEnd());
            Assert.Null(model.Text(Queued));
        }
        finally
        {
            models.Leave(repo, Jobs);
        }
    }

    [Fact]
    public void MM_11_RejectedPush_ModelUnchanged()
    {
        var (fx, committer, models, repo) = Setup();
        using var _ = fx;
        try
        {
            var hook = Path.Combine(fx.Bare, "hooks", "pre-receive");
            Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
            File.WriteAllText(hook, "#!/bin/sh\necho no >&2\nexit 1\n");
            var model = repo.Model!;
            var before = model.Snapshot;

            var pushed = committer.Apply(Change());

            Assert.False(pushed.Ok);
            Assert.Same(before, model.Snapshot);
            Assert.NotNull(model.Text(Queued));
            Assert.Null(model.Text(Added));
        }
        finally
        {
            models.Leave(repo, Jobs);
        }
    }
}
