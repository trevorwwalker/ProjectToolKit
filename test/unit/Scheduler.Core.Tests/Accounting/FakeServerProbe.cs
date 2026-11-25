// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>A model server that answers from what the test puts in it, and counts the calls.</summary>
internal sealed class FakeServerProbe : IServerProbe
{
    /// <summary>What <c>/models</c> answers; null means the server did not answer.</summary>
    public string? ModelsJson { get; set; }

    /// <summary>What the metrics URL answers; null means it did not answer.</summary>
    public string? MetricsText { get; set; }

    /// <summary>How many times <c>/models</c> was asked for, all endpoints together.</summary>
    public int ModelsFetches { get; private set; }

    /// <summary>How many times the metrics were asked for, all URLs together.</summary>
    public int MetricsFetches { get; private set; }

    public bool IsUp(string endpoint) => true;

    public string? ReadModels(string endpoint)
    {
        ModelsFetches++;
        return ModelsJson;
    }

    public string? ReadMetrics(string metricsUrl)
    {
        MetricsFetches++;
        return MetricsText;
    }
}
