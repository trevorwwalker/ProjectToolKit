using System.Text.Json;

namespace Scheduler.Tray;

/// <summary>
/// The pool's status.txt (MP-H3) as the tray reads it for its tooltip (MP-H9): each project's state. The pool
/// writes it in its log folder, the pool file's <c>logDir</c> or <c>%LOCALAPPDATA%\ProjectToolkit\pool</c>.
/// </summary>
public static class PoolStatusFile
{
    /// <summary>The pool file when <c>-Pool</c> names none (MP-C1).</summary>
    public static string DefaultPoolFile(string localAppData) => Path.Combine(localAppData, "ProjectToolkit", "pool.json");

    /// <summary>The pool's status.txt for <paramref name="poolFile"/> ("" = the default file), or "" when it cannot be known.</summary>
    public static string PathFor(string poolFile, string localAppData)
    {
        var file = poolFile.Length > 0 ? poolFile : DefaultPoolFile(localAppData);
        var logDir = Path.Combine(localAppData, "ProjectToolkit", "pool");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("logDir", out var dir)
                && dir.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(dir.GetString()))
            {
                logDir = dir.GetString()!.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // No readable pool file: the default folder, which is where a pool without a logDir writes.
        }

        return Path.Combine(logDir, "status.txt");
    }

    /// <summary>
    /// The projects and their states from the status text: the lines <c>name: state, weight n%, used n%, ...</c>
    /// (a project left out of the pool has no weight and is not listed).
    /// </summary>
    public static IReadOnlyList<(string Name, string State)> Projects(string statusText)
    {
        var projects = new List<(string, string)>();
        foreach (var line in statusText.Split('\n'))
        {
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0 || !line.Contains(", weight ", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[(colon + 2)..];
            var comma = rest.IndexOf(',', StringComparison.Ordinal);
            if (comma > 0)
            {
                projects.Add((line[..colon].Trim(), rest[..comma].Trim()));
            }
        }

        return projects;
    }

    /// <summary>The projects from the file at <paramref name="path"/>; none when it is not there yet.</summary>
    public static IReadOnlyList<(string Name, string State)> Read(string path)
    {
        try
        {
            return path.Length > 0 && File.Exists(path) ? Projects(File.ReadAllText(path)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
