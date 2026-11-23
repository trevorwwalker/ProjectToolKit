using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.PageActions;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Web.Server;
using Scheduler.Web.Views;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// Card 1000-B: the pool's page in Host (design multi-project.md sections 4 and 6): the themes (MP-P1),
/// <see cref="PoolPageData"/> and <see cref="PoolPageActions"/> over a real pool composition (MP-P8, MP-H8)
/// and the Worker types panel's Save into the pool file (MP-P7). No agent is ever started.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[ParallelMethods]
public sealed class PoolPageTests
{
    // MP-P1: the pool's theme first, then every theme the loader offers, including each project's own page.theme.
    [Fact]
    public void MP_P1_Themes_PoolFirst_ThenSchedulerThemes_ThenEachProjectsOwn()
    {
        using var temp = new TempDir();
        var exe = Dir(temp.Path, "exe");
        Write(Path.Combine(exe, "theme.default.json"), """{ "label": "Modern", "order": 10, "css": [".m{}"] }""");
        Write(Path.Combine(Dir(exe, "themes"), "dark.json"), """{ "label": "Dark", "order": 20, "css": [".d{}"] }""");
        var poolTheme = Path.Combine(Dir(temp.Path, "pool"), "pool-theme.json");
        Write(poolTheme, """{ "label": "Pool look", "order": 1, "css": [".p{}"] }""");
        var repoA = Dir(temp.Path, "a");
        Write(Path.Combine(repoA, "workbench.json"), """{ "label": "Workbench", "order": 30, "css": [".w{}"] }""");
        var projects = new[]
        {
            new SchedulerConfig { Project = "AmigaX", Repo = repoA, PageTheme = "workbench.json" },
            new SchedulerConfig { Project = "Plain", Repo = Dir(temp.Path, "b") },
        };

        var log = new ListLog();
        var themes = PoolThemeLoader.Load(poolTheme, temp.Path, projects, Path.Combine(temp.Path, "logs"), log, exe);

        Assert.Equal("Pool look", themes.Entries[0].Label);
        Assert.Equal(["Pool look", "Modern", "Dark", "Workbench"], themes.Entries.Select(e => e.Label));
        Assert.Empty(log.Lines);
    }

    // MP-P1: a theme that is not a file only labels the page; the scheduler's own theme is the first, and it is logged.
    [Fact]
    public void MP_P1_PoolThemeNotAFile_UsesSchedulersOwn_AndSaysSo()
    {
        using var temp = new TempDir();
        var exe = Dir(temp.Path, "exe");
        Write(Path.Combine(exe, "theme.default.json"), """{ "label": "Modern", "css": [".m{}"] }""");
        var log = new ListLog();

        var themes = PoolThemeLoader.Load("dark", temp.Path, [], Path.Combine(temp.Path, "logs"), log, exe);

        Assert.Equal("Modern", themes.Entries[0].Label);
        Assert.Contains(log.Lines, l => l.Contains("the pool's theme dark is not a theme file", StringComparison.Ordinal));
    }

    // MP-P1: a project's page.theme that is gone is logged and left out; the others stay.
    [Fact]
    public void MP_P1_ProjectThemeMissing_LeftOut()
    {
        using var temp = new TempDir();
        var exe = Dir(temp.Path, "exe");
        Write(Path.Combine(exe, "theme.default.json"), """{ "label": "Modern", "css": [".m{}"] }""");
        var log = new ListLog();

        var themes = PoolThemeLoader.Load("", temp.Path, [new SchedulerConfig { Project = "A", Repo = temp.Path, PageTheme = "gone.json" }], Path.Combine(temp.Path, "logs"), log, exe);

        Assert.Single(themes.Entries);
        Assert.Contains(log.Lines, l => l.Contains("A's page.theme gone.json was not found", StringComparison.Ordinal));
    }

    // MP-P8: the pool's page data holds one page per project in the pool file's order, and each project's own data.
    [Fact]
    public void MP_P8_PoolPageData_OnePagePerProject_InFileOrder()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        pool.Runner.Run();

        var snapshot = pool.PageData.PoolSnapshot();

        Assert.Equal([rig.A, rig.B], snapshot.Projects.Select(p => p.Name));
        Assert.Equal([rig.A, rig.B], snapshot.Pages.Select(p => p.Project));
        Assert.NotNull(pool.PageData.For(rig.A));
        Assert.NotNull(pool.PageData.For(rig.B.ToUpperInvariant()));
        Assert.Null(pool.PageData.For("Nobody"));
        Assert.Equal(Path.GetFullPath(rig.PoolLogDir), Path.GetFullPath(pool.PageData.LogDirectory));
        Assert.Equal([rig.A, rig.B], pool.Actions.Projects);
    }

    // MP-H8: Pause acts on the project named, or on every project when none is; Resume the same; a stranger is refused.
    [Fact]
    public void MP_H8_PoolActions_PauseOneOrAll()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        SchedulerState StateOf(string name) => pool.Tick.Projects.Single(p => p.Name == name).Tick.State;

        Assert.True(pool.Actions.Pause(rig.A).Ok);
        Assert.Equal((SchedulerState.Paused, SchedulerState.Running), (StateOf(rig.A), StateOf(rig.B)));

        Assert.True(pool.Actions.Resume(rig.A).Ok);
        Assert.Equal(SchedulerState.Running, StateOf(rig.A));

        Assert.True(pool.Actions.Pause(null).Ok);
        Assert.Equal((SchedulerState.Paused, SchedulerState.Paused), (StateOf(rig.A), StateOf(rig.B)));

        Assert.True(pool.Actions.Drain(rig.B).Ok);
        Assert.Equal(SchedulerState.Draining, StateOf(rig.B));

        var refused = pool.Actions.Pause("Nobody");
        Assert.False(refused.Ok);
        Assert.Contains("which project?", refused.Error, StringComparison.Ordinal);
    }

    // Card 1004-C: a row's Stop asks only that project's workers to stop and pauses it; the other
    // project's worker and state are untouched, and nothing drains, so the pool does not exit.
    [Fact]
    public void Card1004_StopOneProject_OtherProjectAndSchedulerCarryOn()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        var a = pool.Tick.Projects.Single(p => p.Name == rig.A).Tick;
        var b = pool.Tick.Projects.Single(p => p.Name == rig.B).Tick;
        a.Adopt(Worker(1, "010-A-one"));
        b.Adopt(Worker(1, "020-B-two"));

        var stop = pool.Actions.Stop(rig.A);

        Assert.True(stop.Ok, stop.Error);
        Assert.Equal("stopped from the pool page", a.Running[1].StopWhy);
        Assert.NotNull(a.Running[1].StopAt);
        Assert.True(string.IsNullOrEmpty(b.Running[1].StopWhy), b.Running[1].StopWhy);
        Assert.Null(b.Running[1].StopAt);
        Assert.Equal((SchedulerState.Paused, SchedulerState.Running), (a.State, b.State));
        Assert.DoesNotContain(pool.Tick.Projects, p => p.Tick.State == SchedulerState.Draining);

        var nobody = pool.Actions.Stop("Nobody");
        Assert.False(nobody.Ok);
        Assert.Contains("which project?", nobody.Error, StringComparison.Ordinal);
    }

    // Card 1004-C: the title bar's Pause all and Resume all act on every project at once.
    [Fact]
    public void Card1004_PauseAll_ResumeAll_EveryProject()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        SchedulerState StateOf(string name) => pool.Tick.Projects.Single(p => p.Name == name).Tick.State;

        Assert.True(pool.Actions.Pause(null).Ok);
        Assert.Equal((SchedulerState.Paused, SchedulerState.Paused), (StateOf(rig.A), StateOf(rig.B)));

        Assert.True(pool.Actions.Resume(null).Ok);
        Assert.Equal((SchedulerState.Running, SchedulerState.Running), (StateOf(rig.A), StateOf(rig.B)));
    }

    private static RunningWorker Worker(int slot, string card) => new()
    {
        Slot = slot,
        Pid = 4000 + slot,
        Start = DateTimeOffset.UnixEpoch,
        Card = card,
        Category = card.Split('-')[1],
        Id = card[..5],
    };

    // MP-P8: the pool-wide routes need no project; a per-project route without one is refused; with one it reaches that project.
    [Fact]
    public void MP_P8_Routes_PoolWideNeedNoProject_PerProjectDoes()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        pool.Runner.Run();
        using var server = new PageServer(new PageServerOptions
        {
            Data = pool.PageData,
            Actions = pool.Actions,
            Renderer = new PageRenderer(),
            Feed = new LiveFeed(rig.Clock, pool.PageData),
            Pool = new PoolRouting(pool.PageData, pool.Actions, new CombinedPageRenderer()),
        });

        var page = server.Router.Handle(new PageRequest { Method = "GET", Path = "/" });
        Assert.Equal(200, page.Status);
        Assert.Contains(rig.A, page.Body, StringComparison.Ordinal);
        Assert.Contains(rig.B, page.Body, StringComparison.Ordinal);

        var which = server.Router.Handle(new PageRequest { Method = "GET", Path = "/card", QueryValues = PageRequest.ParseQuery("name=x") });
        Assert.Equal(400, which.Status);
        Assert.Contains("which project?", which.Body, StringComparison.Ordinal);

        var tick = server.Router.Handle(new PageRequest { Method = "POST", Path = "/tick" });
        Assert.True(tick.Status < 400, tick.Status + " " + tick.Body);

        var pause = server.Router.Handle(new PageRequest { Method = "POST", Path = "/pause", FormValues = PageRequest.ParseForm("project=" + rig.A) });
        Assert.True(pause.Status < 400, pause.Status + " " + pause.Body);
        Assert.Equal(SchedulerState.Paused, pool.Tick.Projects.Single(p => p.Name == rig.A).Tick.State);
        Assert.Equal(SchedulerState.Running, pool.Tick.Projects.Single(p => p.Name == rig.B).Tick.State);
    }

    // MP-P7, WT-C8: the Worker types panel's Save writes the pool file's pool section and nothing else.
    [Fact]
    public void MP_P7_SaveWorkerTypes_WritesPoolSection_ElsePoolFileKept()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        var before = JsonNode.Parse(File.ReadAllText(rig.PoolFile))!.AsObject();
        var projectsBefore = before["projects"]!.ToJsonString();
        var saved = pool.Actions.SaveWorkerTypes(3, [new PageWorkerTypeView { Name = "w", Max = 3, OriginalName = "w", Projects = [rig.A] }]);

        Assert.True(saved.Ok, saved.Error);
        var after = JsonNode.Parse(File.ReadAllText(rig.PoolFile))!.AsObject();
        Assert.Equal(3, (int)after["pool"]!["maxWorkers"]!);
        Assert.Equal(3, (int)after["pool"]!["types"]!["w"]!["max"]!);
        Assert.Equal(rig.A, (string)after["pool"]!["types"]!["w"]!["projects"]![0]!);
        Assert.Equal(projectsBefore, after["projects"]!.ToJsonString());
    }

    // MP-P7, 999-C Q2: cap 0 turns a type off; the file keeps the type with "max": 0 and the next tick takes it (MP-C7).
    [Fact]
    public void MP_P7_SaveWorkerTypes_CapZero_TurnsTheTypeOff_NextTickTakesIt()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();

        var saved = pool.Actions.SaveWorkerTypes(2, [new PageWorkerTypeView { Name = "w", Max = 0, OriginalName = "w" }]);

        Assert.True(saved.Ok, saved.Error);
        Assert.Equal(0, (int)JsonNode.Parse(File.ReadAllText(rig.PoolFile))!["pool"]!["types"]!["w"]!["max"]!);
        pool.RunTick(TickWake.Poll);
        Assert.Equal(0, pool.Tick.Types.Types.Single(t => t.Name == "w").Max);
    }

    // MP-P7: a total of nothing, a bad address or a repeated name is refused and the file is left as it was.
    [Fact]
    public void MP_P7_SaveWorkerTypes_BadInput_Refused_FileUntouched()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        var text = File.ReadAllText(rig.PoolFile);

        Assert.False(pool.Actions.SaveWorkerTypes(0, [new PageWorkerTypeView { Name = "w", Max = 1 }]).Ok);
        Assert.False(pool.Actions.SaveWorkerTypes(1, [new PageWorkerTypeView { Name = "w", Max = 1, Endpoint = "ftp://x" }]).Ok);
        Assert.False(pool.Actions.SaveWorkerTypes(1, [new PageWorkerTypeView { Name = "w", Max = 1 }, new PageWorkerTypeView { Name = "W", Max = 1 }]).Ok);
        Assert.False(pool.Actions.SaveWorkerTypes(1, [new PageWorkerTypeView { Name = "12", Max = 1 }]).Ok);
        Assert.Equal(text, File.ReadAllText(rig.PoolFile));
    }


    // Request 011 (1002-B): a Click wake through the pool makes no git fetch in any project; a poll does.
    [Fact]
    public void Wake_Click_ThroughThePool_MakesNoFetch_PollDoes()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();
        var fetched = pool.Projects.Select(p => Path.Combine(p.Config.Repo, ".git", "FETCH_HEAD")).ToList();
        DateTime[] Stamps() => [.. fetched.Select(f => File.Exists(f) ? File.GetLastWriteTimeUtc(f) : DateTime.MinValue)];
        // 1124-B: the poll's fetch runs on each repository's background fetcher; the tick does not wait for it
        void Run(TickWake wake)
        {
            pool.RunTick(wake);
            Assert.All(pool.Projects, p => Assert.True(Scheduler.Host.Git.BackgroundFetch.For(p.Model).Running.Wait(TimeSpan.FromSeconds(30))));
        }

        Run(TickWake.Poll);   // the first read always fetches
        var first = Stamps();
        Assert.All(first, t => Assert.NotEqual(DateTime.MinValue, t));

        Thread.Sleep(1100);
        Run(TickWake.Click);
        Assert.Equal(first, Stamps());

        Run(TickWake.Poll);
        Assert.All(Stamps().Zip(first), p => Assert.True(p.First > p.Second));
    }
    private static string Dir(string parent, string name) => Directory.CreateDirectory(Path.Combine(parent, name)).FullName;

    private static void Write(string path, string text) => File.WriteAllText(path, text);
}
