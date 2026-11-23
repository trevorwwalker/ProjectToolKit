using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Git;

/// <summary>1062-B: the Host's models of main, one per repository, over a real repository (state-cache.md section 7).</summary>
[ParallelMethods]
public class MainModelsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static string Card(string title, string status) =>
        $"# {title}\n\nCategory: B\nType: feature\nStatus: {status}\n\n## Task\n\nDo it.\n\n## Result\n";

    private static Dictionary<string, string?> Start() => new()
    {
        ["jobs/queue/1-B-one.md"] = Card("1-B: One", "ready"),
        ["jobs/active/2-B-two.md"] = Card("2-B: Two", "ready"),
        ["jobs/done/3-B-three.md"] = Card("3-B: Three", "done"),
        ["jobs/done/4-B-four.md"] = Card("4-B: Four", "done"),
        ["jobs/scheduler.json"] = "{ \"pollSeconds\": 15 }\n",
        ["src/app.txt"] = "not in the model\n",
    };

    // Seed pushes to the bare remote; Repo (the scheduler's) fetches it, so its origin/main is set.
    private static GitFixture Fixture(Dictionary<string, string?>? files = null)
    {
        var fx = new GitFixture();
        fx.Push("start", files ?? Start());
        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
        return fx;
    }

    private static MainModelsJoin JoinOf(GitFixture fx, GitRunner git, ISchedulerLog log, string jobs = "jobs", bool cli = false) =>
        new(fx.Repo, "origin", "main", jobs, TimeSpan.FromSeconds(15), cli, git, log, new FakeClock(DateTimeOffset.Now));

    private static bool Ended(Task load) => load.Wait(Wait);

    private static MainModelsRepository Loaded(MainModels models, MainModelsJoin join)
    {
        var repo = models.Join(join);
        Assert.True(Ended(repo.Loading), "load did not end");
        Assert.NotNull(repo.Model);
        return repo;
    }

    // A worker's slot: a worktree of Repo, as SlotWorktrees makes it; its push moves Repo's origin/main (MM-8).
    private static string Slot(GitFixture fx)
    {
        var slot = Path.Combine(Path.GetDirectoryName(fx.Repo)!, "slot1");
        GitFixture.Git(fx.Repo, "worktree", "add", "-q", "-B", "slot1", slot, "origin/main");
        return slot;
    }

    private static void PushFromSlot(string slot, string message, Action<string> change)
    {
        change(slot);
        GitFixture.Git(slot, "add", "-A");
        GitFixture.Git(slot, "commit", "-q", "-m", message);
        GitFixture.Git(slot, "push", "-q", "origin", "HEAD:main");
    }

    [Fact]
    public void MM_5_LoadsInTwoPhases_InProcess_LogsTheReader()
    {
        using var fx = Fixture();
        var log = new ListLog();
        var git = new GitRunner();
        var repo = Loaded(new MainModels(2), JoinOf(fx, git, log));

        var model = repo.Model!;
        Assert.Equal("in process", repo.Reader);
        Assert.Equal(GitFixture.Git(fx.Repo, "rev-parse", "origin/main"), model.Commit);
        Assert.Equal(["1-B", "2-B"], model.Cards("jobs").Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.True(model.DoneLoaded);
        Assert.Equal(["3-B", "4-B"], model.DoneCards("jobs").Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Equal("{ \"pollSeconds\": 15 }\n", model.Settings("jobs/scheduler.json"));
        Assert.Equal(0, git.Started);   // MM-1: read in process, no git started
        var lines = log.Lines;
        var phaseOne = lines.FindIndex(l => l.StartsWith($"main model: {repo.Repository}: in process, 2 open card(s) at ", StringComparison.Ordinal));
        var phaseTwo = lines.FindIndex(l => l.StartsWith($"main model: {repo.Repository}: 2 done card(s) in ", StringComparison.Ordinal));
        Assert.True(phaseOne >= 0 && phaseTwo > phaseOne, string.Join("\n", lines));
    }

    [Fact]
    public void MM_8_WorkerPushFromSlot_SeenByRefReadWithoutFetch()
    {
        using var fx = Fixture();
        var git = new GitRunner();
        var repo = Loaded(new MainModels(2), JoinOf(fx, git, new ListLog()));
        var slot = Slot(fx);

        PushFromSlot(slot, "jobs: start 1-B-one", s =>
            File.Move(Path.Combine(s, "jobs", "queue", "1-B-one.md"), Path.Combine(s, "jobs", "active", "1-B-one.md")));

        Assert.True(repo.Tick());
        var model = repo.Model!;
        Assert.Equal(GitFixture.Git(fx.Repo, "rev-parse", "origin/main"), model.Commit);
        Assert.All(model.Cards("jobs"), c => Assert.Equal(CardFolder.Active, c.Folder));
        Assert.Equal("jobs: start 1-B-one", model.Commits(null)!.Last().Subject);
        Assert.Equal(0, repo.Gate.Fetches);
        Assert.Equal(0, git.Started);
    }

    [Fact]
    public void MM_10_RefUnchanged_TickReadsNothingAndStartsNoGit()
    {
        using var fx = Fixture();
        var git = new GitRunner();
        var repo = Loaded(new MainModels(2), JoinOf(fx, git, new ListLog()));
        var before = repo.Model!.Snapshot;

        Assert.False(repo.Tick());
        Assert.False(repo.Tick());

        Assert.Same(before, repo.Model!.Snapshot);
        Assert.Equal(0, git.Started);
    }

    [Fact]
    public void MM_9_FetchGate_RunsOneGitFetch_ThenShares_ClickNever_AndAdvances()
    {
        using var fx = Fixture();
        var git = new GitRunner();
        var repo = Loaded(new MainModels(2), JoinOf(fx, git, new ListLog()));
        fx.Push("jobs: done 2-B-two", new Dictionary<string, string?>
        {
            ["jobs/active/2-B-two.md"] = null,
            ["jobs/done/2-B-two.md"] = Card("2-B: Two", "done"),
        });

        Assert.False(repo.Tick());   // not fetched yet: the ref has not moved
        Assert.Equal(FetchAnswer.Never, repo.Fetch(FetchAsk.Click));
        Assert.Equal(0, git.Started);

        Assert.Equal(FetchAnswer.Fetched, repo.Fetch(FetchAsk.Poll));
        Assert.Equal(FetchAnswer.Shared, repo.Fetch(FetchAsk.Planner));
        Assert.Equal(FetchAnswer.Never, repo.Fetch(FetchAsk.MoveRequest));

        Assert.Equal(1, git.Started);
        var model = repo.Model!;
        Assert.Equal(GitFixture.Git(fx.Bare, "rev-parse", "main"), model.Commit);
        Assert.Equal(["1-B"], model.Cards("jobs").Select(c => c.Id));
        Assert.Contains(model.DoneCards("jobs"), d => d.Id == "2-B");
    }

    [Fact]
    public void MM_2_Alternates_RefusedInProcess_ReadThroughTheCli()
    {
        using var fx = Fixture();
        // A valid alternate (Seed's objects): git reads the repository, the in-process reader refuses it.
        var alternates = Path.Combine(fx.Repo, ".git", "objects", "info", "alternates");
        Directory.CreateDirectory(Path.GetDirectoryName(alternates)!);
        File.WriteAllText(alternates, Path.Combine(fx.Seed, ".git", "objects").Replace('\\', '/') + "\n");
        var log = new ListLog();
        var repo = Loaded(new MainModels(2), JoinOf(fx, new GitRunner(), log));

        Assert.StartsWith("git CLI (", repo.Reader, StringComparison.Ordinal);
        Assert.Contains("alternates", repo.Reader, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, repo.Model!.Cards("jobs").Count);
        Assert.Equal(2, repo.Model.DoneCards("jobs").Count);
        Assert.Contains(log.Lines, l => l.StartsWith($"main model: {repo.Repository}: git CLI (", StringComparison.Ordinal));
    }

    [Fact]
    public void MM_16_GitReaderCliSetting_SameModelAsInProcess()
    {
        using var fx = Fixture();
        var inProcess = Loaded(new MainModels(2), JoinOf(fx, new GitRunner(), new ListLog()));
        var cliGit = new GitRunner();
        var cli = Loaded(new MainModels(2), JoinOf(fx, cliGit, new ListLog(), cli: true));

        Assert.Equal("git CLI (gitReader: cli)", cli.Reader);
        Assert.True(cliGit.Started > 0);
        var a = inProcess.Model!.Snapshot!;
        var b = cli.Model!.Snapshot!;
        Assert.Equal(a.Commit, b.Commit);
        Assert.Equal(a.Files.OrderBy(f => f.Key, StringComparer.Ordinal), b.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        static string Open(Card c) => $"{c.Path} {c.Folder} {c.Id} {c.Title} {c.Status} {c.BlobId}";
        static string Done(DoneCard d) => $"{d.Path} {d.Folder} {d.Id} {d.Title} {d.Status} {d.BlobId}";
        Assert.Equal(a.Cards("jobs").Select(Open), b.Cards("jobs").Select(Open));
        Assert.Equal(a.DoneCards("jobs").Select(Done), b.DoneCards("jobs").Select(Done));
        Assert.Equal(a.Settings("jobs/scheduler.json"), b.Settings("jobs/scheduler.json"));
    }

    [Fact]
    public void MM_16_CliLoadOf500Cards_StartsAtMostTenGitProcesses_SameModelAsInProcess()
    {
        var files = Start();
        for (var n = 10; n < 510; n++)
        {
            var folder = n % 5 == 0 ? "done" : n % 5 == 1 ? "queue" : n % 5 == 2 ? "active" : n % 5 == 3 ? "review" : "done";
            files[$"jobs/{folder}/{n}-B-card-{n}.md"] = Card($"{n}-B: Card {n}", folder == "done" ? "done" : "ready");
        }

        using var fx = Fixture(files);
        var inProcess = Loaded(new MainModels(2), JoinOf(fx, new GitRunner(), new ListLog()));
        var cliGit = new GitRunner();
        var cli = Loaded(new MainModels(2), JoinOf(fx, cliGit, new ListLog(), cli: true));

        Assert.Equal("git CLI (gitReader: cli)", cli.Reader);
        Assert.InRange(cliGit.Started, 1, 10);
        var a = inProcess.Model!.Snapshot!;
        var b = cli.Model!.Snapshot!;
        Assert.Equal(a.Commit, b.Commit);
        Assert.Equal(a.Files.OrderBy(f => f.Key, StringComparer.Ordinal), b.Files.OrderBy(f => f.Key, StringComparer.Ordinal));
        Assert.Equal(a.Cards("jobs").Count, b.Cards("jobs").Count);
        Assert.Equal(a.DoneCards("jobs").Count, b.DoneCards("jobs").Count);
        Assert.True(b.Cards("jobs").Count + b.DoneCards("jobs").Count >= 500);
    }

    [Fact]
    public void DL_22_ProjectsInOneRepository_ShareOneModel_LastToLeaveDropsIt()
    {
        var files = Start();
        files["other/jobs/queue/9-C-nine.md"] = Card("9-C: Nine", "ready");
        using var fx = Fixture(files);
        var models = new MainModels(2);
        var git = new GitRunner();
        var log = new ListLog();
        var first = Loaded(models, JoinOf(fx, git, log));
        var second = models.Join(JoinOf(fx, git, log, jobs: "other\\jobs/") with { Repository = fx.Repo + Path.DirectorySeparatorChar });
        Assert.True(Ended(second.Loading));

        Assert.Same(first, second);
        Assert.Single(models.Repositories);
        Assert.Equal(["jobs", "other/jobs"], first.JobsDirs);
        Assert.Equal(["9-C"], first.Model!.Cards("other/jobs").Select(c => c.Id));
        Assert.Equal(2, first.Model.Cards("jobs").Count);

        models.Leave(first, "jobs");
        Assert.Same(first, models.For(fx.Repo));
        models.Leave(second, "other/jobs");
        Assert.Null(models.For(fx.Repo));
    }

    [Fact]
    public void Section4_ThreeFailedReadsInARow_SwitchToTheCliUntilRestart()
    {
        using var fx = Fixture();
        var log = new ListLog();
        var objects = Scheduler.Host.Git.Objects.MainObjects.TryOpen(fx.Repo, out _)!;
        var broken = new BreakableSource(new MainObjectsSource(objects));
        var git = new GitRunner();
        using var repo = new MainModelsRepository(MainModels.Key(fx.Repo), "origin", "main", TimeSpan.FromSeconds(15), false,
            git, log, new FakeClock(DateTimeOffset.Now), new SemaphoreSlim(2), broken);
        repo.AddJobsDir("jobs");
        Assert.True(Ended(repo.Load()));
        Assert.Equal("in process", repo.Reader);

        PushFromSlot(Slot(fx), "jobs: start 1-B-one", s =>
            File.Move(Path.Combine(s, "jobs", "queue", "1-B-one.md"), Path.Combine(s, "jobs", "active", "1-B-one.md")));
        broken.Broken = true;
        var at = repo.Model!.Commit;
        Assert.False(repo.Tick());
        Assert.False(repo.Tick());
        Assert.Equal(at, repo.Model!.Commit);   // CARD-3: a failed advance keeps the snapshot
        Assert.Equal("in process", repo.Reader);
        Assert.False(repo.Tick());   // the third

        Assert.Equal("git CLI (in-process reads failed)", repo.Reader);
        Assert.True(Ended(repo.Loading));
        Assert.Equal(GitFixture.Git(fx.Repo, "rev-parse", "origin/main"), repo.Model!.Commit);
        Assert.All(repo.Model.Cards("jobs"), c => Assert.Equal(CardFolder.Active, c.Folder));
        Assert.Single(log.Lines, l => l.EndsWith("; git CLI (in-process reads failed) until restart", StringComparison.Ordinal));
    }

    [Fact]
    public void MM_5_NoTrackingRefYet_LoadsOnTheFirstTickAfterItAppears()
    {
        using var fx = new GitFixture();
        fx.Push("start", Start());
        var log = new ListLog();
        var repo = new MainModels(2).Join(JoinOf(fx, new GitRunner(), log));
        Assert.True(Ended(repo.Loading));
        Assert.Null(repo.Model);
        Assert.False(repo.Tick());   // still no ref: tries again, logs once
        Assert.True(Ended(repo.Loading));
        Assert.Single(log.Lines, l => l.EndsWith("no origin/main yet", StringComparison.Ordinal));

        GitFixture.Git(fx.Repo, "fetch", "-q", "origin");
        repo.Tick();
        Assert.True(Ended(repo.Loading));
        Assert.Equal(2, repo.Model!.Cards("jobs").Count);
    }

    [Fact]
    public void NotARepository_NoModel_NoGitStartedOnAnyTick()
    {
        using var temp = new TempDir();
        var git = new GitRunner();
        var log = new ListLog();
        var repo = new MainModels(2).Join(new MainModelsJoin(temp.Path, "origin", "main", "jobs", TimeSpan.FromSeconds(15), false,
            git, log, new FakeClock(DateTimeOffset.Now)));
        Assert.True(Ended(repo.Loading));

        Assert.False(repo.Tick());
        Assert.False(repo.Tick());

        Assert.Equal("none (not a git repository)", repo.Reader);
        Assert.Null(repo.Model);
        Assert.Equal(0, git.Started);
        Assert.Single(log.Lines);
    }

    [Fact]
    public void MM_5_LoadNowFalse_LoadsOnTheFirstTick_ASecondJobsFolderStillReloads()
    {
        var files = Start();
        files["other/jobs/queue/9-C-nine.md"] = Card("9-C: Nine", "ready");
        using var fx = Fixture(files);
        var models = new MainModels(2);
        var log = new ListLog();
        var repo = models.Join(JoinOf(fx, new GitRunner(), log) with { LoadNow = false });
        Assert.True(Ended(repo.Loading));
        Assert.Null(repo.Model);
        Assert.Empty(log.Lines);

        Assert.False(repo.Tick());   // the first card read starts the load
        Assert.True(Ended(repo.Loading));
        Assert.Equal(2, repo.Model!.Cards("jobs").Count);

        models.Join(JoinOf(fx, new GitRunner(), log, jobs: "other/jobs") with { LoadNow = false });
        Assert.True(Ended(repo.Loading));
        Assert.Equal(["9-C"], repo.Model!.Cards("other/jobs").Select(c => c.Id));
    }

    [Fact]
    public void Parallel_FromPtkJobs_PositiveElseFive()
    {
        Assert.Equal(3, MainModels.Parallel("3"));
        Assert.Equal(5, MainModels.Parallel(null));
        Assert.Equal(5, MainModels.Parallel("0"));
        Assert.Equal(5, MainModels.Parallel("many"));
    }

    // The in-process source, made to fail every object read on demand (a corrupt or deleted pack); the ref still reads.
    private sealed class BreakableSource(IMainModelSource inner) : IMainModelSource
    {
        public bool Broken { get; set; }

        public string Kind => inner.Kind;

        public bool IsCli => inner.IsCli;

        public string? ReadRef(string refName) => inner.ReadRef(refName);

        public IDisposable Session() => inner.Session();

        public CommitInfo? ReadCommit(string sha) => Broken ? throw new IOException("pack gone") : inner.ReadCommit(sha);

        public IReadOnlyList<TreeItem>? ReadTree(string treeId) => Broken ? throw new IOException("pack gone") : inner.ReadTree(treeId);

        public string? ReadBlob(string blobId) => Broken ? throw new IOException("pack gone") : inner.ReadBlob(blobId);

        public void Dispose() => inner.Dispose();
    }
}
