using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;

namespace Scheduler.Host.Config;

/// <summary>
/// Finding the repository and the config file, and loading them with the Core model (A1):
/// CFG-1 the repository (<c>-Repo</c>, the first positional argument, or the current directory,
/// each resolved to its git top level), CFG-2 the config file (<c>-Config</c>, else
/// <c>&lt;repo&gt;\jobs\scheduler.json</c>, else <c>&lt;repo&gt;\docs\jobs\scheduler.json</c> (card 400-B);
/// none is fatal), CFG-3 the re-read from
/// <c>&lt;remote&gt;/&lt;branch&gt;</c> after a fetch, and CFG-4 <c>cfg.repo</c>. What the file and
/// the command line mean together is <see cref="SchedulerConfigLoader"/>'s (070-A); this only finds
/// the text and the repository and hands them over.
/// </summary>
public sealed class ConfigLoader
{
    private readonly GitRunner _git;
    private readonly Func<string, string, string, IMainRepository> _remoteRepo;
    private readonly Func<string, string, string, IMainFeed?> _models;
    private readonly ISchedulerLog _log;
    private readonly string _localAppData;
    private readonly RepositoryLocator _locator;

    /// <param name="git">The git to ask about repositories; "git" on PATH unless a test gives one.</param>
    /// <param name="remoteRepo">The repository the config is re-read from (CFG-3); <see cref="MainRepository"/> unless a test gives one.</param>
    /// <param name="log">Where the config's origin is reported; the lines also come back in the result.</param>
    /// <param name="localAppData">What <c>%LOCALAPPDATA%</c> is, for the default log folder (CFG-6).</param>
    /// <param name="models">
    /// 1063-B: the model of main of (repo, remote, branch), when one is loaded; the process's
    /// (<see cref="MainModels.Shared"/>) unless a test gives one. A config read again once the project has joined (the
    /// pool's re-read) then takes main's scheduler.json from the model after its gate's fetch (MM-9, MM-14); the
    /// first read at a start has no model and goes through git as before.
    /// </param>
    public ConfigLoader(
        GitRunner? git = null,
        Func<string, string, string, IMainRepository>? remoteRepo = null,
        ISchedulerLog? log = null,
        string? localAppData = null,
        Func<string, string, string, IMainFeed?>? models = null)
    {
        _models = models ?? IMainFeed.Shared;
        _git = git ?? new GitRunner();
        _locator = new RepositoryLocator(_git);
        _remoteRepo = remoteRepo ?? ((repo, remote, branch) => new MainRepository(_git, repo, remote, branch));
        _log = log ?? new NullLog();
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>
    /// The configuration for one run. <paramref name="currentDirectory"/> is the directory the
    /// scheduler was started in (CFG-1); it comes in as an argument so a test can stand elsewhere.
    /// </summary>
    public ConfigLoadResult Load(CommandLineOptions options, string? currentDirectory = null)
    {
        var lines = new List<string>();
        void Log(string line)
        {
            lines.Add(line);
            _log.Write(line);
        }

        // CFG-1 (L112-128): the named repository, else - with no -Config - the current directory's,
        // each resolved to its git top level. A named folder that is not a repository is an error.
        var repoGiven = options.Repo.Length > 0;
        var repo = "";
        if (repoGiven)
        {
            repo = _locator.TopLevel(options.Repo) ?? "";
            if (repo.Length == 0)
            {
                return Failed($"{options.Repo} is not inside a git repository.", "", "", false, lines);
            }
        }
        else if (options.ConfigPath.Length == 0)
        {
            repo = _locator.TopLevel(currentDirectory ?? Directory.GetCurrentDirectory()) ?? "";
            if (repo.Length == 0)
            {
                return Failed(
                    "Name the project's repository: scheduler-next <folder> (or run it from inside the repository, or pass -Config).",
                    "",
                    "",
                    false,
                    lines);
            }
        }

        // CFG-2 (L129-130, changed by card 400-B): -Config, else <repo>\jobs\scheduler.json, else
        // X; neither is fatal and the message names both. When both
        // exist, jobs\ wins and the other is reported as ignored.
        var explicitConfig = options.ConfigPath.Length > 0;
        var jobsConfig = Path.Combine(repo, "jobs", "scheduler.json");
        var docsConfig = Path.Combine(repo, "docs", "jobs", "scheduler.json");
        string configPath;
        var defaultJobsDir = "jobs";
        if (explicitConfig)
        {
            configPath = Path.GetFullPath(options.ConfigPath);
            if (repo.Length > 0 && Text.Same(Path.GetDirectoryName(configPath) ?? "", Path.GetDirectoryName(docsConfig) ?? ""))
            {
                defaultJobsDir = "docs/jobs";
            }
        }
        else if (File.Exists(jobsConfig))
        {
            configPath = jobsConfig;
            if (File.Exists(docsConfig))
            {
                Log($"Config: {docsConfig} is ignored: {jobsConfig} exists");
            }
        }
        else
        {
            configPath = docsConfig;
            defaultJobsDir = "docs/jobs";
        }

        if (!File.Exists(configPath))
        {
            return Failed(
                explicitConfig
                    ? $"No scheduler config at {configPath} (copy ProjectToolkit\\assets\\templates\\jobs\\scheduler.json)."
                    : $"No scheduler config at {jobsConfig} or {docsConfig} (copy ProjectToolkit\\assets\\templates\\jobs\\scheduler.json).",
                repo,
                configPath,
                explicitConfig,
                lines);
        }

        var json = File.ReadAllText(configPath);

        // The checkout's own copy is read first whatever: it is the fallback, and it names the
        // remote, branch and jobs folder to re-read from. It has to be JSON for anything to work.
        var checkout = FromJson(json, repo, options, defaultJobsDir);
        if (checkout is null)
        {
            return Failed($"The scheduler config at {configPath} is not valid JSON.", repo, configPath, explicitConfig, lines);
        }

        // CFG-3 (L131-149): the scheduler only fetches, so its checkout can sit behind what was
        // pushed; without -Config the config is taken from <remote>/<branch> instead. A -Config
        // file is used as it is, with no fetch.
        var fromRemote = false;
        var remoteRef = "";
        if (!explicitConfig)
        {
            var remote = checkout.Remote;
            var branch = checkout.Branch;
            var path = checkout.JobsDir.TrimEnd('/') + "/scheduler.json";
            remoteRef = remote + "/" + branch;
            bool fetched;
            string? text;
            if (_models(repo, remote, branch) is { Model: { Snapshot: not null } model } feed)
            {
                var answer = feed.Fetch(FetchAsk.Poll);
                if (answer != FetchAnswer.Fetched)
                {
                    _ = feed.Tick();   // MM-8: a fetch that ran has read the ref already
                }

                fetched = answer != FetchAnswer.Failed;
                text = model.Settings(path);
            }
            else
            {
                var main = _remoteRepo(repo, remote, branch);
                fetched = main.Fetch();
                text = main.ReadFile(path);
            }

            if (!string.IsNullOrWhiteSpace(text) && FromJson(text, repo, options, defaultJobsDir) is not null)
            {
                json = text;
                fromRemote = true;
                Log($"Config: {path} from {remoteRef}");
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                Log($"Config: {configPath} (using the checkout's copy: {remoteRef}'s {path} is invalid JSON)");
            }
            else if (fetched)
            {
                Log($"Config: {configPath} (using the checkout's copy: {remoteRef} has no {path})");
            }
            else
            {
                Log($"Config: {configPath} (using the checkout's copy: the fetch from {remote} failed, so {remoteRef} could not be read)");
            }
        }

        // CFG-4 (L150-151): cfg.repo replaces the repository only when the command line named none,
        // and it does so before the settings that depend on the repository are worked out (CFG-6).
        if (!repoGiven)
        {
            var named = Setting(json, "repo");
            if (named.Length > 0)
            {
                repo = named;
            }
        }

        if (repo.Length == 0)
        {
            // Nothing named a repository at all: the folder holding the config file decides (L152).
            repo = _locator.TopLevel(Path.GetDirectoryName(configPath) ?? "") ?? "";
        }

        var config = SchedulerConfigLoader.Load(json, repo, _localAppData, options, defaultJobsDir);
        return new ConfigLoadResult
        {
            Ok = true,
            Repo = repo,
            ConfigPath = configPath,
            ExplicitConfig = explicitConfig,
            FromRemote = fromRemote,
            RemoteRef = fromRemote ? remoteRef : "",
            ConfigText = json,
            Config = config,
            LogLines = lines,
        };
    }

    /// <summary>
    /// A top-level text setting of the config, matched the way PowerShell matches a property name
    /// (G-1: case-insensitively); "" when the key is absent or is not text.
    /// </summary>
    private static string Setting(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject root)
        {
            return "";
        }

        foreach (var property in root)
        {
            if (Text.Same(property.Key, name) && property.Value is JsonValue value && value.TryGetValue(out string? text))
            {
                return text;
            }
        }

        return "";
    }

    /// <summary>The config the text describes, or null when it is not JSON at all (CFG-3's "invalid JSON").</summary>
    private SchedulerConfig? FromJson(string json, string repo, CommandLineOptions options, string defaultJobsDir)
    {
        try
        {
            return SchedulerConfigLoader.Load(json, repo, _localAppData, options, defaultJobsDir);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Why start-up must stop. Nothing was created and nothing else was run.</summary>
    private static ConfigLoadResult Failed(string error, string repo, string configPath, bool explicitConfig, List<string> lines) =>
        new()
        {
            Ok = false,
            Error = error,
            Repo = repo,
            ConfigPath = configPath,
            ExplicitConfig = explicitConfig,
            LogLines = lines,
        };

    private sealed class NullLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }
}
