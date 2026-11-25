// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Processes;

/// <summary>retries.json (TIME-22): the runs counted against each card, kept across a restart.</summary>
public static class RetriesFile
{
    /// <summary>Read the counts; a missing or unreadable file gives none.</summary>
    public static Dictionary<string, int> Load(IStateFiles files)
    {
        var retries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (files.Read(StateFile.Retries) is { } text && JsonNode.Parse(text) is JsonObject o)
            {
                foreach (var (id, value) in o)
                {
                    if (value is JsonValue v && v.TryGetValue<int>(out var n))
                    {
                        retries[id] = n;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable file counts as none.
        }
        return retries;
    }

    /// <summary>Write the counts (every tick).</summary>
    public static void Save(IStateFiles files, IReadOnlyDictionary<string, int> retries)
    {
        var o = new JsonObject();
        foreach (var (id, n) in retries)
        {
            o[id] = n;
        }
        files.Write(StateFile.Retries, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
