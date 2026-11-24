using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Core.Scopes;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Slots;

namespace Scheduler.Host.Tests.Compat;

/// <summary>
/// Card 954-B: what scheduler-next does with AmigaX's own jobs files (copies of C:\Projects\AmigaX
/// docs/jobs, taken 2026-11-19, in Compat/AmigaX). The tests that pass say what already works; the
/// ones named Gap_ pin a difference the planner has to card, so a later fix shows up as a failing test
/// to update.
/// </summary>
public sealed class AmigaXCompatTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Compat", "AmigaX", name));

    private static SchedulerConfig Config() =>
        SchedulerConfigLoader.Load(Fixture("scheduler.json"), @"C:\Projects\AmigaX", @"C:\Temp\local", defaultJobsDir: "docs/jobs");

    private static CategoriesFile Categories()
    {
        var registry = new CategoryRegistry(new ListLog(), new NoFiles(), "jobs/lanes.json");
        registry.Read(Fixture("lanes.json"), []);
        return registry.Current!;
    }

    [Fact]
    public void ConfigLoad_ReadsEveryAmigaXSetting()
    {
        var c = Config();

        Assert.Equal("AmigaX", c.Project);
        Assert.Equal("AMIGAX_JOBS", c.JobsEnvVar);
        Assert.Equal("Hidden", c.WorkerWindow);
        Assert.Equal(240, c.MaxMinutes);
        Assert.Equal("docs/jobs/scheduler-theme.json", c.PageTheme);
        Assert.Contains("&& py testplan/tools/runtests.py", c.HealthCommand, StringComparison.Ordinal);
        Assert.StartsWith("Builds and tests:", c.PromptExtra, StringComparison.Ordinal);
        Assert.True(c.Agents.ContainsKey("claude"));
    }

    [Fact]
    public void WorkersMigration_GivesTheSixSparkWorkersAndTheTwoClaudeOnes()
    {
        var c = Config();
        var pool = WorkerTypesJson.FromWorkersJson(c, Fixture("workers.json"));

        Assert.True(pool.Migrated);
        Assert.Equal(8, pool.MaxWorkers);
        Assert.Equal(3, pool.Types.Count);
        Assert.Equal([6, 1, 1], pool.Types.Select(t => t.Max));
        Assert.Equal(["claude-sonnet-5-5", "claude-opus-5-5"], pool.Types.Skip(1).Select(t => t.Model));
        Assert.Equal(pool.Types[0].Name, pool.DefaultType); // 1146-A: the premium flags made the first type the default
    }

    [Fact]
    public void Categories_ReadWithGlobsAndTheSharedNote()
    {
        var categories = Categories();

        Assert.Equal(12, categories.Categories.Count);
        Assert.Contains("vm/68000.*", categories.Categories["A"].Owns);
        Assert.Contains(categories.AddOnly, p => p.StartsWith("testplan/state-fixtures/", StringComparison.Ordinal));
    }

    [Fact]
    public void ALocksGlobIsReadAndBlocksTheFilesItMatches()
    {
        // AmigaX cards write "Locks: vm/MemoryMap.*". Card 961-A takes the glob: the scope parser
        // accepts it and the overlap check sees the files it matches.
        var scope = ScopeLine.ParseValue("vm/MemoryMap.*, vm/Gary.*, testplan/memory-unittest.asm");

        Assert.NotNull(scope);
        Assert.Null(scope!.Problem);
        Assert.Equal(["vm/MemoryMap.*", "vm/Gary.*", "testplan/memory-unittest.asm"], scope.Paths);
        Assert.Equal("vm/MemoryMap.cpp", ScopeOverlap.Shared(scope, new Scope(["vm/MemoryMap.cpp"], [])));
        Assert.Null(ScopeOverlap.Shared(scope, new Scope(["vm/Ram.cpp"], [])));
    }

    [Fact]
    public void Gap_ALocksLineWithANoteInParenthesesIsRead()
    {
        // 593-E: "Locks: none (investigation; a fix takes the lock it needs and says so)".
        Assert.Null(ScopeLine.ParseValue("none (investigation; a fix takes the lock it needs and says so)"));
    }

    [Fact]
    public void AmigaXThemeFontIsAlsoLookedForFromTheRepositoryRoot()
    {
        using var repo = new TempDir();
        var jobs = Path.Combine(repo.Path, "jobs");
        var fonts = Path.Combine(repo.Path, "tools", "scheduler", "fonts");
        Directory.CreateDirectory(jobs);
        Directory.CreateDirectory(fonts);
        File.WriteAllText(Path.Combine(jobs, "scheduler-theme.json"), Fixture("scheduler-theme.json"));
        File.WriteAllBytes(Path.Combine(fonts, "Topaz_a500_v1.0.ttf"), [0, 1, 0, 0]);
        var config = Config() with { Repo = repo.Path, PageTheme = "jobs/scheduler-theme.json" };

        var themes = PageThemeLoader.Load(config, new ListLog());

        Assert.EndsWith("Topaz_a500_v1.0.ttf", themes.FontFile, StringComparison.Ordinal);
        Assert.True(themes.FontExists);
    }

    [Fact]
    public void Categories_OfAmigaXDoNotOverlapEachOther()
    {
        var categories = Categories();

        Assert.Empty(CategoryOverlaps.Find(categories));
        Assert.Null(ScopeOverlap.Shared(AreaScopeOf(categories, "A"), AreaScopeOf(categories, "E"), categories.AddOnly));
        Assert.NotNull(ScopeOverlap.Shared(AreaScopeOf(categories, "A"), new Scope(["vm/"], []), categories.AddOnly));

        // 961-A: P, Q and R split docs/agents/scouting/ by letter and share nothing; R owns s* while
        // G owns the survey* that R, Q and P all except.
        Assert.Null(ScopeOverlap.Shared(AreaScopeOf(categories, "P"), AreaScopeOf(categories, "Q"), categories.AddOnly));
        Assert.Null(ScopeOverlap.Shared(AreaScopeOf(categories, "R"), AreaScopeOf(categories, "G"), categories.AddOnly));
        Assert.NotNull(ScopeOverlap.Shared(
            AreaScopeOf(categories, "R"), new Scope(["docs/agents/scouting/sprocket-notes.md"], []), categories.AddOnly));
        Assert.Null(ScopeOverlap.Shared(
            AreaScopeOf(categories, "R"), new Scope(["docs/agents/scouting/survey-copper.md"], []), categories.AddOnly));
    }

    [Fact]
    public void AGlobAreaOverlapsAConcreteFileItMatches()
    {
        // Category A owns vm/68000.*; a card that names the file vm/68000.cpp clashes with it (961-A),
        // and a card on a file the glob does not match does not.
        var categories = Categories();

        Assert.Equal(
            "vm/68000.cpp",
            ScopeOverlap.Shared(AreaScopeOf(categories, "A"), new Scope(["vm/68000.cpp"], []), categories.AddOnly));
        Assert.Null(
            ScopeOverlap.Shared(AreaScopeOf(categories, "A"), new Scope(["vm/Agnus.cpp"], []), categories.AddOnly));
    }

    private static Scope AreaScopeOf(CategoriesFile categories, string letter) => new(categories.Categories[letter].Owns, categories.Categories[letter].Except);

    private sealed class NoFiles : IStateFiles
    {
        public string? Read(StateFile file) => null;

        public void Write(StateFile file, string text)
        {
        }
    }

    private sealed class ListLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }
}
