using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Launch;

/// <summary>
/// The environment a worker process starts with (WRK-8, <c>Start-Worker</c> L1795-1813):
/// variables another agent sets are cleared, and this agent's are set with the placeholders and
/// the model server's context length filled in.
/// </summary>
public static class WorkerEnv
{
    /// <summary>How small a context a worker is ever given (L1810).</summary>
    public const int MinContextLength = 1024;

    /// <summary>The project's own git folder (<c>&lt;repo&gt;/.git</c>); the lock hooks act only in a repository whose common git dir is this.</summary>
    public const string RepoVariable = "PTK_REPO";

    private static readonly Regex ContextPattern = new(@"\{contextLength(?:-(\d+))?\}", RegexOptions.CultureInvariant);

    /// <summary>
    /// What to remove from the environment the worker inherits and what to set for it. The threads
    /// per worker go in the <c>jobsEnvVar</c> variable first (WRK-7), so this agent's own variable
    /// of that name still wins, as it does in the script.
    /// </summary>
    /// <param name="processEnv">The environment the worker inherits; pass null to report every
    /// variable another agent sets, whether or not this machine has it.</param>
    /// <param name="locks">Dynamic locks: the card, exe and hooks folder to set; null when locks are off.</param>
    public static WorkerEnvironment Build(
        SchedulerConfig config,
        AgentDefinition agent,
        EffectiveWorker worker,
        int jobs,
        int slot,
        IReadOnlyDictionary<string, string>? processEnv,
        IServerProbe probe,
        ISchedulerLog log,
        WorkerLocks? locks = null)
    {
        var removed = new List<string>();
        foreach (var other in config.Agents.Values)
        {
            foreach (var key in other.Env.Keys)
            {
                if (agent.Env.ContainsKey(key) || removed.Contains(key, Text.Comparer))
                {
                    continue;
                }

                if (processEnv is not null && !processEnv.ContainsKey(key))
                {
                    continue;
                }

                removed.Add(key);
            }
        }

        var set = new Dictionary<string, string>(Text.Comparer)
        {
            [config.JobsEnvVar] = jobs.ToString(CultureInfo.InvariantCulture),

            // WRK-14, job card 570-B: where the message hook finds this worker's messages.
            [MessageHook.FileVariable] = WorkerFiles.MessageFile(config.LogDir, slot),
        };

        var unset = new List<string>();
        var contextLength = 0;
        var asked = false;
        foreach (var pair in agent.Env)
        {
            var value = pair.Value ?? "";
            if (value.Contains("{contextLength", StringComparison.Ordinal))
            {
                if (!asked)
                {
                    asked = true;
                    contextLength = ContextOf(config, worker, probe);
                }

                if (contextLength <= 0)
                {
                    unset.Add(pair.Key);
                    continue;
                }

                value = ContextPattern.Replace(value, match => WithoutReserve(match, contextLength));
            }

            set[pair.Key] = Agents.Expand(config, value, jobs, worker.Model, worker.Endpoint);
        }

        // The script logged one line per variable; the test plan asks for the slot to say it once.
        if (unset.Count > 0)
        {
            log.Write($"slot {slot}: the model server gave no context length; {string.Join(", ", unset)} left unset");
        }

        // Dynamic locks (DL-18): the card, the lock exe and the scheduler's hooks folder; none when locks are off.
        foreach (var pair in locks?.Variables() ?? [])
        {
            set[pair.Key] = pair.Value;
        }

        // Card 1039-B: the hooks act only in the project's own repository, found by its git folder.
        if (locks is not null && config.Repo.Length > 0)
        {
            set[RepoVariable] = config.Repo.Replace('\\', '/').TrimEnd('/') + "/.git";
        }

        return new WorkerEnvironment { Removed = removed, Set = set };
    }

    /// <summary>The context the model server serves for this worker's model; 0 when it does not say.</summary>
    private static int ContextOf(SchedulerConfig config, EffectiveWorker worker, IServerProbe probe)
    {
        var endpoint = worker.Endpoint.Length > 0 ? worker.Endpoint : config.Endpoint;
        if (endpoint.Length == 0)
        {
            return 0;
        }

        var model = worker.Model.Length > 0 ? worker.Model : config.Model;
        return ServerModels.MaxModelLen(probe.ReadModels(endpoint), model);
    }

    // {contextLength} is what the server serves, {contextLength-N} that less a reserve for the
    // reply; neither ever goes below MinContextLength.
    private static string WithoutReserve(Match match, int contextLength)
    {
        var reserve = int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
        return Math.Max(MinContextLength, contextLength - reserve).ToString(CultureInfo.InvariantCulture);
    }
}
