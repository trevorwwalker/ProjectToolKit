// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// Model servers over HTTP; Host returns the raw text and Core parses it. Used by
/// <c>Server-Up</c> in dispatch (DISP-15, cached 20 s), model server down (TIME-16),
/// <c>{contextLength}</c> (WRK-8) and metrics (ACCT-7).
/// </summary>
public interface IServerProbe
{
    /// <summary>The model server at <paramref name="endpoint"/> answers.</summary>
    bool IsUp(string endpoint);

    /// <summary>The server's model list (<c>/v1/models</c> JSON, with max_model_len), or null.</summary>
    string? ReadModels(string endpoint);

    /// <summary>The Prometheus text at <paramref name="metricsUrl"/>, or null.</summary>
    string? ReadMetrics(string metricsUrl);
}
