// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>categories.json on main (LANE-1): the categories and the shared files.</summary>
public sealed record CategoriesFile
{
    /// <summary>The categories by letter (keys compare case-insensitively, G-1).</summary>
    public IReadOnlyDictionary<string, CategoryDefinition> Categories { get; init; } =
        new Dictionary<string, CategoryDefinition>(Text.Comparer);

    /// <summary><c>shared.addOnly</c>: files any category may add to.</summary>
    public IReadOnlyList<string> AddOnly { get; init; } = [];

    /// <summary><c>shared.rerecord</c>: files any category may re-record for its own gated change.</summary>
    public IReadOnlyList<string> Rerecord { get; init; } = [];

    /// <summary><c>shared.rerecordRule</c>.</summary>
    public string RerecordRule { get; init; } = "";
}
