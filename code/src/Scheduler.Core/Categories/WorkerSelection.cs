// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Categories;

/// <summary>Which model server a slot's worker uses, and how many threads it gets (WRK-4, WRK-7).</summary>
public static class WorkerSelection
{
    /// <summary>
    /// <c>Effective-Worker</c> (WRK-4, L2695). An agent with format <c>claude</c>, or one whose
    /// endpoint is given as "", runs its own service: no endpoint, no metrics. Endpoint: the
    /// worker's, else none for own-service, else the agent's, else the config's. Model: the
    /// worker's, else the agent's, else the config's. Metrics: the worker's, else derived from the
    /// worker's endpoint, else none for own-service, else derived from the agent's endpoint, else
    /// the config's metrics URL, else derived from the config's endpoint.
    /// </summary>
    public static EffectiveWorker Effective(
        SchedulerConfig config,
        IReadOnlyDictionary<int, WorkerDefinition> workers,
        int slot)
    {
        workers.TryGetValue(slot, out var c);
        return Effective(
            config,
            c?.Name ?? "",
            agent: c?.Agent ?? "",
            endpoint: c?.Endpoint ?? "",
            model: c?.Model ?? "",
            metricsUrl: c?.MetricsUrl ?? "",
            categories: c?.Categories ?? "");
    }

    /// <summary>
    /// <c>Effective-Worker</c> with a worker <paramref name="type"/> in place of the worker definition
    /// (WT-C4): its endpoint, model, metrics, agent and cli resolve exactly as a worker's do.
    /// </summary>
    public static EffectiveWorker Effective(SchedulerConfig config, WorkerType type) =>
        Effective(
            config,
            type.Name,
            agent: type.Agent,
            endpoint: type.Endpoint,
            model: type.Model,
            metricsUrl: type.MetricsUrl,
            categories: "");

    private static EffectiveWorker Effective(
        SchedulerConfig config,
        string name,
        string agent,
        string endpoint,
        string model,
        string metricsUrl,
        string categories)
    {
        var a = Agents.AgentOf(config, agent);
        var own = a.Format == AgentFormat.Claude || (a.HasEndpoint && a.Endpoint.Length == 0);

        var useEndpoint = !string.IsNullOrEmpty(endpoint) ? endpoint
            : own ? ""
            : a.HasEndpoint ? a.Endpoint
            : config.Endpoint;
        var useModel = !string.IsNullOrEmpty(model) ? model
            : a.Model.Length > 0 ? a.Model
            : config.Model;
        var metrics = !string.IsNullOrEmpty(metricsUrl) ? metricsUrl
            : !string.IsNullOrEmpty(endpoint) ? MetricsUrlFor(endpoint)
            : own ? ""
            : a.HasEndpoint ? MetricsUrlFor(a.Endpoint)
            : config.MetricsUrl.Length > 0 ? config.MetricsUrl
            : MetricsUrlFor(config.Endpoint);

        return new EffectiveWorker
        {
            Name = name,
            Endpoint = useEndpoint,
            Model = useModel,
            MetricsUrl = metrics,
            Categories = categories.ToUpperInvariant(),
            Agent = a.Name,
            Cli = a.Cli,
        };
    }

    /// <summary>
    /// 1087-C (WT-P3): the model a type runs, as the page shows it: its own model if set, else the
    /// <c>Effective-Worker</c> model (the agent's default, else the config's), and where models.json
    /// keys that model - a server alias like <c>spark</c> - its first served name. This is what the
    /// worker windows put in a running worker's title (510-B), and what the Worker types panel shows
    /// greyed as the Model box's placeholder when the type's own model is blank.
    /// </summary>
    public static string DisplayedModel(
        SchedulerConfig config,
        WorkerType type,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models)
    {
        var model = Effective(config, type).Model;
        return models is not null && models.TryGetValue(model, out var served) && served.Count > 0 ? served[0] : model;
    }

    /// <summary><c>Metrics-Url-For</c> (L987): <c>scheme://authority/metrics</c>, or "" for no or a bad endpoint.</summary>
    public static string MetricsUrlFor(string endpoint)
    {
        if (endpoint.Length == 0 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return "";
        }

        return $"{uri.Scheme}://{uri.Authority}/metrics";
    }

    /// <summary>
    /// <c>Jobs-Per-Worker</c> (WRK-7, WT-11, L703): max(1, floor(cores / maxWorkers)); no workers
    /// counts as one. The value goes in the jobsEnvVar environment variable and into <c>{jobs}</c>.
    /// </summary>
    public static int JobsPerWorker(int cores, int workers) => Math.Max(1, cores / Math.Max(1, workers));
}
