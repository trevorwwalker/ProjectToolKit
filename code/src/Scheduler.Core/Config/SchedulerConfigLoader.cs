// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Config;

/// <summary>
/// scheduler.json plus the command line, as one <see cref="SchedulerConfig"/> (A1): the defaults of
/// CFG-6, then the file, then what the command line overrode for this run (CFG-7). Finding and
/// reading the file, and re-reading it from main, is the host's (CFG-1 to CFG-4, card 080-B); here
/// the text and the options come in as arguments.
/// </summary>
public static class SchedulerConfigLoader
{
    /// <summary>
    /// The config for a repository: <paramref name="jsonText"/> is the file's text ("" or blank is
    /// an empty file, which is every default), and <paramref name="commandLine"/> what was given on
    /// the command line. <paramref name="localAppData"/> is <c>%LOCALAPPDATA%</c>, for the default
    /// log folder. <paramref name="defaultJobsDir"/> is the queue folder, relative to the repository, used when the file
    /// has no <c>jobsDir</c>: the one the config was found in (CFG-6, card 400-B).
    /// </summary>
    public static SchedulerConfig Load(string jsonText, string repo, string localAppData, CommandLineOptions? commandLine = null, string defaultJobsDir = "jobs")
    {
        var options = commandLine ?? CommandLineOptions.None;
        var cfg = ConfigJson.Root(jsonText);
        var worker = ConfigJson.GetObject(cfg, "worker");
        var limits = ConfigJson.GetObject(cfg, "limits");
        var health = ConfigJson.GetObject(cfg, "health");
        var page = ConfigJson.GetObject(cfg, "page");

        var project = Cfg.Value(ConfigJson.GetString(cfg, "project"), FolderName(repo));
        var workerArgs = Cfg.Value(ConfigJson.GetArgs(worker, "args"), "-p {prompt} --allow-all --no-ask-user");

        var config = new SchedulerConfig
        {
            Repo = repo,
            Project = project,
            JobsDir = Cfg.Value(ConfigJson.GetString(cfg, "jobsDir"), defaultJobsDir).TrimEnd('/'),
            Remote = Cfg.Value(ConfigJson.GetString(cfg, "remote"), "origin"),
            Branch = Cfg.Value(ConfigJson.GetString(cfg, "branch"), "main"),

            // CFG-7: the command line wins for this run; a zero is PowerShell's "not given" (L162-164).
            Slots = options.Slots != 0 ? options.Slots : Cfg.Value(ConfigJson.GetInt(cfg, "slots"), 2),
            SlotsPinned = options.SlotsGiven,
            Port = options.Port != 0 ? options.Port : Cfg.Value(ConfigJson.GetInt(cfg, "port"), 8765),

            Listen = Cfg.Value(ConfigJson.GetString(cfg, "listen"), "localhost"),
            RemoteControl = Cfg.Value(ConfigJson.GetBool(cfg, "remoteControl"), false),
            Profile = Cfg.Value(ConfigJson.GetBool(cfg, "profile"), false),

            // workspaceRoot, or its alias slotRoot, else beside the repository (L175).
            WorkspaceRoot = Cfg.Value(
                ConfigJson.GetString(cfg, "workspaceRoot"),
                Cfg.Value(ConfigJson.GetString(cfg, "slotRoot"), repo + "-workspaces")),
            LogDir = Cfg.Value(ConfigJson.GetString(cfg, "logDir"), System.IO.Path.Combine(localAppData, project, "scheduler")),
            JobsEnvVar = Cfg.Value(ConfigJson.GetString(cfg, "jobsEnvVar"), "AGENT_JOBS"),
            WorkerWindow = Cfg.Value(ConfigJson.GetString(cfg, "workerWindow"), "Hidden"),

            PollSeconds = Cfg.Value(ConfigJson.GetInt(limits, "pollSeconds"), 15),
            MaxRetries = Cfg.Value(ConfigJson.GetInt(limits, "maxRetries"), 2),
            StallMinutes = Cfg.Value(ConfigJson.GetInt(limits, "stallMinutes"), 30),
            MaxMinutes = Cfg.Value(ConfigJson.GetInt(limits, "maxMinutes"), 240),
            StopGraceMinutes = Cfg.Value(ConfigJson.GetInt(limits, "stopGraceMinutes"), 20),
            WarnMinutes = Cfg.Value(ConfigJson.GetInt(limits, "warnMinutes"), 20),
            PageStopGraceMinutes = Cfg.Value(ConfigJson.GetInt(limits, "pageStopGraceMinutes"), 5),

            // 1100-B: how long a health check may run before the scheduler warns about it (HLTH-4); 0 = off.
            HealthWarnMinutes = Cfg.Value(ConfigJson.GetInt(limits, "healthWarnMinutes"), 5),

            // -NoHealthCheck empties the health command (L192).
            HealthCommand = options.NoHealthCheck ? "" : Cfg.Value(ConfigJson.GetString(health, "command"), ""),
            PromptExtra = Cfg.Value(ConfigJson.GetString(cfg, "promptExtra"), ""),

            WorkerCli = Cfg.Value(ConfigJson.GetString(worker, "cli"), "copilot"),
            WorkerArgs = workerArgs,
            WorkerWeb = Cfg.Value(ConfigJson.GetBool(worker, "web"), false),
            Model = options.Model.Length > 0 ? options.Model : Cfg.Value(ConfigJson.GetString(worker, "model"), ""),
            Endpoint = Cfg.Value(ConfigJson.GetString(worker, "endpoint"), ""),
            MetricsUrl = Cfg.Value(ConfigJson.GetString(worker, "metricsUrl"), ""),
            QuotaPatterns = ConfigJson.GetStringList(worker, "quotaPatterns") ?? SchedulerConfig.DefaultQuotaPatterns,
            WorkerEnv = ConfigJson.GetMap(worker, "env"),

            // CFG-13: the category names from the file, used until categories.json on main has been read.
            CategoryNames = ConfigJson.GetMap(cfg, "lanes"),
            PageTheme = Cfg.Value(ConfigJson.GetString(page, "theme"), ""),

            DryRun = options.DryRun,
            StartPaused = options.Paused,
            ExplicitConfig = options.ConfigPath.Length > 0,
        };

        return config with { Agents = ReadAgents(config, ConfigJson.GetObject(cfg, "agents")) };
    }

    /// <summary>
    /// What workers.json says about how many workers there are (CFG-8, WRK-2, WT-C1, WT-C10): the
    /// pool's <c>maxWorkers</c> when the file has a <c>pool</c>, its <c>slots</c> otherwise - neither
    /// when <c>-Slots</c> pinned the count on the command line, and never below the highest slot still
    /// running. Text that is not JSON changes nothing (L2559). This is the config side of the migration:
    /// <see cref="WorkerTypesJson"/> makes the types themselves.
    /// </summary>
    public static SchedulerConfig ApplyWorkersJson(SchedulerConfig config, string workersJsonText, IReadOnlyList<int>? runningSlots = null)
    {
        if (config.SlotsPinned)
        {
            return config;
        }

        JsonObject? root;
        try
        {
            root = ConfigJson.Root(workersJsonText);
        }
        catch (JsonException)
        {
            return config;
        }

        // WT-C10: a pool's slots and workers are ignored, so the total comes from the pool alone.
        var count = WorkerTypesJson.HasPool(root)
            ? WorkerTypesJson.Parse(root, config)?.MaxWorkers ?? 0
            : ConfigJson.GetInt(root, "slots") ?? 0;

        if (count <= 0)
        {
            return config;
        }

        if (runningSlots is { Count: > 0 })
        {
            var top = runningSlots.Max();
            if (top > count)
            {
                count = top;
            }
        }

        return config with { MaxWorkers = count };
    }

    /// <summary>
    /// The agents (CFG-9, CFG-10): the default one, which is the worker section, plus
    /// <c>cfg.agents</c>. A named agent's cli defaults to its name, its quota patterns to the three
    /// of the rule, and its format to the guess from its own args.
    /// </summary>
    private static IReadOnlyDictionary<string, AgentDefinition> ReadAgents(SchedulerConfig config, JsonObject? named)
    {
        var agents = new Dictionary<string, AgentDefinition>(Text.Comparer)
        {
            [AgentDefinition.DefaultName] = Agents.DefaultAgent(config),
        };

        if (named is null)
        {
            return agents;
        }

        foreach (var entry in named)
        {
            var source = entry.Value as JsonObject;
            var args = Cfg.Value(ConfigJson.GetArgs(source, "args"), "");
            var quota = ConfigJson.GetStringList(source, "quotaPatterns", dropEmpty: true);
            var hasEndpoint = ConfigJson.Has(source, "endpoint");
            var format = ConfigJson.GetString(source, "format");
            var cli = Cfg.Value(ConfigJson.GetString(source, "cli"), entry.Key);

            agents[entry.Key] = new AgentDefinition
            {
                Name = entry.Key,
                Cli = cli,
                Args = args,
                Env = ConfigJson.GetMap(source, "env"),
                QuotaPatterns = quota is { Count: > 0 } ? quota : SchedulerConfig.DefaultAgentQuotaPatterns,
                Model = Cfg.Value(ConfigJson.GetString(source, "model"), ""),
                Format = format is null ? Agents.GuessFormat(cli, args) : Agents.FormatOf(format),

                // CFG-10 with WRK-4: the key's presence is recorded apart from its value.
                HasEndpoint = hasEndpoint,
                Endpoint = hasEndpoint ? Cfg.Value(ConfigJson.GetString(source, "endpoint"), "") : "",
            };
        }

        return agents;
    }

    /// <summary>The folder a repository path names (L156).</summary>
    private static string FolderName(string repo)
    {
        var trimmed = repo.TrimEnd('/', '\\');
        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }
}
