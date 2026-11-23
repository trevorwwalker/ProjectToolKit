namespace Scheduler.Core.WebContract;

/// <summary>One category of the categories file, as the page's chips show it (970-C).</summary>
public sealed record PageCategory
{
    /// <summary>The category letter.</summary>
    public required string Letter { get; init; }

    /// <summary>The short name (core, host, web).</summary>
    public string Name { get; init; } = "";

    /// <summary>What it is for, in full: the chip's tooltip.</summary>
    public string Purpose { get; init; } = "";

    /// <summary>The colour the file gives it (a CSS hex colour), or empty to take the palette's by order.</summary>
    public string Color { get; init; } = "";

    /// <summary>
    /// The letter the Categories panel loaded the row with (992-B), or empty for a row added there. A row
    /// whose <see cref="Letter"/> differs from it is a letter change, not a new category.
    /// </summary>
    public string OriginalLetter { get; init; } = "";

    /// <summary>The paths and globs the category owns (961-A rules), in the file's order (992-B).</summary>
    public IReadOnlyList<string> Owns { get; init; } = [];

    /// <summary>The paths and globs taken back out of <see cref="Owns"/> (992-B).</summary>
    public IReadOnlyList<string> Except { get; init; } = [];
}
