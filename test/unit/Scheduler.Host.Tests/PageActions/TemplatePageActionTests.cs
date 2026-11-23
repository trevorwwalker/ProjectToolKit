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
/// The two page actions of roadmap item 1 (job card 580-A): Overwrite takes the toolkit's template
/// for a generic file the project edited, Keep leaves the project's copy, and either way the copy
/// disappears from Waiting on you - the row comes from <c>TemplateRefresh.Edited</c>, which both
/// actions clear. The toolkit and the project are real temporary repositories, as 540-B's tests have
/// them, so the commit and the state file are the ones the scheduler writes.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5, PAGE-9).
/// </summary>
[ParallelMethods]
public sealed class TemplatePageActionTests : IDisposable
{
    private const string Readme = "assets/templates/jobs/README.md";
    private const string Copy = "jobs/README.md";

    private readonly GitFixture _toolkit = new();
    private readonly GitFixture _project = new();
    private readonly ListLog _log = new();
    private readonly string _logs;
    private int _ticks;

    public TemplatePageActionTests()
    {
        foreach (var repo in new[] { _project.Repo, _toolkit.Repo })
        {
            GitFixture.Git(repo, "config", "user.name", "scheduler");
            GitFixture.Git(repo, "config", "user.email", "s@s");
            GitFixture.Git(repo, "config", "commit.gpgsign", "false");
            GitFixture.Git(repo, "config", "core.autocrlf", "false");
        }

        _logs = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "logs");
        Directory.CreateDirectory(_logs);
    }

    public void Dispose()
    {
        _toolkit.Dispose();
        _project.Dispose();
    }

    // PAGE-5, roadmap item 1: Overwrite from the page commits the toolkit's template and the row is
    // gone from the next snapshot.
    [Fact]
    public void TemplateActions_Overwrite_TakesTheTemplateAndTheRowGoes()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { [Copy] = "my rules\n" });
        var (actions, refresh, snapshot) = Wire();

        Assert.Contains(PageWaitingKind.TemplateEdited, Kinds(snapshot()));

        var result = actions.TemplateOverwrite("ptk-580a", Copy);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("rules v2", OnMain(Copy));
        Assert.Empty(refresh.Edited);
        Assert.DoesNotContain(PageWaitingKind.TemplateEdited, Kinds(snapshot()));
        Assert.StartsWith("jobs: take the toolkit's jobs/README.md (", LastMessage(), StringComparison.Ordinal);
        Assert.Equal(1, _ticks);
    }

    // PAGE-5, roadmap item 1: Keep leaves the project's copy alone, and stops asking - the row is
    // gone and a later check does not bring it back.
    [Fact]
    public void TemplateActions_Keep_LeavesTheCopyAndStopsAsking()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { [Copy] = "my rules\n" });
        var (actions, refresh, snapshot) = Wire();

        var result = actions.TemplateKeep("ptk-580a", Copy);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("my rules", OnMain(Copy));
        Assert.Empty(refresh.Edited);
        Assert.DoesNotContain(PageWaitingKind.TemplateEdited, Kinds(snapshot()));

        // A scheduler started again reads the choice from the state file: still no row.
        var again = Refresh();
        again.Step();
        Assert.Empty(again.Edited);
        Assert.Equal("my rules", OnMain(Copy));
    }

    // PAGE-9: the routes name the project the row came from; another project's copy is not this
    // scheduler's to write, so both actions refuse and nothing is committed.
    [Fact]
    public void TemplateActions_AnotherProject_IsRefused()
    {
        Toolkit("v1", "rules v1\n");
        Toolkit("v2", "rules v2\n");
        Project("seed", new Dictionary<string, string?> { [Copy] = "my rules\n" });
        var (actions, _, snapshot) = Wire();
        var before = Commits();

        Assert.False(actions.TemplateOverwrite("other-project", Copy).Ok);
        Assert.False(actions.TemplateKeep("other-project", Copy).Ok);
        Assert.False(actions.TemplateOverwrite("ptk-580a", "").Ok);

        Assert.Equal(before, Commits());
        Assert.Equal("my rules", OnMain(Copy));
        Assert.Contains(PageWaitingKind.TemplateEdited, Kinds(snapshot()));
    }

    // A scheduler that keeps no templates (an observe or dry run, or no toolkit found) refuses both
    // rather than pretending.
    [Fact]
    public void TemplateActions_NoTemplates_Refused()
    {
        Toolkit("v1", "rules v1\n");
        Project("seed", new Dictionary<string, string?> { [Copy] = "my rules\n" });
        var actions = Parts(null).Actions;

        Assert.False(actions.TemplateOverwrite("ptk-580a", Copy).Ok);
        Assert.False(actions.TemplateKeep("ptk-580a", Copy).Ok);
    }

    // The copy as the project has it, the refresh that lists it, the actions that settle it, and a
    // way to read the window as the page does.
    private (HostPageActions Actions, TemplateRefresh Refresh, Func<PageSnapshot> Snapshot) Wire()
    {
        var refresh = Refresh();
        refresh.Step();
        var parts = Parts(refresh);
        var sources = new PageSnapshotSources
        {
            EditedTemplates = () => [.. refresh.Edited.Select(e => new PageTemplateEdit("ptk-580a", e.Target))],
        };
        return (parts.Actions, refresh, () => HostPageSnapshot.Build(parts.Config, new FakeClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)), parts.Tick, sources));
    }

    private (HostPageActions Actions, SchedulerConfig Config, SchedulerTick Tick) Parts(TemplateRefresh? templates)
    {
        var config = new SchedulerConfig
        {
            Project = "ptk-580a",
            JobsDir = "jobs",
            LogDir = _logs,
            Repo = _project.Repo,
            Slots = 1,
            PageStopGraceMinutes = 5,
            PollSeconds = 1,
        };
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var processes = new ActionProcesses();
        var tick = new SchedulerTick(config, clock, _log, new TickLoad(), new TickProbe(), processes, new StateFiles(_logs), new ActionTickHost());
        var actions = new HostPageActions(new PageActionParts
        {
            Config = config,
            Clock = clock,
            Log = _log,
            Files = new StateFiles(_logs),
            Tick = tick,
            Processes = processes,
            Committer = new RecordingCommitter(),
            Main = new NullMain(),
            TickNow = () => _ticks++,
            Templates = templates,
        });
        return (actions, config, tick);
    }

    private TemplateRefresh Refresh()
    {
        GitFixture.Git(_project.Repo, "fetch", "-q", "origin");
        var answers = Path.Combine(Path.GetDirectoryName(_project.Repo)!, "slots", "answers");
        var committer = new AnswersCommitter(new GitRunner(), _project.Repo, answers, "origin", "main");
        return new TemplateRefresh(new GitRunner(), _toolkit.Repo, _project.Main(), committer, "jobs",
            Path.Combine(_logs, TemplateState.FileName), _log);
    }

    private void Toolkit(string message, string readme) => _toolkit.Push(message, new Dictionary<string, string?> { [Readme] = readme });

    private void Project(string message, IDictionary<string, string?> files) => _project.Push(message, files);

    private string OnMain(string path) => GitFixture.Git(_project.Bare, "show", $"main:{path}");

    private string LastMessage() => GitFixture.Git(_project.Bare, "log", "-1", "--format=%s", "main");

    private int Commits() => int.Parse(GitFixture.Git(_project.Bare, "rev-list", "--count", "main"));

    private static IReadOnlyList<PageWaitingKind> Kinds(PageSnapshot snapshot) => [.. snapshot.Waiting.Select(w => w.Kind)];
}
