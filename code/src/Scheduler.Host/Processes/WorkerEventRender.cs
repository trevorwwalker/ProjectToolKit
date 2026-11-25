// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Scheduler.Host.Processes;

/// <summary>
/// A json-format agent's events as readable lines (460-B), ports of schedule.ps1's
/// <c>Render-Event</c> (Copilot) and <c>Render-Claude</c> (Claude stream-json), same marks:
/// the model's own text (<c>* </c>), its tool calls (<c>&gt; </c>), failed tool results
/// (<c>  x </c>), its reasoning (<c>  ~ </c>), slow calls and the session's end, and errors and the
/// CLI's own lines (<c>! </c>). Everything else is left out.
/// </summary>
public static class WorkerEventRender
{
    private static readonly Regex Shown = new(@"""type"":""(assistant\.message|assistant\.reasoning|tool\.execution_complete|model\.call_finished|[a-z_.]*error[a-z_.]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex Paragraphs = new(@"(?:\r?\n){2,}", RegexOptions.CultureInvariant);
    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex NewLine = new(@"\r?\n", RegexOptions.CultureInvariant);

    /// <summary>One Copilot CLI event line as readable lines (<c>Render-Event</c>).</summary>
    public static IReadOnlyList<string> Copilot(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return [];
        }

        if (line[0] != '{')
        {
            return ["! " + Clip(line, 400)]; // the CLI itself, on stderr
        }

        if (!Shown.IsMatch(line))
        {
            return [];
        }

        using var doc = Parse(line);
        if (doc is null)
        {
            return [];
        }

        var e = doc.RootElement;
        var type = Str(e, "type");
        var d = e.TryGetProperty("data", out var data) ? data : default;
        switch (type)
        {
            case "assistant.reasoning":
                return Thoughts(Str(d, "content"));
            case "assistant.message":
            {
                var o = new List<string>();
                var content = Str(d, "content");
                if (content.Length > 0)
                {
                    o.AddRange(NewLine.Split(content).Select(l => "* " + l));
                }

                foreach (var t in Items(d, "toolRequests"))
                {
                    var a = Prop(t, "arguments");
                    var what = First(Str(a, "command"), Str(a, "path"));
                    var label = First(Str(t, "intentionSummary"), Str(t, "name"));
                    o.Add("> " + label + What(what));
                }

                return o;
            }

            case "tool.execution_complete":
            {
                if (Truthy(Prop(d, "success")))
                {
                    return [];
                }

                var result = Str(Prop(d, "result"), "content");
                var c = result.Length > 0 ? result
                    : Prop(d, "error") is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } err ? err.GetRawText() : "";
                return ["  x " + Clip(FirstLines(c), 200)];
            }

            case "model.call_finished":
            {
                var ms = Prop(d, "dispatchDurationMs");
                if (ms.ValueKind == JsonValueKind.Number && ms.GetDouble() > 30000)
                {
                    var s = (int)Math.Round(ms.GetDouble() / 1000);
                    return [string.Create(CultureInfo.InvariantCulture, $"  (the model took {s} s to answer)")];
                }

                return [];
            }

            default:
                return ["! " + type + ": " + Clip(d.ValueKind == JsonValueKind.Undefined ? "" : d.GetRawText(), 300)];
        }
    }

    /// <summary>One Claude Code stream-json line as readable lines (<c>Render-Claude</c>).</summary>
    public static IReadOnlyList<string> Claude(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return [];
        }

        if (line[0] != '{')
        {
            return ["! " + Clip(line, 400)];
        }

        using var doc = Parse(line);
        if (doc is null)
        {
            return [];
        }

        var e = doc.RootElement;
        var o = new List<string>();
        switch (Str(e, "type"))
        {
            case "assistant":
                foreach (var c in Items(Prop(e, "message"), "content"))
                {
                    switch (Str(c, "type"))
                    {
                        case "text":
                            o.AddRange(NewLine.Split(Str(c, "text")).Select(l => "* " + l));
                            break;
                        case "thinking":
                            o.AddRange(Thoughts(Str(c, "thinking")));
                            break;
                        case "tool_use":
                        {
                            var a = Prop(c, "input");
                            var what = First(Str(a, "command"), Str(a, "file_path"), Str(a, "pattern"), Str(a, "path"));
                            var label = First(Str(a, "description"), Str(c, "name"));
                            o.Add("> " + label + What(what));
                            break;
                        }
                    }
                }

                return o;
            case "user":
                foreach (var c in Items(Prop(e, "message"), "content"))
                {
                    if (Str(c, "type") != "tool_result" || !Truthy(Prop(c, "is_error")))
                    {
                        continue;
                    }

                    var content = Prop(c, "content");
                    var txt = content.ValueKind == JsonValueKind.Array
                        ? string.Join(' ', content.EnumerateArray().Select(x => Str(x, "text")))
                        : Str(c, "content");
                    o.Add("  x " + Clip(FirstLines(txt), 200));
                }

                return o;
            case "result":
            {
                // is_error marks a failed session even when subtype says success (a sign-in that expired, say).
                if (Truthy(Prop(e, "is_error")))
                {
                    return ["! session failed: " + Clip(Str(e, "result"), 300)];
                }

                var cost = Prop(e, "total_cost_usd");
                var costText = cost.ValueKind == JsonValueKind.Number
                    ? ", $" + cost.GetDouble().ToString("0.00", CultureInfo.InvariantCulture)
                    : "";
                return [$"  (session ended: {Str(e, "subtype")}, {Str(e, "num_turns")} turns{costText})"];
            }

            default:
                return [];
        }
    }

    /// <summary>The text cut to <paramref name="n"/> characters, marked when cut (<c>Clip</c>).</summary>
    public static string Clip(string s, int n) => s.Length > n ? s[..n] + " ..." : s;

    private static IReadOnlyList<string> Thoughts(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        // The model's own thinking is what the developer follows: shown whole (up to 3000
        // characters), paragraph by paragraph.
        return Paragraphs.Split(Clip(text.Trim(), 3000))
            .Select(p => Space.Replace(p, " ").Trim())
            .Where(p => p.Length > 0)
            .Select(p => "  ~ " + p)
            .ToList();
    }

    private static string What(string what) => what.Length > 0 ? ": " + Clip(Space.Replace(what, " "), 80) : "";

    private static string FirstLines(string text) =>
        string.Join(" | ", NewLine.Split(text).Where(l => l.Trim().Length > 0).Take(2));

    private static string First(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";

    private static JsonDocument? Parse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static IEnumerable<JsonElement> Items(JsonElement e, string name)
    {
        var v = Prop(e, name);
        return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [];
    }

    // [string]$x in PowerShell: a string as it is, a number or boolean as its text, nothing as "".
    private static string Str(JsonElement e, string name)
    {
        var v = Prop(e, name);
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => "",
        };
    }

    private static bool Truthy(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => (v.GetString() ?? "").Length > 0,
        JsonValueKind.Number => v.GetDouble() != 0,
        JsonValueKind.Object or JsonValueKind.Array => true,
        _ => false,
    };
}
