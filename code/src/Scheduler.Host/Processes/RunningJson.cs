// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;

namespace Scheduler.Host.Processes;

/// <summary>
/// running.json (ADOPT-1): per worker the slot, pid, card, category, id, logs, raw position, start,
/// job, time box, stop state, kill time, messages flag, model, endpoint, metrics address, agent
/// and figures, under the names <c>Save-Running</c> gave them (<c>log</c> is the raw log,
/// <c>view</c> the readable one).
/// </summary>
public static class RunningJson
{
    /// <summary>The text for these workers; <paramref name="stats"/> gives a slot's figures.</summary>
    public static string Write(IEnumerable<RunningWorker> workers, Func<int, WorkerStats?>? stats = null)
    {
        var list = new JsonArray();
        foreach (var w in workers)
        {
            var s = stats?.Invoke(w.Slot);
            list.Add(new JsonObject
            {
                ["slot"] = w.Slot,
                ["pid"] = w.Pid,
                ["card"] = w.Card,
                ["lane"] = w.Category,
                ["id"] = w.Id,
                ["log"] = w.RawLog,
                ["view"] = w.Log,
                ["rawPos"] = w.RawPosition,
                ["started"] = w.Start.ToString("o", CultureInfo.InvariantCulture),
                ["job"] = w.Job,
                ["timebox"] = w.TimeBox,
                ["stopping"] = w.StopAt is not null,
                ["stopAsked"] = w.StopAt is null ? "" : w.StopAt.Value.ToString("o", CultureInfo.InvariantCulture),
                ["stopWhy"] = w.StopWhy,
                ["grace"] = w.StopGraceMinutes,
                ["killedAt"] = w.KillTime is null ? "" : w.KillTime.Value.ToString("o", CultureInfo.InvariantCulture),
                ["msgs"] = w.Msgs,
                ["model"] = w.Model,
                ["endpoint"] = w.Endpoint,
                ["metricsUrl"] = w.MetricsUrl,
                ["agent"] = w.Agent,
                ["type"] = w.Type,
                ["stats"] = s is null ? null : new JsonObject
                {
                    ["Calls"] = s.Calls,
                    ["Tools"] = s.Tools,
                    ["ToolFails"] = s.ToolFails,
                    ["CtxChars"] = s.CtxChars,
                    ["Compactions"] = s.Compactions,
                    ["GenTokens"] = s.GenTokens,
                    ["GenSeconds"] = s.GenSeconds,
                    ["LastTps"] = s.LastTps,
                    ["LastTtft"] = s.LastTtft,
                },
            });
        }
        return list.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The entries in the text; one without a pid or a start, or text that is not JSON, gives none.
    /// A single object counts as a list of one, as the script's PowerShell made it.
    /// </summary>
    public static IReadOnlyList<RunningEntry> Read(string? text)
    {
        var result = new List<RunningEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return result;
        }
        IEnumerable<JsonNode?> items = root switch
        {
            JsonArray a => a,
            JsonObject => [root],
            _ => [],
        };
        foreach (var item in items)
        {
            if (item is JsonObject o && Entry(o) is { } entry)
            {
                result.Add(entry);
            }
        }
        return result;
    }

    private static RunningEntry? Entry(JsonObject o)
    {
        var pid = Int(o, "pid");
        var started = Time(o, "started");
        if (pid is null or 0 || started is null)
        {
            return null;
        }
        var worker = new RunningWorker
        {
            Slot = Int(o, "slot") ?? 0,
            Pid = pid.Value,
            Start = started.Value,
            Card = Str(o, "card"),
            Category = Str(o, "lane"),
            Id = Str(o, "id"),
            RawLog = Str(o, "log"),
            Log = Str(o, "view") is { Length: > 0 } view ? view : Str(o, "log"),
            RawPosition = Long(o, "rawPos"),
            Job = Str(o, "job"),
            TimeBox = Int(o, "timebox"),
            StopAt = Time(o, "stopAsked") ?? (Bool(o, "stopping") ? started : null),
            StopWhy = Str(o, "stopWhy"),
            StopGraceMinutes = Int(o, "grace") ?? 0,
            KillTime = Time(o, "killedAt"),
            Msgs = Bool(o, "msgs"),
            Model = Str(o, "model"),
            Endpoint = Str(o, "endpoint"),
            MetricsUrl = Str(o, "metricsUrl"),
            Agent = Str(o, "agent"),
            Type = Str(o, "type"),   // WT-W5: an entry written before types has none
        };
        WorkerStats? stats = null;
        if (o["stats"] is JsonObject s)
        {
            stats = new WorkerStats
            {
                Calls = Int(s, "Calls") ?? 0,
                Tools = Int(s, "Tools") ?? 0,
                ToolFails = Int(s, "ToolFails") ?? 0,
                CtxChars = Dbl(s, "CtxChars"),
                Compactions = Int(s, "Compactions") ?? 0,
                GenTokens = Dbl(s, "GenTokens"),
                GenSeconds = Dbl(s, "GenSeconds"),
                LastTps = Dbl(s, "LastTps"),
                LastTtft = Dbl(s, "LastTtft"),
            };
        }
        return new RunningEntry { Worker = worker, Stats = stats };
    }

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v ? v.ToString() : "";

    private static int? Int(JsonObject o, string key) =>
        o[key] is JsonValue v && int.TryParse(v.ToString(), CultureInfo.InvariantCulture, out var i) ? i : null;

    private static long Long(JsonObject o, string key) =>
        o[key] is JsonValue v && long.TryParse(v.ToString(), CultureInfo.InvariantCulture, out var l) ? l : 0;

    private static double Dbl(JsonObject o, string key) =>
        o[key] is JsonValue v && double.TryParse(v.ToString(), CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static DateTimeOffset? Time(JsonObject o, string key) =>
        Str(o, key) is { Length: > 0 } s
        && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
}
