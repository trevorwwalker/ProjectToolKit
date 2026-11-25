// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>Health and the red banner (PAGE-5, A7).</summary>
public sealed record PageHealthView
{
    /// <summary>Whether main is green.</summary>
    public bool Green { get; init; } = true;

    /// <summary>The health line.</summary>
    public string Text { get; init; } = "";

    /// <summary>The red banner text, or empty when there is none.</summary>
    public string Banner { get; init; } = "";

    /// <summary>The short sha the last health check finished on; "" until one has (HLTH-6).</summary>
    public string Sha { get; init; } = "";

    /// <summary>The sha of the health check running now; "" when none runs (known gap 930-C).</summary>
    public string CheckingSha { get; init; } = "";

    /// <summary>Whether the model server is down.</summary>
    public bool ServerDown { get; init; }

    /// <summary>Category overlaps found, one line each.</summary>
    public IReadOnlyList<string> CategoryOverlaps { get; init; } = [];

    /// <summary>The model servers that do not answer (510-B).</summary>
    public IReadOnlyList<string> DownServers { get; init; } = [];

    /// <summary>Whether the last health check left a log the page can show (510-B).</summary>
    public bool HasLog { get; init; }
}
