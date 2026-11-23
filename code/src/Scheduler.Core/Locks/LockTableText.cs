using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scheduler.Core.Locks;

/// <summary>
/// The lock table on disk (design <c>dynamic-locks.md</c>, DL-20 to DL-22): one JSON file per
/// repository, <c>&lt;log&gt;\locks\&lt;repository key&gt;.json</c>, written only by the scheduler (tmp then
/// rename), and the restart rules (DL-21, with 1018-D's rule for a claim that had not landed).
/// Pure: Host reads and writes the file.
/// </summary>
public static class LockTableText
{
    /// <summary>The folder under the scheduler's log folder that holds the tables.</summary>
    public const string FolderName = "locks";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// The table's file name for a repository: its path, case and slashes folded, every other
    /// character that is not a letter or digit as <c>-</c> (DL-22: per repository, not per project).
    /// </summary>
    public static string FileName(string repo)
    {
        var norm = (repo ?? "").Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in norm)
        {
            var ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
            if (ok)
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var key = sb.ToString().Trim('-');
        return (key.Length == 0 ? "repo" : key) + ".json";
    }

    /// <summary>The table's path under <paramref name="logDir"/>.</summary>
    public static string PathIn(string logDir, string repo) => Path.Combine(logDir, FolderName, FileName(repo));

    /// <summary>The table as JSON.</summary>
    public static string Write(LockTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var cards = new JsonArray();
        foreach (var c in table.Cards)
        {
            var row = new JsonObject
            {
                ["card"] = c.Card,
                ["project"] = c.Project,
                ["slot"] = c.Slot,
                ["prio"] = c.Age?.Prio,
                ["num"] = c.Age?.Num,
                ["state"] = c.State.ToString().ToLowerInvariant(),
                ["held"] = new JsonArray([.. c.Held.Select(e => (JsonNode)new JsonObject
                {
                    ["path"] = e.Path,
                    ["kind"] = e.Kind.ToString().ToLowerInvariant(),
                    ["granted"] = Time(e.Granted),
                })]),
                ["except"] = Strings(c.Except),
                ["addOnly"] = Strings(c.AddOnly),
                ["remembered"] = Strings(c.Remembered),
            };
            if (c.Open is { } o)
            {
                row["open"] = new JsonObject
                {
                    ["file"] = o.FileName,
                    ["paths"] = Strings(o.Paths),
                    ["why"] = o.Why,
                    ["asked"] = Time(o.Asked),
                    ["deadline"] = Time(o.Deadline),
                };
            }

            cards.Add(row);
        }

        var root = new JsonObject { ["repo"] = table.Repo, ["auditedCommit"] = table.AuditedCommit, ["cards"] = cards };
        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// DL-21: reads a table. Missing (<paramref name="json"/> null) or unreadable, it starts empty for
    /// <paramref name="repo"/> and the error is the log line to write; read, the error is empty.
    /// </summary>
    public static (LockTable Table, string Error) Read(string? json, string repo = "")
    {
        if (json is null)
        {
            return (LockTable.Empty(repo), "no lock table: starting empty; running cards hold their claims, the audit starts at main's head");
        }

        try
        {
            return (Parse(json, repo), "");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return (LockTable.Empty(repo), $"lock table unreadable ({e.Message}): starting empty; running cards hold their claims, the audit starts at main's head");
        }
    }

    /// <summary>
    /// DL-21: after a restart, cards whose workers were adopted keep their held sets and open requests
    /// (deadlines carry over); every other card that held its set is released as ended, its granted
    /// entries becoming its remembered set. A card still starting (its claim had not landed, 995-B)
    /// has no worker to adopt and is released with nothing to remember (1018-D). Returns the table and log lines.
    /// </summary>
    public static (LockTable Table, IReadOnlyList<string> Log) Restart(LockTable table, IEnumerable<string> adopted)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(adopted);
        var keep = new HashSet<string>(adopted, Scheduler.Core.Contracts.Text.Comparer);
        var log = new List<string>();
        foreach (var c in table.Cards.Where(c => c.State != CardLockState.Released).ToList())
        {
            if (c.State == CardLockState.Starting)
            {
                table = table.Release(c.Card, LockRelease.Ended);
                log.Add($"locks: {c.Card} released at restart: its claim had not landed");
            }
            else if (!keep.Contains(c.Card))
            {
                table = table.Release(c.Card, LockRelease.Ended);
                log.Add($"locks: {c.Card} released at restart: its worker was not adopted");
            }
        }

        return (table, log);
    }

    private static LockTable Parse(string json, string repo)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("not a JSON object");
        var cards = new List<CardLocks>();
        foreach (var node in root["cards"] as JsonArray ?? [])
        {
            var o = node as JsonObject ?? throw new FormatException("a card is not an object");
            var name = Str(o, "card");
            if (name.Length == 0)
            {
                throw new FormatException("a card with no name");
            }

            var held = new List<LockEntry>();
            foreach (var e in o["held"] as JsonArray ?? [])
            {
                var eo = e as JsonObject ?? throw new FormatException($"{name}: a held entry is not an object");
                held.Add(new LockEntry(Str(eo, "path"), Enum<LockKind>(Str(eo, "kind"), name), When(eo, "granted")));
            }

            OpenLockRequest? open = null;
            if (o["open"] is JsonObject oo)
            {
                open = new OpenLockRequest(Str(oo, "file"), List(oo, "paths"), Str(oo, "why"), When(oo, "asked"), When(oo, "deadline"));
            }

            cards.Add(new CardLocks
            {
                Card = name,
                Project = Str(o, "project"),
                Slot = o["slot"]?.GetValue<int>(),
                Age = o["num"] is null ? null : new CardAge(o["prio"]?.GetValue<int>() ?? 1, o["num"]!.GetValue<long>()),
                State = Enum<CardLockState>(Str(o, "state"), name),
                Held = held,
                Except = List(o, "except"),
                AddOnly = List(o, "addOnly"),
                Remembered = List(o, "remembered"),
                Open = open,
            });
        }

        var r = Str(root, "repo");
        return new LockTable { Repo = r.Length > 0 ? r : repo, AuditedCommit = Str(root, "auditedCommit"), Cards = [.. cards.OrderBy(c => c.Card, StringComparer.Ordinal)] };
    }

    private static JsonArray Strings(IEnumerable<string> items) => new([.. items.Select(s => (JsonNode)JsonValue.Create(s))]);

    private static string Time(DateTimeOffset t) => t.ToString("o", CultureInfo.InvariantCulture);

    private static string Str(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";

    private static IReadOnlyList<string> List(JsonObject o, string key) =>
        [.. (o[key] as JsonArray ?? []).Select(n => n?.GetValue<string>() ?? throw new FormatException($"{key}: an empty entry"))];

    private static DateTimeOffset When(JsonObject o, string key) =>
        DateTimeOffset.Parse(Str(o, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static T Enum<T>(string text, string card)
        where T : struct, Enum =>
        System.Enum.TryParse<T>(text, ignoreCase: true, out var v) && System.Enum.IsDefined(v)
            ? v
            : throw new FormatException($"{card}: {typeof(T).Name} '{text}' is not known");
}
