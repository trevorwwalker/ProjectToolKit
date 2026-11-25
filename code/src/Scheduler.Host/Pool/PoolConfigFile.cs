// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Pool;

/// <summary>
/// The pool file (MP-C1, MP-C7): <c>%LOCALAPPDATA%\ProjectToolkit\pool.json</c> or the path after
/// <c>-Pool</c>, the PC's own and in no repository. Read at start; re-read when its text changes
/// (checked each tick), where invalid JSON or a file with no valid project is logged and the
/// previous settings kept. The page's Worker types panel writes its <c>pool</c> section back in place.
/// </summary>
public sealed class PoolConfigFile(string path, ISchedulerLog log)
{
    private string? _text;

    /// <summary>The file's path.</summary>
    public string Path { get; } = System.IO.Path.GetFullPath(path);

    /// <summary>The settings in use; null until a valid file has been read.</summary>
    public PoolConfig? Current { get; private set; }

    /// <summary>MP-C1: the default pool file under <paramref name="localAppData"/>.</summary>
    public static string DefaultPath(string localAppData) =>
        System.IO.Path.Combine(localAppData, "ProjectToolkit", "pool.json");

    /// <summary>MP-C3: the pool's own log folder: the file's <c>logDir</c>, else <c>%LOCALAPPDATA%\ProjectToolkit\pool</c>.</summary>
    public static string LogDirOf(PoolConfig config, string localAppData) =>
        config.LogDir.Length > 0 ? config.LogDir : System.IO.Path.Combine(localAppData, "ProjectToolkit", "pool");

    /// <summary>
    /// Read the file if its text changed since the last read. True when new settings were taken
    /// (<see cref="Current"/>); false when nothing changed or the new text was refused (logged, MP-C7).
    /// </summary>
    public bool Read()
    {
        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (_text is not null || Current is null)
            {
                log.Write($"pool file: {Path} cannot be read ({e.Message}); {(Current is null ? "no pool" : "the previous settings are kept")}");
            }

            _text = null;
            return false;
        }

        if (text == _text)
        {
            return false;
        }

        _text = text;
        var config = PoolConfig.Parse(text);
        foreach (var line in config.Skipped)
        {
            log.Write(line);
        }

        if (!config.Valid || config.Projects.Count == 0)
        {
            log.Write($"pool file: {Path} {(config.Valid ? "names no valid project" : "is not valid JSON")}; "
                + (Current is null ? "no pool" : "the previous settings are kept"));
            return false;
        }

        if (Current is { } before && (before.Port != config.Port || !string.Equals(before.Listen, config.Listen, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.LogDir, config.LogDir, StringComparison.OrdinalIgnoreCase)))
        {
            log.Write("pool file: port, listen and logDir take effect at the next start");
        }

        Current = config;
        return true;
    }

    /// <summary>
    /// 1033-C: write <paramref name="workspaces"/> as <paramref name="project"/>'s <c>workspaces</c> (names compare
    /// without case), changing that entry only and keeping every other entry and key. False when the file has no such
    /// project or cannot be read or written; the next tick re-reads it (MP-C7).
    /// </summary>
    public bool WriteWorkspaces(string project, int workspaces)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject;
            var entry = (root?["projects"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(p => p["name"] is JsonValue v && v.TryGetValue<string>(out var n) && string.Equals(n, project, StringComparison.OrdinalIgnoreCase));
            if (root is null || entry is null)
            {
                return false;
            }

            var existing = entry.FirstOrDefault(p => string.Equals(p.Key, "workspaces", StringComparison.OrdinalIgnoreCase)).Key;
            entry[existing ?? "workspaces"] = workspaces;
            var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var temp = Path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, Path, overwrite: true);
            log.Write($"pool file: {project} now has {workspaces} workspaces ({Path})");
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Write($"pool file: the workspaces of {project} could not be saved to {Path} ({e.Message})");
            return false;
        }
    }

    /// <summary>
    /// The Worker types panel's Save in a pool (MP-C1, WT-C8): replace the file's <c>pool</c> section with
    /// <paramref name="poolSection"/> (a JSON object), keeping everything else. False when either is not JSON.
    /// </summary>
    public bool WritePool(string poolSection)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject;
            var section = JsonNode.Parse(poolSection) as JsonObject;
            if (root is null || section is null)
            {
                return false;
            }

            root["pool"] = section;
            var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var temp = Path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, Path, overwrite: true);
            log.Write($"pool file: the worker types were saved to {Path}");
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Write($"pool file: the worker types could not be saved to {Path} ({e.Message})");
            return false;
        }
    }
}
