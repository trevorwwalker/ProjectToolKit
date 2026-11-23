using System.Text.Json.Nodes;
using Scheduler.Core;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Pool;

namespace Scheduler.Host.Pool;

/// <summary>One project in the top-level config: its own repository and jobs folder (MP-1).</summary>
/// <param name="Name">The project's name.</param>
/// <param name="Repo">The project's own repository.</param>
/// <param name="JobsDir">Its jobs folder in that repository, without a trailing <c>/</c>.</param>
/// <param name="Weight">Its weight (MP-4); a missing one is the smallest other (300-A Q5, Core's <c>PoolWeights</c>).</param>
/// <param name="Remote">The git remote.</param>
/// <param name="Branch">The main branch.</param>
public sealed record PoolProjectEntry(string Name, string Repo, string JobsDir, int Weight, string Remote, string Branch)
{
    /// <summary>The fewest workspaces a project may have (1029-B).</summary>
    public const int MinWorkspaces = 1;

    /// <summary>The most workspaces a project may have (1029-B).</summary>
    public const int MaxWorkspaces = 64;

    /// <summary>
    /// 1029-B: the project's workspaces, <c>slot1..slotN</c> under its workspace root: at most this many of its
    /// workers run at once. Null when the file gives none (or a bad one): the pool's <c>maxWorkers</c>.
    /// </summary>
    public int? Workspaces { get; init; }
}

/// <summary>
/// The top-level config of a pool (MP-1, spec B3 Q12 as answered on card 330-B; MP-C1 to MP-C4): the
/// project list and, top-level as well, the page's port, listen, theme and remote control, the
/// pool's own log folder and the worker types.
/// </summary>
public sealed record PoolConfig(
    IReadOnlyList<PoolProjectEntry> Projects,
    int Port,
    string Theme,
    bool RemoteControl)
{
    /// <summary>The page's port when the file gives none (as <c>SchedulerConfig.Port</c>).</summary>
    public const int DefaultPort = 8765;

    /// <summary>Where ProjectToolkit is, for every project of the pool (card 540-B), or "" to look above the exe.</summary>
    public string Toolkit { get; init; } = "";

    /// <summary>MP-C3: <c>localhost</c> or <c>lan</c>, beside the port (955-D Q2).</summary>
    public string Listen { get; init; } = "localhost";

    /// <summary>MP-C3: the pool's own log folder; "" = <c>%LOCALAPPDATA%\ProjectToolkit\pool</c>.</summary>
    public string LogDir { get; init; } = "";

    /// <summary>The file's <c>about</c> line, kept for the reader.</summary>
    public string About { get; init; } = "";

    /// <summary>
    /// The pool's worker types and total from the top-level <c>pool</c> section (WT-C1 to WT-C6, WT-C8), each type
    /// with the <c>projects</c> it serves (530-D Q1); null when there is none or it has no valid type (the caller keeps
    /// what it had, WT-C6). The projects' own workers.json files are not read in a pool (MP-C4).
    /// </summary>
    public WorkerPool? Pool { get; init; }

    /// <summary>MP-C2: what the reading skipped and why, one line each, for the log.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>The file was JSON (an object); false when it was not, which a re-read does not take (MP-C7).</summary>
    public bool Valid { get; init; }

    /// <summary>Read the config text; an unreadable file gives no projects and the defaults.</summary>
    public static PoolConfig Parse(string? json)
    {
        JsonObject? root = null;
        try
        {
            root = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            // an unreadable file is the defaults
        }

        var weights = root is null ? new Dictionary<string, int>() : PoolWeights.Parse(json);
        var projects = new List<PoolProjectEntry>();
        var skipped = new List<string>();
        if (root?["projects"] is JsonArray array)
        {
            var index = 0;
            foreach (var item in array.OfType<JsonObject>())
            {
                index++;
                var name = Str(item, "name");
                var repo = Str(item, "repo");
                if (name.Length == 0 || repo.Length == 0)
                {
                    skipped.Add($"pool file: project {index} is skipped: it has no {(name.Length == 0 ? "name" : "repo")}");
                    continue;
                }

                if (projects.Any(p => Text.Same(p.Name, name)))
                {
                    skipped.Add($"pool file: project {name} is listed twice; the repeat is skipped");
                    continue;
                }

                var jobs = Str(item, "jobsDir");
                var remote = Str(item, "remote");
                var branch = Str(item, "branch");
                projects.Add(new PoolProjectEntry(
                    name,
                    repo,
                    jobs.Length == 0 ? "jobs" : jobs.Replace('\\', '/').TrimEnd('/'),
                    weights.TryGetValue(name, out var weight) ? weight : 0,
                    remote.Length == 0 ? "origin" : remote,
                    branch.Length == 0 ? "main" : branch)
                {
                    Workspaces = Workspaces(item, name, skipped),
                });
            }
        }

        var listen = Str(root, "listen");
        return new PoolConfig(
            projects,
            int.TryParse(Str(root, "port"), out var port) && port > 0 ? port : DefaultPort,
            Str(root, "theme"),
            bool.TryParse(Str(root, "remoteControl"), out var remoteControl) && remoteControl)
        {
            Toolkit = Str(root, "toolkit"),
            Listen = listen.Length == 0 ? "localhost" : listen,
            LogDir = Str(root, "logDir"),
            About = Str(root, "about"),
            Pool = WorkerTypesJson.Parse(root, new SchedulerConfig()),
            Skipped = skipped,
            Valid = root is not null,
        };
    }

    /// <summary>
    /// 1029-B: the project's <c>workspaces</c>, a whole number 1..64 (a number in quotes counts, as for <c>weight</c>).
    /// Missing is null; anything else is logged and null, so the pool's <c>maxWorkers</c> is used.
    /// </summary>
    private static int? Workspaces(JsonObject item, string name, List<string> skipped)
    {
        var given = item.FirstOrDefault(p => string.Equals(p.Key, "workspaces", StringComparison.OrdinalIgnoreCase)).Value;
        if (given is null)
        {
            return null;
        }

        var text = given is JsonValue v ? (v.ToString() ?? "").Trim() : "";
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n >= PoolProjectEntry.MinWorkspaces && n <= PoolProjectEntry.MaxWorkspaces)
        {
            return n;
        }

        skipped.Add($"pool file: project {name} has workspaces {given.ToJsonString()}, not a whole number "
            + $"{PoolProjectEntry.MinWorkspaces} to {PoolProjectEntry.MaxWorkspaces}; the pool's maxWorkers is used");
        return null;
    }

    private static string Str(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v ? (v.ToString() ?? "").Trim() : "";
}
