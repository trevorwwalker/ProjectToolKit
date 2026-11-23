namespace Scheduler.Core.Categories;

/// <summary>
/// Where a project keeps its categories: <c>categories.json</c> when it exists, else the older
/// <c>lanes.json</c> (same format; 979-A).
/// </summary>
public static class CategoriesLocation
{
    /// <summary>The preferred file name.</summary>
    public const string Name = "categories.json";

    /// <summary>The name older projects still use.</summary>
    public const string OldName = "lanes.json";

    /// <summary>The path to read in <paramref name="jobsDir"/>: categories.json if <paramref name="read"/> finds it, else lanes.json.</summary>
    public static string Rel(string jobsDir, Func<string, string?> read) =>
        read($"{jobsDir}/{Name}") is not null ? $"{jobsDir}/{Name}" : $"{jobsDir}/{OldName}";

    /// <summary>The file's text, from categories.json or else lanes.json; null when neither can be read.</summary>
    public static string? Read(string jobsDir, Func<string, string?> read) =>
        read($"{jobsDir}/{Name}") ?? read($"{jobsDir}/{OldName}");
}
