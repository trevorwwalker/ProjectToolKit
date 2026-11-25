// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using GitFx = Scheduler.Host.Tests.Git.GitFixture;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 920-B: one health check at a time in the health worktree. A check left by the scheduler that ran
/// before this one (a tray Restart ends only the scheduler) is ended, never adopted; a second start
/// ends the first's tree before building; a gate that fails only with MSB3026/MSB3027 file-lock
/// errors is retried once and then reported as "could not run" rather than red.
/// </summary>
[ParallelMethods]
public class HealthSupersedeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    // HLTH-3 + 920-B: a start ends the whole tree of the check recorded by the previous scheduler
    // (its job object: dotnet, MSBuild nodes, testhost) and waits for it before checking.
    [Fact]
    public void R_HLTH_StartEndsTheTreeThePreviousSchedulerLeft()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out var logDir);
        var start = T0.AddMinutes(-3);
        processes.MakeAlive(777, start);
        Directory.CreateDirectory(logDir);
        File.WriteAllText(
            Path.Combine(logDir, "health-running.json"),
            """{"Pid":777,"Job":"ProjectToolkit-proj-health-0-777","Start":"2026-11-19T09:00:00+00:00","Sha":"abc1234","Log":"C:\\logs\\health.log"}""");

        var runner = Runner(config, processes, log);
        Assert.NotNull(runner.Start(sha, "exit 0"));

        Assert.Contains(processes.Ended, e => e.Pid == 777 && e.Job == "ProjectToolkit-proj-health-0-777");
        Assert.False(processes.IsAlive(777, start));                       // waited for the tree to go
        var now = Assert.Single(processes.Started);                          // only the new check runs
        var record = File.ReadAllText(Path.Combine(logDir, "health-running.json"));
        Assert.Contains($"\"Pid\":{now.Pid}", record);                       // the record moved to the new check
    }

    // 920-B: a second check never runs beside the first in the same workspace.
    [Fact]
    public void R_HLTH_SecondStartEndsTheFirstBeforeBuilding()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out _);
        var runner = Runner(config, processes, log);

        Assert.NotNull(runner.Start(sha, "exit 0"));
        var first = Assert.Single(processes.Started);
        Assert.True(processes.IsAlive(first.Pid, first.Start));

        Assert.NotNull(runner.Start(sha, "exit 0"));

        Assert.Contains(processes.Ended, e => e.Pid == first.Pid && e.Job == $"ProjectToolkit-proj-health-0-{first.Pid}");
        Assert.False(processes.IsAlive(first.Pid, first.Start));
        Assert.Equal(2, processes.Started.Count);
        Assert.NotEqual(first.Pid, processes.Started[1].Pid);
    }

    // 920-B: an MSB3026/MSB3027 file-lock failure is retried once after the locker's tree is ended.
    [Fact]
    public void R_HLTH_LockOnlyFailureIsRetriedAfterEndingTheLocker()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out _);
        processes.ExitCodeValue = 1;
        var runner = Runner(config, processes, log);

        var logPath = runner.Start(sha, "dotnet build")!;
        File.WriteAllText(logPath, "  MSB3027: Could not copy \"a.dll\". Exceeded retry count. The file is locked by: \"testhost.exe (4321)\"\n");
        processes.Stop(1001);

        Assert.True(runner.IsRunning());                       // it started again rather than finishing
        Assert.Equal(2, processes.Started.Count);
        Assert.Contains(processes.Ended, e => e.Pid == 1001);
        Assert.Contains(log.Lines, l => l.Contains("locked files") && l.Contains("started it again"));
        Assert.Contains("\"a.dll\"", File.ReadAllText(logPath));  // the first attempt's output is kept
    }

    // 920-B: a check that still fails with only file locks is "could not run", not red.
    [Fact]
    public void R_HLTH_LockOnlyFailureAfterRetryIsNotRed()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out _);
        processes.ExitCodeValue = 1;
        var runner = Runner(config, processes, log);

        var logPath = runner.Start(sha, "dotnet build")!;
        File.WriteAllText(logPath, "  MSB3026: The file \"a.dll\" is locked by \"testhost.exe (4321)\". Waiting for 10 retries...\n");
        processes.Stop(1001);
        Assert.True(runner.IsRunning());
        processes.Stop(1002);

        Assert.False(runner.IsRunning());
        Assert.Equal(2, processes.Started.Count);              // it did not retry a second time
        Assert.Equal(0, runner.ExitCode());                     // not red: dispatching is left alone
        Assert.Equal("testhost.exe (4321)", runner.CouldNotRun);
        Assert.Contains(log.Lines, l => l.Contains("could not run") && l.Contains("testhost.exe (4321)"));
    }

    // 920-B: a check that failed for a real reason is still red, and is not retried.
    [Fact]
    public void R_HLTH_ARealBuildFailureStaysRed()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out _);
        processes.ExitCodeValue = 1;
        var runner = Runner(config, processes, log);

        var logPath = runner.Start(sha, "dotnet build")!;
        File.WriteAllText(logPath, "  MSB3027: The file is locked by \"testhost.exe (4321)\"\nProgram.cs(10,5): error CS1002: ; expected\n");
        processes.Stop(1001);

        Assert.False(runner.IsRunning());
        Assert.Single(processes.Started);
        Assert.Equal(1, runner.ExitCode());
        Assert.Equal("", runner.CouldNotRun);
    }

    // 920-B: a scheduler already paused-red is not un-paused by a sha whose check could not run.
    [Fact]
    public void R_HLTH_CouldNotRunDoesNotUnPauseAPausedRedScheduler()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out _);
        processes.ExitCodeValue = 1;
        var runner = new HealthWorktreeRunner(config, new GitRunner(), processes,
            new FakeClock(T0), log, cores: 8, () => SchedulerState.PausedRed);

        var logPath = runner.Start(sha, "dotnet build")!;
        File.WriteAllText(logPath, "  MSB3026: The file \"a.dll\" is locked by \"testhost.exe (4321)\"\n");
        processes.Stop(1001);
        Assert.True(runner.IsRunning());
        processes.Stop(1002);

        Assert.Equal(1, runner.ExitCode());                     // its verdict stands
        Assert.Equal("testhost.exe (4321)", runner.CouldNotRun);
    }

    // 920-B: killing a check leaves no record behind for the next scheduler to wait on.
    [Fact]
    public void R_HLTH_KillClearsTheRecord()
    {
        using var fx = Repo();
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        using var temp = new TempDir();
        var (config, processes, log) = Build(fx, temp, out var logDir);
        var runner = Runner(config, processes, log);
        runner.Start(sha, "exit 0");
        Assert.True(File.Exists(Path.Combine(logDir, "health-running.json")));

        runner.Kill();

        Assert.False(File.Exists(Path.Combine(logDir, "health-running.json")));
        Assert.Contains(processes.Ended, e => e.Pid == 1001);
    }

    private static GitFx Repo()
    {
        var fx = new GitFx();
        fx.Push("one", new Dictionary<string, string?> { ["a.txt"] = "one" });
        GitFx.Git(fx.Repo, "fetch", "-q", "origin");
        return fx;
    }

    private static (SchedulerConfig Config, HealthProcesses Processes, ListLog Log) Build(GitFx fx, TempDir temp, out string logDir)
    {
        logDir = Path.Combine(temp.Path, "logs");
        var config = new SchedulerConfig
        {
            Project = "proj",
            Repo = fx.Repo,
            WorkspaceRoot = Path.Combine(temp.Path, "ws"),
            LogDir = logDir,
            JobsEnvVar = "PTK_JOBS",
            HealthCommand = "exit 0",
            Slots = 2,
        };
        return (config, new HealthProcesses(), new ListLog());
    }

    private static HealthWorktreeRunner Runner(SchedulerConfig config, HealthProcesses processes, ListLog log) =>
        new(config, new GitRunner(), processes, new FakeClock(T0), log, cores: 8);

    /// <summary>An <see cref="IWorkerProcesses"/> that hands out pids from 1001 and lets a test stop them.</summary>
    private sealed class HealthProcesses : IWorkerProcesses
    {
        private readonly Dictionary<int, DateTimeOffset> _alive = [];
        private int _next = 1001;

        public List<(string Job, int Pid)> Ended { get; } = [];

        public List<(int Index, string CommandLine, string JobName, int Pid, DateTimeOffset Start)> Started { get; } = [];

        public int ExitCodeValue { get; set; }

        public StartedProcess? Start(string commandLine, string jobName, string workingDirectory,
            IReadOnlyDictionary<string, string?> environment)
        {
            var pid = _next++;
            Started.Add((Started.Count, commandLine, jobName, pid, T0));
            _alive[pid] = T0;
            return new StartedProcess { Pid = pid, Start = T0 };
        }

        public bool IsAlive(int pid, DateTimeOffset start) => _alive.TryGetValue(pid, out var s) && s == start;

        public DateTimeOffset? CmdStartTime(int pid) => _alive.TryGetValue(pid, out var s) ? s : null;

        public void EndJob(string jobName, int pid)
        {
            Ended.Add((jobName, pid));
            _alive.Remove(pid);
        }

        public int? ExitCode(int pid) => _alive.ContainsKey(pid) ? null : ExitCodeValue;

        public long? LogSize(string path) => 0;

        public DateTimeOffset? LogLastWrite(string path) => null;

        public void MakeAlive(int pid, DateTimeOffset start) => _alive[pid] = start;

        public void Stop(int pid) => _alive.Remove(pid);
    }
}
