using Scheduler.Core.Contracts;
using Scheduler.Host.Git;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using GitFx = Scheduler.Host.Tests.Git.GitFixture;

namespace Scheduler.Host.Tests.Program;

public class HealthRunnerTests
{
    // HLTH-3: the check runs in worktree <workspaceRoot>\health at main; health.cmd sets the title, cds,
    // sets jobsEnvVar and runs the command; output goes to health-<sha>-<ts>.log; in a job object.
    [Fact]
    public void Health_RunsInHealthWorktreeWithLog()
    {
        using var fx = new GitFx();
        using var temp = new TempDir();
        fx.Push("one", new Dictionary<string, string?> { ["a.txt"] = "one" });
        GitFx.Git(fx.Repo, "fetch", "-q", "origin");
        var sha = GitFx.Git(fx.Repo, "rev-parse", "--short", "origin/main");
        var config = new SchedulerConfig
        {
            Project = "proj",
            Repo = fx.Repo,
            WorkspaceRoot = Path.Combine(temp.Path, "ws"),
            LogDir = Path.Combine(temp.Path, "logs"),
            JobsEnvVar = "PTK_JOBS",
            HealthCommand = "exit 0",
            Slots = 2,
        };
        var processes = new TickProcesses();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 12, 30, 5, TimeSpan.Zero));
        var runner = new HealthWorktreeRunner(config, new GitRunner(), processes, clock, new ListLog(), cores: 8);

        Assert.Equal(sha, runner.MainShortSha());
        var log = runner.Start(sha, "exit 0");

        var dir = Path.Combine(config.WorkspaceRoot, "health");
        Assert.True(Path.Exists(Path.Combine(dir, ".git")));
        Assert.Equal(GitFx.Git(fx.Repo, "rev-parse", "origin/main"), GitFx.Git(dir, "rev-parse", "HEAD"));
        Assert.Equal(
            [
                "@echo off",
                $"title proj health check of {sha}",
                $"cd /d \"{dir}\"",
                "set PTK_JOBS=4",
                "set MSBUILDDISABLENODEREUSE=1",          // 920-B: no MSBuild node outlives the check
                "set DOTNET_CLI_DISABLE_BUILD_SERVERS=1",
                "exit 0",
            ],
            File.ReadAllLines(Path.Combine(config.LogDir, "health.cmd")));
        Assert.Matches($@"^health-{sha}-\d{{8}}-\d{{6}}\.log$", Path.GetFileName(log));
        Assert.Equal(config.LogDir, Path.GetDirectoryName(log));
        var started = Assert.Single(processes.Started);
        Assert.Equal(dir, started.WorkingDirectory);
        Assert.Contains(Path.Combine(config.LogDir, "health.cmd"), started.CommandLine);
        Assert.EndsWith($"> \"{log}\" 2>&1", started.CommandLine);
        Assert.Contains("health", started.JobName); // runs in a job object named for the health check
    }
}
