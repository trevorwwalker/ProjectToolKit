// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>A request to the planner in <c>&lt;jobsDir&gt;/requests/</c> (CARD-18).</summary>
public sealed record JobRequest
{
    /// <summary>The path in the repository.</summary>
    public required string Path { get; init; }

    /// <summary>The file name without <c>.md</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The first <c># </c> line.</summary>
    public string Title { get; init; } = "";

    /// <summary>The first <c>Status:</c> line's value.</summary>
    public string Status { get; init; } = "";

    /// <summary>The first <c>For:</c> line's value.</summary>
    public string For { get; init; } = "";

    /// <summary>Open: Status does not start <c>answered</c>, <c>closed</c> or <c>done</c> (CARD-18).</summary>
    public bool Open { get; init; }
}
