// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Config;

/// <summary>
/// scheduler.json's text plus the command line become a <see cref="SchedulerConfig"/>: the
/// defaults of CFG-6, the overrides of CFG-7, and the pinning of the worker count of CFG-8.
/// Finding and reading the file is 080-B, so here the text and the options come in as arguments.
/// Test plan: scheduler-next/test-plans/config.md.
/// </summary>
public class SchedulerConfigLoaderTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    private static SchedulerConfig Load(string json, CommandLineOptions? options = null) =>
        SchedulerConfigLoader.Load(json, Repo, LocalAppData, options);

    // CFG-6: every default the rule lists, one assertion each. An empty file and a blank file
    // both give them (L156-205); the repository-derived three come from the repo and %LOCALAPPDATA%.
    [Fact]
    public void Config_EmptyFile_AllDefaults()
    {
        var cfg = Load("{}");

        Assert.Equal(Repo, cfg.Repo);
        Assert.Equal("Proj", cfg.Project);
        Assert.Equal("jobs", cfg.JobsDir);
        Assert.Equal("origin", cfg.Remote);
        Assert.Equal("main", cfg.Branch);
        Assert.Equal(2, cfg.Slots);
        Assert.Equal(8765, cfg.Port);
        Assert.Equal("localhost", cfg.Listen);
        Assert.False(cfg.RemoteControl);
        Assert.False(cfg.Profile);
        Assert.Equal(Repo + "-workspaces", cfg.WorkspaceRoot);
        Assert.Equal(@"C:\Users\dev\AppData\Local\Proj\scheduler", cfg.LogDir);
        Assert.Equal("AGENT_JOBS", cfg.JobsEnvVar);
        Assert.Equal("Hidden", cfg.WorkerWindow);
        Assert.Equal(15, cfg.PollSeconds);
        Assert.Equal(2, cfg.MaxRetries);
        Assert.Equal(30, cfg.StallMinutes);
        Assert.Equal(240, cfg.MaxMinutes);
        Assert.Equal(20, cfg.StopGraceMinutes);
        Assert.Equal(20, cfg.WarnMinutes);
        Assert.Equal(5, cfg.PageStopGraceMinutes);
        Assert.Equal("", cfg.HealthCommand);
        Assert.Equal("", cfg.PromptExtra);
        Assert.Equal("copilot", cfg.WorkerCli);
        Assert.Equal("-p {prompt} --allow-all --no-ask-user", cfg.WorkerArgs);
        Assert.False(cfg.WorkerWeb);
        Assert.Equal("", cfg.Model);
        Assert.Equal("", cfg.Endpoint);
        Assert.Equal("", cfg.MetricsUrl);
        Assert.Equal(
            new[] { "exceeded your (monthly )?quota", "quota exceeded", "rate limit" },
            cfg.QuotaPatterns);

        // A file with nothing in it is the same as no file: every setting falls back (L154).
        var blank = Load("   ");
        Assert.Equal("Proj", blank.Project);
        Assert.Equal("jobs", blank.JobsDir);
        Assert.Equal(2, blank.Slots);
        Assert.Equal("-p {prompt} --allow-all --no-ask-user", blank.WorkerArgs);
        Assert.Equal(cfg.QuotaPatterns, blank.QuotaPatterns);
    }

    // CFG-6: jobsDir has any trailing '/' trimmed (L158); "" means the default.
    [Fact]
    public void Config_JobsDirTrailingSlash_Trimmed()
    {
        Assert.Equal("work/jobs", Load("""{"jobsDir":"work/jobs/"}""").JobsDir);
        Assert.Equal("work/jobs", Load("""{"jobsDir":"work/jobs"}""").JobsDir);
        Assert.Equal("jobs", Load("""{"jobsDir":""}""").JobsDir);
    }

    // CFG-6: workspaceRoot, with slotRoot as its alias, and the repository path plus
    // "-workspaces" when neither is given (L175).
    [Fact]
    public void Config_SlotRootAlias_SameAsWorkspaceRoot()
    {
        Assert.Equal(@"D:\work\slots", Load("""{"slotRoot":"D:\\work\\slots"}""").WorkspaceRoot);

        // workspaceRoot wins over the alias.
        Assert.Equal(
            @"D:\work\ws",
            Load("""{"slotRoot":"D:\\work\\slots","workspaceRoot":"D:\\work\\ws"}""").WorkspaceRoot);

        // Neither: beside the repository, with "-workspaces" appended.
        Assert.Equal(Repo + "-workspaces", Load("{}").WorkspaceRoot);
    }

    // CFG-7: what is given on the command line wins for this run only (L89-98, L162-196);
    // the config text is never written back.
    [Fact]
    public void Config_CommandLineOverrides_OneRun()
    {
        const string json = """
            { "slots": 2, "port": 8765, "worker": { "model": "m1" }, "health": { "command": "h" } }
            """;

        var cfg = Load(
            json,
            CommandLine.Parse(["-Slots", "4", "-Port", "9000", "-Model", "m2", "-NoHealthCheck", "-Paused", "-DryRun"]));

        Assert.Equal(4, cfg.Slots);
        Assert.Equal(9000, cfg.Port);
        Assert.Equal("m2", cfg.Model);
        Assert.Equal("", cfg.HealthCommand);
        Assert.True(cfg.StartPaused);
        Assert.True(cfg.DryRun);

        // The file is unchanged, and the same text loaded again keeps its own values.
        Assert.Contains("\"model\": \"m1\"", json);
        var again = Load(json);
        Assert.Equal(2, again.Slots);
        Assert.Equal(8765, again.Port);
        Assert.Equal("m1", again.Model);
        Assert.Equal("h", again.HealthCommand);
        Assert.False(again.StartPaused);
        Assert.False(again.DryRun);
    }

    // CFG-7: a zero on the command line is PowerShell's "not given", so the file's value stands
    // (L162-164) - and the count is not pinned either.
    [Fact]
    public void Config_SlotsZeroOrPortZero_DoNotOverride()
    {
        const string json = """{ "slots": 2, "port": 8765 }""";

        var cfg = Load(json, CommandLine.Parse(["-Slots", "0", "-Port", "0"]));

        Assert.Equal(2, cfg.Slots);
        Assert.Equal(8765, cfg.Port);
        Assert.False(cfg.SlotsPinned);

        // Because -Slots 0 did not pin it, workers.json still sets the count (CFG-8).
        Assert.Equal(5, SchedulerConfigLoader.ApplyWorkersJson(cfg, """{"slots": 5}""").Slots);
    }

    // CFG-8 with WRK-2: -Slots on the command line pins the count, so workers.json's slots is
    // ignored; without it workers.json's slots is the count, never below a running slot.
    [Fact]
    public void Config_SlotsOnCommandLine_PinsCountOverWorkersJson()
    {
        var pinned = Load("{}", CommandLine.Parse(["-Slots", "3"]));
        Assert.True(pinned.SlotsPinned);
        Assert.Equal(3, SchedulerConfigLoader.ApplyWorkersJson(pinned, """{"slots": 5}""").Slots);

        // Not pinned: workers.json's slots is the count.
        Assert.Equal(5, SchedulerConfigLoader.ApplyWorkersJson(Load("{}"), """{"slots": 5}""").Slots);

        // Nothing usable in workers.json leaves the count alone (L2570-2577): no slots, 0,
        // and text that is not JSON at all.
        Assert.Equal(2, SchedulerConfigLoader.ApplyWorkersJson(Load("{}"), "{}").Slots);
        Assert.Equal(2, SchedulerConfigLoader.ApplyWorkersJson(Load("{}"), """{"slots": 0}""").Slots);
        Assert.Equal(2, SchedulerConfigLoader.ApplyWorkersJson(Load("{}"), "not json").Slots);

        // WRK-2: never below the highest running slot.
        Assert.Equal(4, SchedulerConfigLoader.ApplyWorkersJson(Load("{}"), """{"slots": 2}""", [4]).Slots);
    }
}
