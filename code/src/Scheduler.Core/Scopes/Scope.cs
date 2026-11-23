namespace Scheduler.Core.Scopes;

/// <summary>
/// A card's scope (spec B2 SC-1): the paths it expects to touch, and the paths it does not. A path
/// ending in <c>/</c> is a folder and covers everything inside it; a path with <c>*</c> or <c>?</c>
/// is a glob and covers every path it matches (<see cref="PathGlob"/>, Q7 as the developer reversed
/// it on card 961-A, 2026-11-19); any other is one file. Paths are relative to the repository.
/// </summary>
/// <param name="Paths">The paths the card expects to touch.</param>
/// <param name="Except">Paths inside those that it does not (an area's <c>except</c> entries).</param>
public sealed record Scope(IReadOnlyList<string> Paths, IReadOnlyList<string> Except)
{
    /// <summary>The paths marked <c>(add-only)</c> on the card's Scope line (SC-5, Q5).</summary>
    public IReadOnlyList<string> AddOnly { get; init; } = [];

    /// <summary>
    /// The repository the paths are in, or empty for "the project's own". Scopes in different
    /// repositories never overlap (SC-2, Q6).
    /// </summary>
    public string Repo { get; init; } = "";

    /// <summary>Why the scope cannot be used (a pattern it cannot match, such as <c>[abc]</c>), or null when it can (SC-1).</summary>
    public string? Problem { get; init; }

    /// <summary>A scope of these paths, with no exceptions.</summary>
    public static Scope Of(params string[] paths) => new(paths, []);
}
