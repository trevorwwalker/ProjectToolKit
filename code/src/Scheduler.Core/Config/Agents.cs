using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Config;

/// <summary>
/// The agents a worker can run (CFG-9 to CFG-12): the default one, made from the config's
/// <c>worker</c> section, the named ones the loader adds from <c>cfg.agents</c>, picking one by
/// name, and the placeholders filled into what a worker is started with.
/// </summary>
public static class Agents
{
    /// <summary>
    /// The default agent's format (CFG-9, L210): <c>copilot</c> when the args ask for JSON events,
    /// <c>text</c> otherwise.
    /// </summary>
    public static AgentFormat GuessDefaultFormat(string args) =>
        Text.IsMatch(args, "output-format[= ]json") ? AgentFormat.Copilot : AgentFormat.Text;

    /// <summary>
    /// A named agent's format (CFG-10, L231): <c>claude</c> for stream-json, else the default
    /// agent's guess.
    /// </summary>
    public static AgentFormat GuessFormat(string args) =>
        Text.IsMatch(args, "stream-json") ? AgentFormat.Claude : GuessDefaultFormat(args);

    /// <summary>
    /// A named agent's format from its cli and args (AG-2): <c>codex</c> when the cli is <c>codex</c>
    /// and the args ask for <c>--json</c>, else <see cref="GuessFormat(string)"/>.
    /// </summary>
    public static AgentFormat GuessFormat(string cli, string args) =>
        Text.IsMatch(Path.GetFileNameWithoutExtension(cli), "^codex$") && Text.IsMatch(args, @"(^|\s)--json(\s|$)")
            ? AgentFormat.Codex
            : Text.IsMatch(Path.GetFileNameWithoutExtension(cli), "^opencode$") && Text.IsMatch(args, @"--format[= ]json(\s|$)")
                ? AgentFormat.OpenCode
                : GuessFormat(args);

    /// <summary>What the file's <c>format</c> means; anything unknown reads as plain text (G-1).</summary>
    public static AgentFormat FormatOf(string? format) => format?.ToLowerInvariant() switch
    {
        "copilot" => AgentFormat.Copilot,
        "claude" => AgentFormat.Claude,
        "codex" => AgentFormat.Codex,
        "opencode" => AgentFormat.OpenCode,
        _ => AgentFormat.Text,
    };

    /// <summary>
    /// The default agent (CFG-9, L224-225): the worker section, its format guessed, no model of its
    /// own and never an endpoint of its own - the worker's or the config's endpoint is used instead.
    /// </summary>
    public static AgentDefinition DefaultAgent(SchedulerConfig config) => new()
    {
        Name = AgentDefinition.DefaultName,
        Cli = config.WorkerCli,
        Args = config.WorkerArgs,
        Env = config.WorkerEnv,
        QuotaPatterns = config.QuotaPatterns,
        Model = "",
        Format = GuessDefaultFormat(config.WorkerArgs),
        HasEndpoint = false,
        Endpoint = "",
    };

    /// <summary>
    /// <c>Agent-Of(name)</c> (CFG-11, L239): that agent when it exists, the default one otherwise -
    /// including for no name. Names match as PowerShell hashtable keys do (G-1).
    /// </summary>
    public static AgentDefinition AgentOf(SchedulerConfig config, string? name)
    {
        if (!string.IsNullOrEmpty(name) && config.Agents.TryGetValue(name, out var agent))
        {
            return agent;
        }

        return config.Agents.TryGetValue(AgentDefinition.DefaultName, out var def) ? def : DefaultAgent(config);
    }

    /// <summary>
    /// <c>Expand</c> (CFG-12, L401): <c>{model}</c>, <c>{endpoint}</c>, <c>{jobs}</c> and
    /// <c>{project}</c> filled in. A worker with no model or endpoint of its own gets the config's.
    /// Every other placeholder, <c>{prompt}</c> among them, is left for whoever starts the worker.
    /// </summary>
    public static string Expand(SchedulerConfig config, string? text, int jobs, string model = "", string endpoint = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var useModel = string.IsNullOrEmpty(model) ? config.Model : model;
        var useEndpoint = string.IsNullOrEmpty(endpoint) ? config.Endpoint : endpoint;

        return text.Replace("{model}", useModel)
            .Replace("{endpoint}", useEndpoint)
            .Replace("{jobs}", jobs.ToString(CultureInfo.InvariantCulture))
            .Replace("{project}", config.Project);
    }

    /// <summary>The worker's environment with the same placeholders filled in (CFG-12, WRK-8).</summary>
    public static IReadOnlyDictionary<string, string> ExpandEnv(
        SchedulerConfig config,
        IReadOnlyDictionary<string, string> env,
        int jobs,
        string model = "",
        string endpoint = "")
    {
        var expanded = new Dictionary<string, string>(Text.Comparer);
        foreach (var pair in env)
        {
            expanded[pair.Key] = Expand(config, pair.Value, jobs, model, endpoint);
        }

        return expanded;
    }
}
