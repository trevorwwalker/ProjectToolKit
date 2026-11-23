using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.PageActions;
using Scheduler.Host.State;
using Scheduler.Host.Templates;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Program;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 1116-B (state-cache.md MM-15: a tick's page refresh costs little): the template check runs on the git
/// writer's thread (987-B) and holds its lock through its fetch, commit and reset; the page's refresh reads
/// <c>TemplateRefresh.Edited</c> on the tick's thread. On the live pool (2026-11-22) a project whose refresh
/// commit failed every tick kept that check running every tick, and each PageRefresh waited 5-10 s for it.
/// The read must take the last check's list without waiting for the running one.
/// </summary>
[ParallelMethods]
public sealed class TemplateEditedReadTests : IDisposable
{
    private readonly GitFixture _toolkit = new();
    private readonly GitFixture _project = new();
    private readonly string _logs;

    public TemplateEditedReadTests()
    {
        foreach (var repo in new[] { _project.Repo, _toolkit.Repo })
        {
            GitFixture.Git(repo, "config", "user.name", "scheduler");
            GitFixture.Git(repo, "config", "user.email", "s@s");
            GitFixture.Git(repo, "config", "commit.gpgsign", "false");
        }

        _logs = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "logs");
        Directory.CreateDirectory(_logs);
    }

    public void Dispose()
    {
        _toolkit.Dispose();
        _project.Dispose();
    }

    // MM-15, 1116-B: a page refresh while a template check is held inside its git work returns at once.
    [Fact]
    public async Task PageRefresh_WhileATemplateCheckRuns_DoesNotWaitForIt()
    {
        _toolkit.Push("v1", new Dictionary<string, string?> { ["assets/templates/jobs/README.md"] = "rules v1\n" });
        using var main = new HeldFetchMain(_project.Main());
        var answers = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "slots", "answers");
        var refresh = new TemplateRefresh(new GitRunner(), _toolkit.Repo, main, new AnswersCommitter(new GitRunner(), _project.Repo, answers, "origin", "main"),
            "jobs", Path.Combine(_logs, TemplateState.FileName));
        var config = new SchedulerConfig { Project = "ptk-1116b", JobsDir = "jobs", LogDir = _logs, Repo = _project.Repo, Slots = 1 };
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 22, 21, 0, 0, TimeSpan.Zero));
        var tick = new SchedulerTick(config, clock, new ListLog(), new TickLoad(), new TickProbe(), new ActionProcesses(), new StateFiles(_logs), new ActionTickHost());
        var sources = new PageSnapshotSources
        {
            EditedTemplates = () => [.. refresh.Edited.Select(e => new PageTemplateEdit(config.Project, e.Target))],
        };

        var check = Task.Run(refresh.Check);
        Assert.True(main.Entered.Wait(TimeSpan.FromSeconds(30)), "the check never reached its fetch");
        try
        {
            var build = Task.Run(() => HostPageSnapshot.Build(config, clock, tick, sources));
            Assert.Same(build, await Task.WhenAny(build, Task.Delay(TimeSpan.FromSeconds(5))));
            Assert.Empty((await build).Waiting);
            Assert.False(check.IsCompleted);
        }
        finally
        {
            main.Release.Set();
            _ = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(30)));
        }
    }

    // The project's main, whose fetch holds until the test lets it go: the check is then inside its lock.
    private sealed class HeldFetchMain(IMainRepository inner) : IMainRepository, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public bool Fetch()
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return inner.Fetch();
        }

        public IReadOnlyList<TreeEntry>? ListTree(string folder) => inner.ListTree(folder);

        public string? ReadBlob(string blobId) => inner.ReadBlob(blobId);

        public string? ReadFile(string path) => inner.ReadFile(path);

        public string? HeadSha() => inner.HeadSha();

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
