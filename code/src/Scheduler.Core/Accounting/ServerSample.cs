// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Accounting;

/// <summary>
/// One read of a model server's <c>/metrics</c> (ACCT-7, <c>Sample-One</c> L1030). A gauge the
/// server does not publish is <see cref="double.NaN"/>, never zero.
/// </summary>
public sealed record ServerSample
{
    /// <summary>When it was read.</summary>
    public required DateTimeOffset When { get; init; }

    /// <summary><c>vllm:num_requests_running</c>.</summary>
    public required double Running { get; init; }

    /// <summary><c>vllm:num_requests_waiting</c>.</summary>
    public required double Waiting { get; init; }

    /// <summary><c>vllm:kv_cache_usage_perc</c>, 0 to 1.</summary>
    public required double KvUsage { get; init; }

    /// <summary><c>vllm:prompt_tokens_total</c>.</summary>
    public required double PromptTokens { get; init; }

    /// <summary><c>vllm:generation_tokens_total</c>; its change since the last sample is the speed.</summary>
    public required double GenerationTokens { get; init; }

    /// <summary><c>vllm:prefix_cache_hits_total</c>.</summary>
    public required double PrefixHits { get; init; }

    /// <summary><c>vllm:prefix_cache_queries_total</c>.</summary>
    public required double PrefixQueries { get; init; }

    /// <summary><c>vllm:num_preemptions_total</c>; its change since the last sample is what was preempted.</summary>
    public required double Preemptions { get; init; }
}
