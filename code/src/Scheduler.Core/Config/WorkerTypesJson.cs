// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Config;

/// <summary>
/// The <c>pool</c> section of workers.json (WT-C1 to WT-C6), the migration that derives the types
/// from today's <c>slots</c> and <c>workers</c> in memory (WT-C10), and the text the Worker types
/// panel writes back on its first Save (WT-C11). Everything here is text in, objects out: which
/// file it came from, and when its text changed, is the caller's (WT-C7, WT-C8).
/// </summary>
public static class WorkerTypesJson
{
    /// <summary>The <c>about</c> text of the pool form (WT-C11).</summary>
    public const string About =
        "The scheduler's worker types: how many workers may run at once in all (pool maxWorkers), and "
        + "each type's cap (max; 0 is off), the agent it runs (an entry of scheduler.json's agents; empty "
        + "means its worker section), model, model server (an OpenAI-style /v1 endpoint), and metrics "
        + "address. In a pool a type may also name the projects it serves (projects); empty means every "
        + "project. A card's Worker: line names a type by its name or its model; a card with none goes to "
        + "the default type (pool defaultWorker). The scheduler reads this file from main; changes made from its "
        + "page are committed and pushed for you, so a cap change is a commit.";

    /// <summary>
    /// Whether the workers.json text has a <c>pool</c> at all, which is what decides between the two
    /// forms (WT-C10). Text that is not JSON has none.
    /// </summary>
    public static bool HasPool(string? text)
    {
        try
        {
            return HasPool(ConfigJson.Root(text));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Whether an already-read workers.json has a <c>pool</c> (WT-C10).</summary>
    public static bool HasPool(JsonObject? root) => ConfigJson.Has(root, "pool");

    /// <summary>
    /// The types of the file's <c>pool</c> (WT-C1 to WT-C6), or null when the text is not JSON, has no
    /// <c>pool</c>, or has one with no valid type - the last two are logged and the caller keeps the
    /// settings it had (WT-C6).
    /// </summary>
    public static WorkerPool? Parse(string? text, SchedulerConfig config, ISchedulerLog? log = null)
    {
        JsonObject? root;
        try
        {
            root = ConfigJson.Root(text);
        }
        catch (JsonException)
        {
            log?.Write("workers.json is not valid JSON; keeping the previous worker types");
            return null;
        }

        return Parse(root, config, log);
    }

    /// <summary>The types of an already-read workers.json's <c>pool</c> (WT-C1 to WT-C6); null as above.</summary>
    public static WorkerPool? Parse(JsonObject? root, SchedulerConfig config, ISchedulerLog? log = null)
    {
        var pool = ConfigJson.GetObject(root, "pool");
        if (pool is null)
        {
            return null;
        }

        var maxWorkers = Total(config, ConfigJson.GetInt(pool, "maxWorkers"));
        var types = new List<WorkerType>();
        var taken = new HashSet<string>(Text.Comparer);
        var section = ConfigJson.GetObject(pool, "types");
        var areasSeen = false;
        string? firstPlain = null;   // 1146-A: the migration's default, the first type the old premium flag left off
        if (section is not null)
        {
            // WT-C5: the file's order is the order types are offered to a card, so it is kept.
            foreach (var entry in section)
            {
                var name = entry.Key;
                if (Refused(name) is { } why)
                {
                    log?.Write($"worker type {name} is refused: {why}");
                    continue;
                }

                if (!taken.Add(name))
                {
                    log?.Write($"worker type {name} is a duplicate of one already listed; it is skipped");
                    continue;
                }

                var v = entry.Value as JsonObject;
                if (v is not null && v.ContainsKey("areas"))
                {
                    areasSeen = true;
                }

                // 1146-A: premium is no longer a setting; it is read only to choose the default of a file that names none.
                if (firstPlain is null && ConfigJson.GetBool(v, "premium") is not true)
                {
                    firstPlain = name;
                }

                var max = ConfigJson.GetInt(v, "max");
                types.Add(new WorkerType
                {
                    Name = name,
                    // WT-C3: max is 0 or more; missing, or not that, gives the total as WT-C1 says for the total.
                    Max = max is >= 0 ? max.Value : maxWorkers,
                    Agent = ConfigJson.GetString(v, "agent") ?? "",
                    Model = ConfigJson.GetString(v, "model") ?? "",
                    Endpoint = ConfigJson.GetString(v, "endpoint") ?? "",
                    MetricsUrl = ConfigJson.GetString(v, "metricsUrl") ?? "",
                    Projects = ConfigJson.GetStringList(v, "projects", dropEmpty: true) ?? [],
                });
            }
        }

        if (areasSeen)
        {
            log?.Write("workers.json: worker types no longer have an areas list (969-A); it is ignored, and the planner picks a type with the card's Worker: line");
        }

        if (types.Count == 0)
        {
            log?.Write("the pool has no valid worker type; keeping the previous worker types");
            return null;
        }

        // 1146-A: defaultWorker names the type a card with no Worker: line goes to. A file that names
        // none (written before it existed) gets the first type its premium flag left off, else the
        // first type; one that names no listed type is logged and falls back the same way.
        var fallback = firstPlain ?? types[0].Name;
        var named = (ConfigJson.GetString(pool, "defaultWorker") ?? "").Trim();
        var defaultWorker = types.FirstOrDefault(t => Text.Same(t.Name, named))?.Name;
        if (defaultWorker is null)
        {
            if (named.Length > 0)
            {
                log?.Write($"workers.json: defaultWorker {named} names no worker type; the default is {fallback}");
            }

            defaultWorker = fallback;
        }

        // WT-C6: the caps may add up to more than the total; that is the point of them sharing it.
        return new WorkerPool { MaxWorkers = maxWorkers, Types = types, DefaultWorker = defaultWorker };
    }

    /// <summary>
    /// The types today's <c>slots</c> and <c>workers</c> migrate to (WT-C10), given which slots are
    /// enabled in workers-state.json. For a file with no <c>pool</c>: the caller decides that with
    /// <see cref="HasPool(string)"/>. The migration is in memory only: nothing is written (WT-C11).
    /// </summary>
    public static WorkerPool FromWorkersJson(
        SchedulerConfig config,
        string? text,
        IReadOnlyDictionary<int, bool>? enabled = null,
        Func<string, string, string>? modelShort = null,
        ISchedulerLog? log = null)
    {
        JsonObject? root;
        try
        {
            root = ConfigJson.Root(text);
        }
        catch (JsonException)
        {
            log?.Write("workers.json is not valid JSON; keeping the previous worker types");
            root = null;
        }

        return WithSlotDefault(FromSlots(config, ConfigJson.GetInt(root, "slots") ?? 0, ReadWorkers(root), enabled, modelShort), root);
    }

    /// <summary>
    /// 1146-A: a migrated <paramref name="pool"/> with its default type: the type of the first slot
    /// whose worker in <paramref name="root"/>'s <c>workers</c> the old premium flag left off, else the first type.
    /// </summary>
    public static WorkerPool WithSlotDefault(WorkerPool pool, JsonObject? root)
    {
        var section = ConfigJson.GetObject(root, "workers");
        for (var slot = 1; slot <= pool.Types.Sum(t => t.Slots.Count); slot++)
        {
            var v = section?.FirstOrDefault(p => p.Key == slot.ToString(CultureInfo.InvariantCulture)).Value as JsonObject;
            if (!Truthy(v is null ? null : GetNode(v, "premium")) && pool.Types.FirstOrDefault(t => t.Slots.Contains(slot)) is { } type)
            {
                return pool with { DefaultWorker = type.Name };
            }
        }

        return pool with { DefaultWorker = pool.Types.FirstOrDefault()?.Name ?? "" };
    }

    /// <summary>
    /// The migration of WT-C10 from the pieces the caller already has: how many workers there are,
    /// each slot's worker (an absent one is the <c>worker</c> section), and which slots are enabled.
    /// <paramref name="modelShort"/> is <c>Model-Short</c> of a model as a caller with models.json can
    /// give it: "" when nothing is known of that model, which is also what a caller without one gives.
    /// </summary>
    public static WorkerPool FromSlots(
        SchedulerConfig config,
        int slots,
        IReadOnlyDictionary<int, WorkerDefinition> workers,
        IReadOnlyDictionary<int, bool>? enabled = null,
        Func<string, string, string>? modelShort = null)
    {
        // WT-C10.1: the total is the file's slots, or what -Slots pinned for this run (WT-C1, CFG-8).
        var total = Math.Max(0, config.SlotsPinned ? config.Slots : slots);

        // WT-C10.2 and WT-C10.3: each slot is one worker, and workers with the same settings are one type.
        var groups = new List<Group>();
        var bySettings = new Dictionary<string, Group>(Text.Comparer);
        for (var slot = 1; slot <= total; slot++)
        {
            var worker = workers.GetValueOrDefault(slot) ?? new WorkerDefinition { Slot = slot };
            var key = SettingsKey(worker);
            if (!bySettings.TryGetValue(key, out var group))
            {
                group = new Group(worker);
                bySettings[key] = group;
                groups.Add(group);
            }

            group.Slots.Add(slot);
            if (IsEnabled(enabled, slot))
            {
                group.Enabled++;
            }
        }

        var types = new List<WorkerType>(groups.Count);
        var taken = new HashSet<string>(Text.Comparer);
        foreach (var group in groups)
        {
            var name = MigratedName(config, group, taken, modelShort);
            taken.Add(name);
            types.Add(new WorkerType
            {
                Name = name,
                Max = group.Enabled, // WT-C10.3: the cap is how many of its workers are enabled (WRK-3)
                Agent = group.First.Agent,
                Model = group.First.Model,
                Endpoint = group.First.Endpoint,
                MetricsUrl = group.First.MetricsUrl,
                Slots = [.. group.Slots], // WT-C10.5: kept for `Worker: <number>` (WT-3)
            });
        }

        return new WorkerPool { MaxWorkers = total, Types = types, Migrated = true };
    }

    /// <summary>
    /// The workers.json <c>workers</c> section as definitions (WRK-1): every numeric key with its
    /// name, agent, endpoint, model, metrics URL, and categories (upper-cased); premium is no longer read (1146-A).
    /// </summary>
    public static Dictionary<int, WorkerDefinition> ReadWorkers(JsonObject? root)
    {
        var workers = new Dictionary<int, WorkerDefinition>();
        var section = ConfigJson.GetObject(root, "workers");
        if (section is null)
        {
            return workers;
        }

        foreach (var property in section)
        {
            if (!int.TryParse(property.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
            {
                continue;
            }

            var v = property.Value as JsonObject;
            workers[slot] = new WorkerDefinition
            {
                Slot = slot,
                Name = ConfigJson.GetString(v, "name") ?? "",
                Agent = ConfigJson.GetString(v, "agent") ?? "",
                Endpoint = ConfigJson.GetString(v, "endpoint") ?? "",
                Model = ConfigJson.GetString(v, "model") ?? "",
                MetricsUrl = ConfigJson.GetString(v, "metricsUrl") ?? "",
                Categories = (ConfigJson.GetString(v, ConfigJson.Has(v, "categories") ? "categories" : "lanes") ?? "").ToUpperInvariant(),
            };
        }

        return workers;
    }

    /// <summary>
    /// workers.json in the pool form (WT-C11): <c>about</c>, then <c>pool</c> with <c>maxWorkers</c>
    /// and one line per type. No <c>slots</c> and no <c>workers</c>, and the migrated slot numbers are
    /// the old form's, so they are not written either. LF endings; the caller gives it main's own (WT-C7).
    /// </summary>
    public static string BuildPool(WorkerPool pool, string? about = null)
    {
        var lines = new List<string>
        {
            "{",
            $"  \"about\": {Quote(about ?? About)},",
            "  \"pool\": {",
            $"    \"maxWorkers\": {pool.MaxWorkers.ToString(CultureInfo.InvariantCulture)},",
            $"    \"defaultWorker\": {Quote(pool.DefaultType)},",
            "    \"types\": {",
        };

        for (var i = 0; i < pool.Types.Count; i++)
        {
            var t = pool.Types[i];
            var comma = i < pool.Types.Count - 1 ? "," : "";
            var projects = t.Projects.Count == 0
                ? ""
                : $", \"projects\": [{string.Join(", ", t.Projects.Select(Quote))}]";
            lines.Add(
                $"      {Quote(t.Name)}: {{ \"max\": {t.Max.ToString(CultureInfo.InvariantCulture)},"
                + $" \"agent\": {Quote(t.Agent)}, \"model\": {Quote(t.Model)},"
                + $" \"endpoint\": {Quote(t.Endpoint)}, \"metricsUrl\": {Quote(t.MetricsUrl)}{projects} }}{comma}");
        }

        lines.AddRange(["    }", "  }", "}", ""]);
        return string.Join('\n', lines);
    }

    /// <summary>WT-C1: an integer of 1 or more, else scheduler.json's <c>slots</c>; <c>-Slots</c> pins it (CFG-8).</summary>
    private static int Total(SchedulerConfig config, int? maxWorkers) =>
        config.SlotsPinned
            ? Math.Max(0, config.Slots)
            : maxWorkers is > 0 ? maxWorkers.Value : Math.Max(0, config.Slots);

    /// <summary>Why WT-C2 refuses a type's name, or null when it is usable.</summary>
    private static string? Refused(string name) => name switch
    {
        { Length: 0 } => "a type must be named",
        _ when name.All(char.IsDigit) => "a name of only digits is a slot number, which means something else in a Worker: line (WT-3)",
        _ => null,
    };

    /// <summary>WT-C10.3: the settings that make two workers one type; categories compare upper-cased.</summary>
    private static string SettingsKey(WorkerDefinition w) =>
        string.Join('\n', w.Agent, w.Endpoint, w.Model, w.MetricsUrl, w.Categories.ToUpperInvariant());

    // A slot the local state never saw is enabled (WRK-2).
    private static bool IsEnabled(IReadOnlyDictionary<int, bool>? enabled, int slot) =>
        enabled is null || !enabled.TryGetValue(slot, out var on) || on;

    /// <summary>
    /// WT-C10.4: the first member's name; else <c>Model-Short</c> of the type's effective model (WT-C4);
    /// else <c>worker</c>. A name WT-C2 would refuse gets <c>worker-</c> in front, and a name already
    /// taken gets <c>-2</c>, <c>-3</c>, ...
    /// </summary>
    private static string MigratedName(SchedulerConfig config, Group group, HashSet<string> taken, Func<string, string, string>? modelShort)
    {
        var name = group.First.Name.Trim();
        if (name.Length == 0)
        {
            var effective = WorkerSelection.Effective(config, AsType(group.First));
            name = modelShort?.Invoke(effective.Model, effective.Endpoint) ?? "";
        }

        if (name.Length == 0)
        {
            name = "worker";
        }
        else if (Refused(name) is not null)
        {
            name = "worker-" + name;
        }

        var only = name;
        for (var n = 2; taken.Contains(only); n++)
        {
            only = $"{name}-{n}";
        }

        return only;
    }

    // A worker as a type, so its effective worker is resolved the way a type's is (WT-C4).
    private static WorkerType AsType(WorkerDefinition w) => new()
    {
        Name = "",
        Agent = w.Agent,
        Model = w.Model,
        Endpoint = w.Endpoint,
        MetricsUrl = w.MetricsUrl,
    };

    private static JsonNode? GetNode(JsonObject obj, string name) =>
        obj.FirstOrDefault(p => Text.Same(p.Key, name)).Value;

    // PowerShell's [bool], as workers.json's old premium was read (now only to choose the default, 1146-A): text is true when not empty
    // ("false" included), a number when not 0.
    private static bool Truthy(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False or JsonValueKind.Null or null => false,
        JsonValueKind.String => node.GetValue<string>().Length > 0,
        JsonValueKind.Number => node.AsValue().TryGetValue<double>(out var d) && d != 0,
        _ => true,
    };

    // Json-Str (L2629): the two characters that would end the string.
    private static string Quote(string text) =>
        '"' + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    /// <summary>The workers WT-C10.3 found to be one type: what they have in common, and their slots.</summary>
    private sealed class Group(WorkerDefinition first)
    {
        public WorkerDefinition First { get; } = first;

        public List<int> Slots { get; } = [];

        public int Enabled { get; set; }
    }
}
