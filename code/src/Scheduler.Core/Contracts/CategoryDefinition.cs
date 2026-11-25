// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>One category of categories.json: <c>categories.&lt;letter&gt;</c> (LANE-1).</summary>
public sealed record CategoryDefinition
{
    /// <summary>The category letter.</summary>
    public required string Letter { get; init; }

    /// <summary>What the category is for.</summary>
    public string Name { get; init; } = "";

    /// <summary>Files and folders it owns; a folder ends with <c>/</c>.</summary>
    public IReadOnlyList<string> Owns { get; init; } = [];

    /// <summary>Entries under <see cref="Owns"/> it does not own.</summary>
    public IReadOnlyList<string> Except { get; init; } = [];

    /// <summary>Free text for what else it may touch (LANE-5).</summary>
    public string AlsoOwns { get; init; } = "";

    /// <summary>The definition as compact JSON, compared with categories-accepted.json (LANE-2).</summary>
    public string Json { get; init; } = "";
}
