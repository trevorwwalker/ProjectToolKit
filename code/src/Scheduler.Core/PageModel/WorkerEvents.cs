using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.PageModel;

/// <summary>
/// A worker's raw log events into readable lines (PAGE-10): a faithful port of <c>Render-Event</c>
/// (schedule.ps1 L1899) for Copilot's jsonl and <c>Render-Claude</c> (L1854) for Claude's
/// stream-json. Both give the model's reasoning the mark <c>~</c>, its messages <c>*</c>, its tool
/// calls <c>&gt;</c>, failed tools <c>x</c>, a model call over 30 s, and anything the CLI itself
/// printed <c>!</c>; Claude adds the session's end with its cost. Everything else - token deltas,
/// status chatter - is left out. What a line looks like on the page is the mark's
/// <see cref="LiveEvent.Prefix"/> and <see cref="LiveEvent.Text"/>; the two-space indent the
/// reasoning, failed tools, slow calls and session end carry belongs to the view.
/// </summary>
public static class WorkerEvents
{
    private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex ShowRx = new(@"""type"":""(assistant\.message|assistant\.reasoning|tool\.execution_complete|model\.call_finished|[a-z_.]*error[a-z_.]*)""", Ci);
    private static readonly Regex BlankLineRx = new(@"(?:\r?\n){2,}", Ci);
    private static readonly Regex LineRx = new(@"\r?\n", Ci);
    private static readonly Regex SpaceRx = new(@"\s+", Ci);

    /// <summary>
    /// The lines one raw log line gives (L2441). Only lines the scanner showed should be passed,
    /// but each renderer re-checks, as the two PowerShell functions do. A worker whose agent is
    /// plain text is read as Copilot output is, which is what the scheduler does (L2438).
    /// </summary>
    public static IReadOnlyList<LiveEvent> Render(AgentFormat format, string line) =>
        format switch
        {
            AgentFormat.Claude => RenderClaude(line),
            AgentFormat.Codex => RenderCodex(line),
            AgentFormat.OpenCode => RenderOpenCode(line),
            _ => RenderCopilot(line),
        };

    /// <summary>The notice every Codex run prints first; not an error (AG-3).</summary>
    private const string CodexStdinNotice = "Reading additional input from stdin";

    private static readonly Regex ShellWrapperRx = new(@"^\s*""?[^""]*?(?:pwsh|powershell)(?:\.exe)?""?\s+-Command\s+", Ci);

    /// <summary>
    /// The lines of a Codex worker's raw log worth showing (AG-3, AG-14): the CLI's own text (but not
    /// its stdin notice) and the events <see cref="RenderCodex"/> renders. Fills <paramref name="stats"/>:
    /// calls, tools, failed tools and generated tokens.
    /// </summary>
    public static WorkerScanResult ScanCodex(string text, WorkerStats stats, string partial)
    {
        var show = new List<string>();
        foreach (var raw in text.Split((char)10))
        {
            var t = raw.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (t[0] != '{')
            {
                if (!t.StartsWith(CodexStdinNotice, StringComparison.Ordinal))
                {
                    show.Add(raw);
                }

                continue;
            }

            using var doc = Parse(t);
            if (doc is null)
            {
                continue;
            }

            var root = doc.RootElement;
            var type = Str(root, "type");
            switch (type)
            {
                case "turn.started":
                    stats.Calls++;
                    break;
                case "turn.completed":
                    stats.GenTokens += Num(Prop(root, "usage"), "output_tokens") ?? 0;
                    show.Add(raw);
                    break;
                case "item.completed":
                    var item = Prop(root, "item");
                    var itemType = Str(item, "type");
                    if (itemType is "command_execution" or "file_change" or "mcp_tool_call")
                    {
                        stats.Tools++;
                        if (CodexToolFailed(item))
                        {
                            stats.ToolFails++;
                        }
                    }

                    if (itemType is not ("web_search" or "todo_list"))
                    {
                        show.Add(raw);
                    }

                    break;
                default:
                    if (type is "turn.failed" or "error" || type.Contains("error", StringComparison.Ordinal))
                    {
                        show.Add(raw);
                    }

                    break;
            }
        }

        return new WorkerScanResult(partial ?? "", show);
    }

    private static bool CodexToolFailed(JsonElement? item) =>
        Str(item, "status") == "failed" || (Num(item, "exit_code") is { } code && code != 0);

    private static IReadOnlyList<LiveEvent> RenderCodex(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            return [];
        }

        if (line[0] != '{')
        {
            return line.StartsWith(CodexStdinNotice, StringComparison.Ordinal) ? [] : [Error(Clip(line, 400))];
        }

        using var doc = Parse(line);
        if (doc is null)
        {
            return [];
        }

        var root = doc.RootElement;
        var type = Str(root, "type");
        switch (type)
        {
            case "item.completed":
                var item = Prop(root, "item");
                switch (Str(item, "type"))
                {
                    case "agent_message":
                        return [.. LineRx.Split(Str(item, "text")).Select(l => new LiveEvent(LiveMark.Message, l))];
                    case "reasoning":
                        return [.. Paragraphs(Clip(Str(item, "text").Trim(), 3000)).Select(p => new LiveEvent(LiveMark.Reasoning, p))];
                    case "command_execution":
                        var command = ShellWrapperRx.Replace(Str(item, "command"), "").Trim();
                        if (command.Length >= 2 && command[0] == command[^1] && (command[0] == (char)39 || command[0] == (char)34))
                        {
                            command = command[1..^1];
                        }

                        var events = new List<LiveEvent> { new(LiveMark.ToolCall, ToolLine("shell", command)) };
                        if (CodexToolFailed(item))
                        {
                            events.Add(new LiveEvent(LiveMark.FailedTool, Clip(FirstTwo(Str(item, "aggregated_output")), 200)));
                        }

                        return events;
                    case "file_change":
                        var paths = string.Join(", ", Enumerate(Prop(item, "changes")).Select(c => Str(c, "path")).Where(p => p.Length > 0));
                        return [new LiveEvent(LiveMark.ToolCall, ToolLine("edit", paths))];
                    case "mcp_tool_call":
                        return [new LiveEvent(LiveMark.ToolCall, $"{Str(item, "server")}.{Str(item, "tool")}")];
                    case "error":
                        return [Error(Clip(Str(item, "message"), 300))];
                    default:
                        return [];
                }

            case "turn.completed":
                var usage = Prop(root, "usage");
                return [new LiveEvent(LiveMark.SessionEnd,
                    $"(session ended: {(long)(Num(usage, "input_tokens") ?? 0)} tokens in, {(long)(Num(usage, "output_tokens") ?? 0)} out)")];

            case "turn.failed":
                return [Error($"session failed: {Clip(Str(Prop(root, "error"), "message"), 300)}")];

            case "error":
                return [Error(Clip(Str(root, "message"), 300))];

            default:
                return type.Contains("error", StringComparison.Ordinal)
                    ? [Error($"{type}: {Clip(line, 300)}")]
                    : [];
        }
    }

    /// <summary>
    /// The lines of an OpenCode worker's raw log worth showing (AG-3, AG-14): the CLI's own text and the
    /// events <see cref="RenderOpenCode"/> renders. Fills <paramref name="stats"/>: calls, tools, failed
    /// tools and generated tokens.
    /// </summary>
    public static WorkerScanResult ScanOpenCode(string text, WorkerStats stats, string partial)
    {
        var show = new List<string>();
        foreach (var raw in text.Split((char)10))
        {
            var t = raw.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (t[0] != '{')
            {
                show.Add(raw);
                continue;
            }

            using var doc = Parse(t);
            if (doc is null)
            {
                continue;
            }

            var root = doc.RootElement;
            var type = Str(root, "type");
            var part = Prop(root, "part");
            switch (type)
            {
                case "step_start":
                    stats.Calls++;
                    break;
                case "step_finish":
                    stats.GenTokens += Num(Prop(part, "tokens"), "output") ?? 0;
                    if (Str(part, "reason") == "stop")
                    {
                        show.Add(raw);
                    }

                    break;
                case "text":
                    show.Add(raw);
                    break;
                case "tool_use":
                    var state = Prop(part, "state");
                    if (Str(state, "status") is "completed" or "error")
                    {
                        stats.Tools++;
                        if (OpenCodeToolFailed(state))
                        {
                            stats.ToolFails++;
                        }

                        show.Add(raw);
                    }

                    break;
                default:
                    if (type is "retry" or "error" || type.Contains("error", StringComparison.Ordinal))
                    {
                        show.Add(raw);
                    }

                    break;
            }
        }

        return new WorkerScanResult(partial ?? "", show);
    }

    // A failed command is "completed" with a non-zero exit in state.metadata.metadata.exit (recorded).
    private static bool OpenCodeToolFailed(JsonElement? state) =>
        Str(state, "status") == "error" || (Num(Prop(Prop(state, "metadata"), "metadata"), "exit") is { } code && code != 0);

    private static string OpenCodeErrorText(JsonElement? error)
    {
        var message = OrDefault(Str(error, "message"), Str(Prop(error, "data"), "message"));
        return OrDefault(message, OrDefault(Str(error, "name"), Str(error, "type")));
    }

    private static IReadOnlyList<LiveEvent> RenderOpenCode(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            return [];
        }

        if (line[0] != '{')
        {
            return [Error(Clip(line, 400))];
        }

        using var doc = Parse(line);
        if (doc is null)
        {
            return [];
        }

        var root = doc.RootElement;
        var type = Str(root, "type");
        var part = Prop(root, "part");
        switch (type)
        {
            case "text":
                return [.. LineRx.Split(Str(part, "text")).Select(l => new LiveEvent(LiveMark.Message, l))];

            case "reasoning":
                return [.. Paragraphs(Clip(Str(part, "text").Trim(), 3000)).Select(p => new LiveEvent(LiveMark.Reasoning, p))];

            case "tool_use":
                var state = Prop(part, "state");
                if (Str(state, "status") is not ("completed" or "error"))
                {
                    return [];
                }

                var input = Prop(state, "input");
                var what = OrDefault(Str(input, "command"), OrDefault(Str(input, "filePath"), Str(input, "path")));
                var events = new List<LiveEvent> { new(LiveMark.ToolCall, ToolLine(OrDefault(Str(part, "tool"), "tool"), what)) };
                if (OpenCodeToolFailed(state))
                {
                    var why = Str(state, "status") == "error" ? Str(state, "error") : Str(state, "output");
                    events.Add(new LiveEvent(LiveMark.FailedTool, Clip(FirstTwo(why), 200)));
                }

                return events;

            case "step_finish":
                if (Str(part, "reason") != "stop")
                {
                    return [];
                }

                var tokens = Prop(part, "tokens");
                var cost = Num(part, "cost") is { } c && c > 0 ? $", ${c.ToString("0.####", CultureInfo.InvariantCulture)}" : "";
                return [new LiveEvent(LiveMark.SessionEnd,
                    $"(session ended: {(long)(Num(tokens, "input") ?? 0)} tokens in, {(long)(Num(tokens, "output") ?? 0)} out{cost})")];

            case "retry":
                return [Error($"retrying (attempt {(long)(Num(root, "attempt") ?? 0)}): {Clip(OpenCodeErrorText(Prop(root, "error")), 300)}")];

            case "error":
                return [Error(Clip(OpenCodeErrorText(Prop(root, "error")), 300))];

            default:
                return type.Contains("error", StringComparison.Ordinal)
                    ? [Error($"{type}: {Clip(line, 300)}")]
                    : [];
        }
    }

    private static IReadOnlyList<LiveEvent> RenderCopilot(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            return [];
        }

        if (line[0] != '{')
        {
            return [Error(Clip(line, 400))];   // the CLI itself, on stderr
        }

        if (!ShowRx.IsMatch(line))
        {
            return [];
        }

        using var doc = Parse(line);
        if (doc is null)
        {
            return [];
        }

        var root = doc.RootElement;
        var type = Str(root, "type");
        var data = Prop(root, "data");
        switch (type)
        {
            case "assistant.reasoning":
                // The model's own thinking is what the developer follows: whole, up to 3000
                // characters, paragraph by paragraph.
                var content = Str(data, "content");
                if (content.Length == 0)
                {
                    return [];
                }

                return [.. Paragraphs(Clip(content.Trim(), 3000)).Select(p => new LiveEvent(LiveMark.Reasoning, p))];

            case "assistant.message":
                var outList = new List<LiveEvent>();
                var text = Str(data, "content");
                if (text.Length > 0)
                {
                    outList.AddRange(LineRx.Split(text).Select(l => new LiveEvent(LiveMark.Message, l)));
                }

                // One short line per tool call: what it is for, and a glimpse of the command.
                foreach (var t in Enumerate(Prop(data, "toolRequests")))
                {
                    var args = Prop(t, "arguments");
                    var what = OrDefault(Str(args, "command"), Str(args, "path"));
                    var label = OrDefault(Str(t, "intentionSummary"), Str(t, "name"));
                    outList.Add(new LiveEvent(LiveMark.ToolCall, ToolLine(label, what)));
                }

                return outList;

            case "tool.execution_complete":
                // Successful tool output is left to the model to describe; only failures are shown.
                if (IsTrue(Prop(data, "success")))
                {
                    return [];
                }

                var result = Str(Prop(data, "result"), "content");
                if (result.Length == 0)
                {
                    result = Raw(Prop(data, "error"));
                }

                return [new LiveEvent(LiveMark.FailedTool, Clip(FirstTwo(result), 200))];

            case "model.call_finished":
                var ms = Num(data, "dispatchDurationMs");
                return ms is > 30_000
                    ? [new LiveEvent(LiveMark.SlowModelCall, $"(the model took {(int)(ms.Value / 1000)} s to answer)")]
                    : [];

            default:
                return [Error($"{type}: {Clip(Raw(data), 300)}")];
        }
    }

    private static IReadOnlyList<LiveEvent> RenderClaude(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            return [];
        }

        if (line[0] != '{')
        {
            return [Error(Clip(line, 400))];
        }

        using var doc = Parse(line);
        if (doc is null)
        {
            return [];
        }

        var root = doc.RootElement;
        switch (Str(root, "type"))
        {
            case "assistant":
                var outList = new List<LiveEvent>();
                foreach (var c in Enumerate(Prop(Prop(root, "message"), "content")))
                {
                    switch (Str(c, "type"))
                    {
                        case "text":
                            outList.AddRange(LineRx.Split(Str(c, "text")).Select(l => new LiveEvent(LiveMark.Message, l)));
                            break;
                        case "thinking":
                            outList.AddRange(Paragraphs(Clip(Str(c, "thinking").Trim(), 3000))
                                .Select(p => new LiveEvent(LiveMark.Reasoning, p)));
                            break;
                        case "tool_use":
                            var input = Prop(c, "input");
                            var what = OrDefault(OrDefault(OrDefault(Str(input, "command"), Str(input, "file_path")), Str(input, "pattern")), Str(input, "path"));
                            var label = OrDefault(Str(input, "description"), Str(c, "name"));
                            outList.Add(new LiveEvent(LiveMark.ToolCall, ToolLine(label, what)));
                            break;
                    }
                }

                return outList;

            case "user":
                var fails = new List<LiveEvent>();
                foreach (var c in Enumerate(Prop(Prop(root, "message"), "content")))
                {
                    if (Str(c, "type") != "tool_result" || !IsTrue(Prop(c, "is_error")))
                    {
                        continue;
                    }

                    var content = Prop(c, "content");
                    var txt = content is { ValueKind: JsonValueKind.String } ? content.Value.GetString()! : string.Join(' ', Enumerate(content).Select(p => Str(p, "text")));
                    fails.Add(new LiveEvent(LiveMark.FailedTool, Clip(FirstTwo(txt), 200)));
                }

                return fails;

            case "result":
                // is_error marks a failed session even when the subtype says success.
                if (IsTrue(Prop(root, "is_error")))
                {
                    return [Error($"session failed: {Clip(Str(root, "result"), 300)}")];
                }

                var cost = Prop(root, "total_cost_usd") is { ValueKind: JsonValueKind.Number } usd
                    ? ", $" + usd.GetDouble().ToString("0.00", CultureInfo.InvariantCulture)
                    : "";
                return [new LiveEvent(LiveMark.SessionEnd, $"(session ended: {Str(root, "subtype")}, {Raw(Prop(root, "num_turns"))} turns{cost})")];

            default:
                return [];
        }
    }

    private static LiveEvent Error(string text) => new(LiveMark.Error, text);

    private static string ToolLine(string label, string what) =>
        what.Length == 0 ? label : $"{label}: {Clip(SpaceRx.Replace(what, " "), 80)}";

    private static IEnumerable<string> Paragraphs(string text) =>
        BlankLineRx.Split(text).Select(p => SpaceRx.Replace(p, " ").Trim()).Where(p => p.Length > 0);

    private static IEnumerable<string> FirstTwoLines(string text) =>
        LineRx.Split(text).Where(l => l.Trim().Length > 0).Take(2);

    private static string FirstTwo(string text) => string.Join(" | ", FirstTwoLines(text));

    private static string Clip(string s, int n) => s.Length > n ? s[..n] + " ..." : s;

    private static string OrDefault(string first, string second) => first.Length > 0 ? first : second;

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

    private static JsonElement? Prop(JsonElement? element, string name)
    {
        if (element is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null)
        {
            return value;
        }

        return null;
    }

    private static string Str(JsonElement? element, string name) => Prop(element, name) is { } value && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? ""
        : "";

    private static double? Num(JsonElement? element, string name) => Prop(element, name) is { } value && value.ValueKind == JsonValueKind.Number
        ? value.GetDouble()
        : null;

    private static bool IsTrue(JsonElement? element) => element is { ValueKind: JsonValueKind.True };

    private static string Raw(JsonElement? element) => element is { } value ? value.GetRawText() : "";

    private static IEnumerable<JsonElement> Enumerate(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Array } array => array.EnumerateArray(),
        { } single when single.ValueKind != JsonValueKind.Null && single.ValueKind != JsonValueKind.Undefined => [single],
        _ => [],
    };
}
