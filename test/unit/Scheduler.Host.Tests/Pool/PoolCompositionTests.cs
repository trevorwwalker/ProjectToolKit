// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Config;
using Scheduler.Host.Pool;
using Scheduler.Host.Processes;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.Tick;
using Scheduler.Web.Views;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// Card 998-B: the pool in Host (design multi-project.md sections 1 and 3): the pool file (MP-C1 to
/// MP-C7), <c>-Pool</c> (MP-C9) and <see cref="PoolComposition"/> over two temporary repositories
/// in a dry run (MP-H1 to MP-H8, MP-19, MP-22). No agent is ever started.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[ParallelMethods]
public sealed class PoolCompositionTests
{
    // MP-C1: the PC's own file under %LOCALAPPDATA%, and the pool's own log folder beside it (MP-C3).
    [Fact]
    public void MP_C1_DefaultPoolFile_AndLogDir_UnderLocalAppData()
    {
        Assert.Equal(@"C:\L\ProjectToolkit\pool.json", PoolConfigFile.DefaultPath(@"C:\L"));
        Assert.Equal(@"C:\L\ProjectToolkit\pool", PoolConfigFile.LogDirOf(PoolConfig.Parse("{}"), @"C:\L"));
        Assert.Equal(@"D:\pool", PoolConfigFile.LogDirOf(PoolConfig.Parse("""{ "logDir": "D:\\pool" }"""), @"C:\L"));
    }

    // MP-C2: an entry with no name or repo is skipped, a repeated name too (case-insensitively), each logged.
    [Fact]
    public void MP_C2_ProjectEntries_SkipNamelessAndRepeated()
    {
        var config = PoolConfig.Parse("""
            { "projects": [
                { "name": "A", "repo": "C:/a", "weight": 70 },
                { "repo": "C:/x" },
                { "name": "NoRepo" },
                { "name": "a", "repo": "C:/a2", "weight": 5 },
                { "name": "B", "repo": "C:/b", "weight": 30 } ] }
            """);

        Assert.Equal(["A", "B"], config.Projects.Select(p => p.Name));
        Assert.Equal("C:/a", config.Projects[0].Repo);
        Assert.Equal(3, config.Skipped.Count);
        Assert.Contains(config.Skipped, l => l.Contains("project 2 is skipped: it has no name", StringComparison.Ordinal));
        Assert.Contains(config.Skipped, l => l.Contains("project 3 is skipped: it has no repo", StringComparison.Ordinal));
        Assert.Contains(config.Skipped, l => l.Contains("project a is listed twice", StringComparison.Ordinal));
    }

    // MP-C2, 300-A Q5: a missing weight is the smallest other.
    [Fact]
    public void MP_C2_MissingWeight_SmallestOther()
    {
        var config = PoolConfig.Parse("""
            { "projects": [
                { "name": "A", "repo": "C:/a", "weight": 70 },
                { "name": "B", "repo": "C:/b", "weight": 20 },
                { "name": "C", "repo": "C:/c" } ] }
            """);

        Assert.Equal([70, 20, 20], config.Projects.Select(p => p.Weight));
    }

    // MP-C2: the name must be the project's own scheduler.json project; a mismatch is logged and skipped.
    [Fact]
    public void MP_C2_NameMismatch_ProjectSkipped()
    {
        using var rig = new PoolRig(mismatchA: true);
        using var pool = rig.Compose();

        Assert.Equal([rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Contains(rig.Log.Lines, l => l.Contains($"project {rig.A} is skipped: its scheduler.json names the project Other", StringComparison.Ordinal));
    }

    // MP-C3: port, listen, remote control, theme, logDir and about are top-level; their defaults.
    [Fact]
    public void MP_C3_PageSettings_TopLevel()
    {
        var given = PoolConfig.Parse("""
            { "about": "the pool", "port": 9001, "listen": "lan", "remoteControl": true, "theme": "dark", "logDir": "D:/p",
              "projects": [ { "name": "A", "repo": "C:/a" } ] }
            """);
        var defaults = PoolConfig.Parse("""{ "projects": [ { "name": "A", "repo": "C:/a" } ] }""");

        Assert.Equal((9001, "lan", true, "dark", "D:/p", "the pool"), (given.Port, given.Listen, given.RemoteControl, given.Theme, given.LogDir, given.About));
        Assert.Equal((PoolConfig.DefaultPort, "localhost", false, "", ""), (defaults.Port, defaults.Listen, defaults.RemoteControl, defaults.Theme, defaults.LogDir));
    }

    // MP-C4, WT-C8: a pooled project's ticks use the pool file's types; its own workers.json is not read.
    [Fact]
    public void MP_C4_PoolTypes_ProjectsWorkersJsonNotRead()
    {
        using var rig = new PoolRig(workersJsonInA: true);
        using var pool = rig.Compose();

        foreach (var p in pool.Tick.Projects)
        {
            Assert.Equal(["w"], p.Tick.Types.Types.Select(t => t.Name));
        }

        Assert.Contains(rig.Log.Lines, l => l.StartsWith($"{rig.A}: workers.json is not read in a pool", StringComparison.Ordinal));
    }

    // MP-C7: re-read when the text changes; invalid JSON or no valid project keeps the previous settings.
    [Fact]
    public void MP_C7_PoolFileChanged_ReReadAndInvalid_KeepsPrevious()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "pool.json");
        var log = new ListLog();
        var file = new PoolConfigFile(path, log);
        File.WriteAllText(path, """{ "projects": [ { "name": "A", "repo": "C:/a", "weight": 1 } ] }""");

        Assert.True(file.Read());
        Assert.False(file.Read()); // unchanged

        File.WriteAllText(path, "{ not json");
        Assert.False(file.Read());
        Assert.Equal(1, file.Current!.Projects[0].Weight);
        Assert.Contains(log.Lines, l => l.Contains("is not valid JSON; the previous settings are kept", StringComparison.Ordinal));

        File.WriteAllText(path, """{ "projects": [ { "name": "" } ] }""");
        Assert.False(file.Read());
        Assert.Contains(log.Lines, l => l.Contains("names no valid project; the previous settings are kept", StringComparison.Ordinal));

        File.WriteAllText(path, """{ "port": 9100, "projects": [ { "name": "A", "repo": "C:/a", "weight": 7 } ] }""");
        Assert.True(file.Read());
        Assert.Equal(7, file.Current!.Projects[0].Weight);
        Assert.Contains(log.Lines, l => l.Contains("take effect at the next start", StringComparison.Ordinal));
    }

    // MP-C1, WT-C8: the Worker types panel writes the pool section back in place, keeping the rest.
    [Fact]
    public void MP_C7_WritePool_ReplacesOnlyThePoolSection()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "pool.json");
        File.WriteAllText(path, """{ "port": 9100, "projects": [ { "name": "A", "repo": "C:/a" } ], "pool": { "maxWorkers": 1 } }""");
        var file = new PoolConfigFile(path, new ListLog());

        Assert.True(file.WritePool("""{ "maxWorkers": 3, "types": { "w": { "max": 3 } } }"""));
        Assert.False(file.WritePool("not json"));

        Assert.True(file.Read());
        Assert.Equal(9100, file.Current!.Port);
        Assert.Equal(["A"], file.Current.Projects.Select(p => p.Name));
        Assert.Equal(3, file.Current.Pool!.MaxWorkers);
    }

    // MP-C7: a project added to the file joins at the next tick; one removed with no workers is let go.
    [Fact]
    public void MP_C7_ProjectAddedAndRemoved_TakesEffectNextTick()
    {
        using var rig = new PoolRig(onlyA: true);
        using var pool = rig.Compose();
        Assert.Equal([rig.A], pool.Tick.Projects.Select(p => p.Name));

        rig.WritePoolFile(onlyB: true);
        pool.Runner.Run();
        Assert.True(pool.WaitForJoins(TimeSpan.FromSeconds(60))); // 1015-B: the join started at the tick runs on its own task

        Assert.Equal([rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Empty(pool.Tick.Leaving);
        Assert.Equal([rig.B], pool.Projects.Select(p => p.Config.Project));
    }

    // MP-C9: -Pool with an optional file; no repository argument.
    [Fact]
    public void MP_C9_PoolSwitch_Parsed()
    {
        var bare = LaunchArguments.Parse(["-Pool", "-DryRun"]);
        var named = LaunchArguments.Parse(["-pool", @"C:\p.json", "-Slots", "2"]);

        Assert.True(bare.Pool);
        Assert.Equal("", bare.PoolFile);
        Assert.Equal(["-DryRun"], bare.Rest);
        Assert.Equal(@"C:\p.json", named.PoolFile);
        Assert.Equal(["-Slots", "2"], named.Rest);
        Assert.False(LaunchArguments.Parse([@"C:\repo"]).Pool);

        var error = new StringWriter();
        Assert.Equal(2, PoolProgram.Run("", new CommandLineOptions { Repo = @"C:\repo" }, [], TextWriter.Null, error));
        Assert.Contains("-Pool takes no repository", error.ToString(), StringComparison.Ordinal);
    }

    // MP-H1: a project whose own scheduler holds its mutex is left out, noted, and tried again each minute.
    [Fact]
    public void MP_H1_ProjectMutexTaken_ProjectLeftOut_ThenJoins()
    {
        using var rig = new PoolRig();
        using var held = new MutexHolder(rig.A);
        using var pool = rig.Compose();

        Assert.Equal([rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Equal(PoolComposition.OwnSchedulerRunning, pool.LeftOut[rig.A]);

        held.Release();
        rig.Clock.Skew += PoolComposition.RetryEvery;
        pool.Runner.Run();
        Assert.True(pool.WaitForJoins(TimeSpan.FromSeconds(60))); // 1015-B: the join started at the tick runs on its own task

        Assert.Equal([rig.A, rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Empty(pool.LeftOut);
    }

    // 1198-B: a project left out shows on the pool page's data with its reason, and as a row in the Projects window.
    [Fact]
    public void P1198B_LeftOutProject_InPageData_AndShownAsRow()
    {
        using var rig = new PoolRig();
        using var held = new MutexHolder(rig.A);
        using var pool = rig.Compose();
        pool.Runner.Run();

        var snapshot = pool.PageData.PoolSnapshot();

        var left = Assert.Single(snapshot.LeftOut);
        Assert.Equal(rig.A, left.Name);
        Assert.Equal(PoolComposition.OwnSchedulerRunning, left.Why);
        var html = new CombinedPageRenderer().Render(snapshot, remote: false, controlsAllowed: true, "workers");
        Assert.Contains("data-leftout='1'", html, StringComparison.Ordinal);
        Assert.Contains("not in the pool: " + PoolComposition.OwnSchedulerRunning, html, StringComparison.Ordinal);
    }

    // 1198-B: each project's repository folder reaches the pool page's data, for the chip's hover.
    [Fact]
    public void P1198B_ProjectRepo_ReachesPageData()
    {
        using var rig = new PoolRig();
        using var pool = rig.Compose();
        pool.Runner.Run();

        var snapshot = pool.PageData.PoolSnapshot();

        Assert.All(snapshot.Projects, p => Assert.NotEqual("", p.Repo));
        Assert.True(rig.IsA(snapshot.Projects.Single(p => p.Name == rig.A).Repo));
    }

    // MP-H3, MP-22, MP-19: the two-repository dry run: each project's card is "started" with a line
    // naming its project, nothing runs; state files stay in each project's logDir, the pool's own in its.
    [Fact]
    public void MP_22_TwoRepositories_DryRun_NamesEachProject()
    {
        using var rig = new PoolRig();
        using var pool = rig.Compose();

        Assert.All(pool.Projects, p => Assert.Equal(2, p.Config.Slots)); // MP-19: the pool's maxWorkers
        pool.Runner.Run();

        Assert.Contains(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.A} w in slot 1 <- {TickRig.CardName}", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.B} w in slot 1 <- {TickRig.CardName}", StringComparison.Ordinal));
        Assert.All(pool.Tick.Projects, p => Assert.Empty(p.Tick.Busy()));

        // MP-H3: per-project status.txt and scheduler.log in its own logDir; the pool's status in the pool's.
        Assert.True(File.Exists(Path.Combine(rig.LogDirA, "status.txt")));
        Assert.True(File.Exists(Path.Combine(rig.LogDirB, "status.txt")));
        Assert.True(File.Exists(Path.Combine(rig.LogDirA, "scheduler.log")));
        var status = File.ReadAllText(Path.Combine(rig.PoolLogDir, "status.txt"));
        Assert.Contains($"{rig.A}: running, weight 70%", status, StringComparison.Ordinal);
        Assert.Contains($"{rig.B}: running, weight 30%", status, StringComparison.Ordinal);
        Assert.Contains("type w:", status, StringComparison.Ordinal);
        // MP-H3: the pool log carries each project's lines with its name in front.
        Assert.Contains(rig.Log.Lines, l => l.StartsWith($"{rig.A}: ", StringComparison.Ordinal));
    }

    // MP-H3, 1108-B: while the last process-start check was slow (1078-B), its warning ends the first line;
    // when a later check is back above the limit the writer passes null and the suffix goes. Nothing else changes.
    [Fact]
    public void MP_H3_StatusText_TheSlowStartWarningIsOnTheFirstLine_AndGoesWhenRecovered()
    {
        var now = new DateTimeOffset(2026, 11, 22, 14, 0, 0, TimeSpan.Zero);
        var pool = new PoolPageSnapshot
        {
            Built = now,
            MaxWorkers = 3,
            Running = 1,
            Projects = [new PoolProjectView { Name = "p", Weight = 100, WeightShare = 1, UsedShare = 0, Running = 1, Queued = 2 }],
            Types = [new PoolTypeView { Name = "w", Max = 3, RunningByProject = new Dictionary<string, int> { ["p"] = 1 } }],
        };
        var leftOut = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["gone"] = "its own scheduler is running" };

        var slow = PoolComposition.StatusText(pool, leftOut, now, "process starts slow: 20/s (under 25/s)");
        var ok = PoolComposition.StatusText(pool, leftOut, now, null);

        Assert.StartsWith("pool at 2026-11-22 14:00:00: 1 of 3 worker(s) running - process starts slow: 20/s (under 25/s)\n", slow, StringComparison.Ordinal);
        Assert.StartsWith("pool at 2026-11-22 14:00:00: 1 of 3 worker(s) running\n", ok, StringComparison.Ordinal);
        // the suffix is the only difference in the file
        Assert.Equal(ok.Split('\n')[1..], slow.Split('\n')[1..]);
    }

    // MP-H6: each project's running.json is adopted into its own tick, and counts toward the pool.
    [Fact]
    public void MP_H6_AdoptionPerProject()
    {
        using var rig = new PoolRig();
        using var worker = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var running = new RunningWorker
            {
                Slot = 1,
                Pid = worker.Id,
                Start = new DateTimeOffset(worker.StartTime),
                Card = TickRig.CardName,
                Category = "B",
                Id = "010",
                Type = "w",
            };
            Directory.CreateDirectory(rig.LogDirA);
            File.WriteAllText(Path.Combine(rig.LogDirA, "running.json"), RunningJson.Write([running]));
            using var pool = rig.Compose();
            pool.Runner.Run();

            var a = pool.Tick.Projects.Single(p => p.Name == rig.A).Tick;
            var b = pool.Tick.Projects.Single(p => p.Name == rig.B).Tick;
            Assert.Equal([worker.Id], a.Busy().Select(w => w.Pid));
            Assert.DoesNotContain(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.A}", StringComparison.Ordinal));
            Assert.Contains(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.B}", StringComparison.Ordinal));
            Assert.Equal(1, pool.Tick.Snapshot().Running);
        }
        finally
        {
            worker.Kill(entireProcessTree: true);
        }
    }

    // MP-H8 (MP-12): pausing one project stops only its starts; the other runs on.
    [Fact]
    public void MP_H8_PauseOneProject_OthersRun()
    {
        using var rig = new PoolRig();
        using var pool = rig.Compose();
        pool.Tick.Projects.Single(p => p.Name == rig.A).Tick.State = SchedulerState.Paused;

        pool.Runner.Run();

        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.A}", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.B} w", StringComparison.Ordinal));
    }

    // Two temporary repositories, each with one ready card, and a pool file naming both (or one).
    internal sealed class PoolRig : IDisposable
    {
        private readonly TempDir _temp = new();
        private readonly GitFixture _a;
        private readonly GitFixture _b;
        private readonly bool _mismatchA;

        public PoolRig(bool mismatchA = false, bool workersJsonInA = false, bool onlyA = false)
        {
            _mismatchA = mismatchA;
            var tag = Guid.NewGuid().ToString("N")[..8];
            A = "PoolA" + tag;
            B = "PoolB" + tag;
            Name = "pooltest-" + tag;
            _a = Repo(workersJsonInA ? """{ "maxWorkers": 1, "types": { "local": { "max": 1 } } }""" : null);
            _b = Repo(null);
            PoolLogDir = Path.Combine(_temp.Path, "pool-logs");
            LogDirA = Path.Combine(_temp.Path, "logs-a");
            LogDirB = Path.Combine(_temp.Path, "logs-b");
            PoolFile = Path.Combine(_temp.Path, "pool.json");
            WritePoolFile(onlyA: onlyA);
        }

        public string A { get; }

        public string B { get; }

        public string Name { get; }

        public string PoolFile { get; }

        public string PoolLogDir { get; }

        public string LogDirA { get; }

        public string LogDirB { get; }

        public SkewClock Clock { get; } = new();

        public ListLog Log { get; } = new();

        public void WritePoolFile(bool onlyA = false, bool onlyB = false, string workspacesA = "", int maxWorkers = 2)
        {
            string Entry(string name, string repo, int weight, string extra = "") =>
                $$"""{ "name": "{{name}}", "repo": "{{repo.Replace('\\', '/')}}", "weight": {{weight}}{{extra}} }""";
            var entries = new List<string>();
            if (!onlyB)
            {
                entries.Add(Entry(A, _a.Repo, 70, workspacesA.Length == 0 ? "" : $", \"workspaces\": {workspacesA}"));
            }

            if (!onlyA)
            {
                entries.Add(Entry(B, _b.Repo, 30));
            }

            File.WriteAllText(PoolFile, $$"""
                { "logDir": "{{PoolLogDir.Replace('\\', '/')}}",
                  "projects": [ {{string.Join(", ", entries)}} ],
                  "pool": { "maxWorkers": {{maxWorkers}}, "types": { "w": { "max": {{maxWorkers}} } } } }
                """);
        }

        // 1015-B: the projects join on tasks of their own; by default the rig waits for them, as the tests of a
        // composed pool expect. wait: false leaves them joining; load stands in front of the rig's own loader.
        public PoolComposition Compose(bool wait = true, Func<string, Func<string, ConfigLoadResult>, ConfigLoadResult>? load = null, bool dryRun = true,
            TimeSpan? startHold = null)
        {
            var file = new PoolConfigFile(PoolFile, Log);
            Assert.True(file.Read());
            var pool = PoolComposition.Create(new PoolStart
            {
                File = file,
                Options = new CommandLineOptions { DryRun = dryRun },
                AnyPort = true,   // 1307-B: the page binds a port the system gives it; PagePort reads it back
                Clock = Clock,
                Log = Log,
                LocalAppData = _temp.Path,
                Name = Name,
                MutexWait = TimeSpan.FromSeconds(5),
                LoadProject = load is null ? Load : repo => load(repo, Load),
                StartHold = startHold ?? PoolStart.DefaultStartHold,
            });
            if (wait)
            {
                Assert.True(pool.WaitForJoins(TimeSpan.FromSeconds(60)));
            }

            return pool;
        }

        public bool IsA(string repo) => Path.GetFullPath(repo) == Path.GetFullPath(_a.Repo);

        // 1307-B: the port the pool's page was given, once it listens (a port chosen here and let go could be taken
        // before the page bound it). A run that failed first fails here with its own error; the limit is a hang cap only.
        public static int PagePort(PoolComposition pool, Func<Exception?> failed)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (pool.PagePort == 0)
            {
                Assert.Null(failed());
                Assert.True(watch.Elapsed < TimeSpan.FromMinutes(10), "hang: the pool's page never started listening in 10 minutes");
                Thread.Sleep(20);
            }

            return pool.PagePort;
        }

        public void Dispose()
        {
            _a.Dispose();
            _b.Dispose();
            _temp.Dispose();
        }

        private ConfigLoadResult Load(string repo)
        {
            var isA = Path.GetFullPath(repo) == Path.GetFullPath(_a.Repo);
            var config = new SchedulerConfigBuilder
            {
                Repo = isA ? _a.Repo : _b.Repo,
                Project = isA ? (_mismatchA ? "Other" : A) : B,
                WorkspaceRoot = Path.Combine(_temp.Path, isA ? "ws-a" : "ws-b"),
                LogDir = isA ? LogDirA : LogDirB,
                WorkerCli = Path.Combine(_temp.Path, "no-agent.cmd"),
                Port = TickRig.FreePort(),
                DryRun = true,
                Slots = 1,
                SlotsPinned = false,
            }.Build();
            return new ConfigLoadResult { Ok = true, Repo = config.Repo, Config = config, ConfigText = "{}" };
        }

        private static GitFixture Repo(string? workersJson)
        {
            var fx = new GitFixture();
            var seed = new Dictionary<string, string?> { [$"jobs/queue/{TickRig.CardName}.md"] = TickRig.CardText };
            if (workersJson is not null)
            {
                seed["jobs/workers.json"] = workersJson;
            }

            fx.Push("seed", seed);
            GitFixture.Git(fx.Repo, "pull", "-q", "origin", "main");
            return fx;
        }
    }

    // MP-H1: another scheduler of the project, holding its mutex on a thread of its own.
    private sealed class MutexHolder : IDisposable
    {
        private readonly ManualResetEventSlim _taken = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;

        public MutexHolder(string project)
        {
            _thread = new Thread(() =>
            {
                using var instance = SingleInstance.Acquire(project, TimeSpan.FromSeconds(5));
                _taken.Set();
                _release.Wait();
            });
            _thread.Start();
            _taken.Wait(TimeSpan.FromSeconds(10));
        }

        public void Release()
        {
            _release.Set();
            _thread.Join(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            Release();
            _taken.Dispose();
            _release.Dispose();
        }
    }
}
