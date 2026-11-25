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

/// <summary>1119-B: a health folder that is a plain folder inside an outer repository is re-created as a worktree.</summary>
public class HealthWorktreeGuardTests
{
    [Fact]
    public void Start_PlainHealthFolderInsideOuterRepository_IsRecreatedAsWorktree()
    {
        using var fx = new GitFx();
        fx.Push("one", new Dictionary<string, string?> { ["a.txt"] = "one" });
        GitFx.Git(fx.Repo, "fetch", "-q", "origin");
        var outer = Path.GetDirectoryName(fx.Repo)!;
        GitFx.Git(outer, "init", "-q", "-b", "master");
        GitFx.Git(outer, "commit", "-q", "--allow-empty", "-m", "initial");
        var config = new SchedulerConfig
        {
            Project = "proj",
            Repo = fx.Repo,
            WorkspaceRoot = Path.Combine(outer, "ws"),
            LogDir = Path.Combine(outer, "logs"),
            JobsEnvVar = "PTK_JOBS",
            HealthCommand = "exit 0",
            Slots = 2,
        };
        var dir = Path.Combine(config.WorkspaceRoot, "health");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "left.txt"), "left");
        var log = new ListLog();
        var runner = new HealthWorktreeRunner(config, new GitRunner(), new TickProcesses(),
            new FakeClock(new DateTimeOffset(2026, 11, 22, 21, 0, 0, TimeSpan.Zero)), log, cores: 8);

        Assert.NotNull(runner.Start(runner.MainShortSha()!, "exit 0"));

        Assert.True(GitRunner.IsWorktreeOf(dir, fx.Repo));
        Assert.Equal(GitFx.Git(fx.Repo, "rev-parse", "origin/main"), GitFx.Git(dir, "rev-parse", "HEAD"));
        var aside = Assert.Single(Directory.GetDirectories(config.WorkspaceRoot, "health.not-a-worktree-*"));
        Assert.Equal("left", File.ReadAllText(Path.Combine(aside, "left.txt")));
        Assert.Contains(log.Lines, l => l.Contains("was not a worktree", StringComparison.Ordinal));
        Assert.Equal("1", GitFx.Git(outer, "rev-list", "--count", "HEAD"));
        Assert.NotNull(GitRunner.CeilingFor(dir));
    }
}
