using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.PageModel;

/// <summary>
/// The raw log scanner (PAGE-3, PAGE-10): a faithful port of the compiled <c>LiveScan</c> of
/// schedule.ps1 (L1958-2163). It reads a whole chunk of a worker's log at once, updates the
/// <see cref="WorkerStats"/> as it goes, keeps the line the worker is still writing, and returns
/// only the whole lines worth showing - the streamed pieces that make up most of the log feed the
/// stats and the partial line, and never become lines of their own.
/// </summary>
public static class WorkerLogScan
{
    private const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const RegexOptions Cs = RegexOptions.CultureInvariant;

    private static readonly Regex TypeRx = new(@"^\{""type"":""([a-z_.]+)""", Cs);
    private static readonly Regex DeltaTypeRx = new(@"^assistant\.(reasoning|message|tool_call)_delta$", Ci);
    private static readonly Regex DeltaRx = new(@"""(?:deltaContent|inputDelta)"":""((?:[^""\\]|\\.)*)""", Cs);
    private static readonly Regex ContentRx = new(@"""deltaContent"":""((?:[^""\\]|\\.)*)""", Cs);
    private static readonly Regex TimeRx = new(@"""timestamp"":""([^""]+)""", Cs);
    private static readonly Regex SkipRx = new(@"^\{""type"":""[a-z_.]+_delta""", Ci);
    private static readonly Regex ShowRx = new(@"""type"":""(assistant\.message|assistant\.reasoning|tool\.execution_complete|model\.call_finished|[a-z_.]*error[a-z_.]*)""", Ci);

    private static readonly Regex CTypeRx = new(@"^\{""type"":""([a-z_]+)""", Cs);
    private static readonly Regex CEventRx = new(@"""event"":\{""type"":""([a-z_]+)""", Cs);
    private static readonly Regex CDeltaRx = new(@"""delta"":\{""type"":""(text_delta|thinking_delta|input_json_delta)"",""(?:text|thinking|partial_json)"":""((?:[^""\\]|\\.)*)""", Cs);
    private static readonly Regex CUsageRx = new(@"""(input_tokens|cache_creation_input_tokens|cache_read_input_tokens)"":(\d+)", Cs);

    /// <summary>Scan a chunk of a Copilot worker's log (L2142).</summary>
    public static WorkerScanResult Copilot(string text, WorkerStats stats, string partial)
    {
        var show = new List<string>();
        var p = partial ?? "";
        foreach (var line in text.Split('\n'))
        {
            Stats(line, stats);
            p = Partial(line, p);
            if (SkipRx.IsMatch(line))
            {
                continue;
            }

            var t = line.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            if (t[0] != '{' || ShowRx.IsMatch(t))
            {
                show.Add(line);
            }
        }

        return new WorkerScanResult(p, show);
    }

    /// <summary>
    /// Scan a chunk of a Claude worker's stream-json (L2080). Its events carry no timestamps, so
    /// the call timings are taken from <paramref name="clock"/> as they arrive.
    /// </summary>
    public static WorkerScanResult Claude(string text, WorkerStats stats, string partial, IClock clock)
    {
        var show = new List<string>();
        var p = partial ?? "";
        foreach (var raw in text.Split('\n'))
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

            var type = MatchGroup(CTypeRx, t);
            if (type == "stream_event")
            {
                var et = MatchGroup(CEventRx, t);
                switch (et)
                {
                    case "message_start":
                        stats.Calls++;
                        stats.CallStart = clock.Now;
                        stats.FirstDelta = null;
                        stats.Deltas = 0;
                        break;
                    case "content_block_delta":
                        var d = CDeltaRx.Match(t);
                        if (d.Success)
                        {
                            stats.FirstDelta ??= clock.Now;
                            stats.Deltas += d.Groups[2].Value.Length / 3.5;
                            if (d.Groups[1].Value != "input_json_delta")
                            {
                                p = Cut(p + Unescape(d.Groups[2].Value));
                            }
                        }

                        break;
                    case "message_stop":
                        var end = clock.Now;
                        if (stats.CallStart is { } start && stats.FirstDelta is { } first)
                        {
                            stats.LastTtft = Math.Round((first - start).TotalSeconds, 1);
                            var gen = (end - first).TotalSeconds;
                            if (gen >= 0.5 && stats.Deltas >= 3)
                            {
                                stats.LastTps = Math.Round(stats.Deltas / gen, 1);
                                stats.GenTokens += stats.Deltas;
                                stats.GenSeconds += gen;
                            }
                        }

                        stats.CallStart = null;
                        stats.FirstDelta = null;
                        stats.Deltas = 0;
                        break;
                }

                continue;
            }

            if (type == "assistant")
            {
                // The message is finished: the line being written starts again from nothing.
                p = "";
                var ctx = 0L;
                foreach (Match u in CUsageRx.Matches(t))
                {
                    ctx += long.Parse(u.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                if (ctx > 0)
                {
                    stats.CtxChars = ctx * 3.5;
                }

                show.Add(raw);
                continue;
            }

            if (type == "user")
            {
                var at = 0;
                while ((at = t.IndexOf("\"type\":\"tool_result\"", at, StringComparison.Ordinal)) >= 0)
                {
                    stats.Tools++;
                    at++;
                }

                if (t.Contains("\"is_error\":true", StringComparison.Ordinal))
                {
                    stats.ToolFails++;
                    show.Add(raw);
                }

                continue;
            }

            if (type == "result")
            {
                show.Add(raw);
                continue;
            }

            if (type == "system" && t.Contains("\"subtype\":\"compact_boundary\"", StringComparison.Ordinal))
            {
                stats.Compactions++;
            }
        }

        return new WorkerScanResult(p, show);
    }

    private static void Stats(string line, WorkerStats stats)
    {
        if (line.Length < 12 || line[0] != '{')
        {
            return;
        }

        var type = MatchGroup(TypeRx, line);
        if (type is null)
        {
            return;
        }

        if (DeltaTypeRx.IsMatch(type))
        {
            stats.FirstDelta ??= EventTime(line);
            var dm = DeltaRx.Match(line);
            if (dm.Success)
            {
                stats.Deltas += dm.Groups[1].Value.Length / 3.5;
            }

            return;
        }

        switch (type)
        {
            case "model.call_start":
                stats.Calls++;
                stats.CallStart = EventTime(line);
                stats.FirstDelta = null;
                stats.Deltas = 0;
                break;

            case "model.call_finished":
                var end = EventTime(line);
                if (stats.CallStart is { } start && stats.FirstDelta is { } first && end is { } at)
                {
                    stats.LastTtft = Math.Round((first - start).TotalSeconds, 1);
                    var gen = (at - first).TotalSeconds;
                    if (gen >= 0.5 && stats.Deltas >= 3)
                    {
                        stats.LastTps = Math.Round(stats.Deltas / gen, 1);
                        stats.GenTokens += stats.Deltas;
                        stats.GenSeconds += gen;
                    }
                }

                stats.CallStart = null;
                stats.FirstDelta = null;
                stats.Deltas = 0;
                break;

            case "user.message":
            case "assistant.message":
                stats.CtxChars += Math.Max(0, line.Length - 300);
                break;

            case "tool.execution_complete":
                stats.Tools++;
                if (line.Contains("\"success\":false", StringComparison.Ordinal))
                {
                    stats.ToolFails++;
                }

                stats.CtxChars += Math.Max(0, line.Length - 300);
                break;

            default:
                if (type.Contains("compact", StringComparison.OrdinalIgnoreCase))
                {
                    stats.Compactions++;
                    stats.CtxChars = 0;
                }

                break;
        }
    }

    private static string Partial(string line, string p)
    {
        if (line.Length < 20 || line[0] != '{')
        {
            return p;
        }

        if (line.StartsWith("{\"type\":\"assistant.reasoning_delta\"", StringComparison.Ordinal)
            || line.StartsWith("{\"type\":\"assistant.message_delta\"", StringComparison.Ordinal))
        {
            var m = ContentRx.Match(line);
            if (m.Success)
            {
                p += Unescape(m.Groups[1].Value);
                if (p.Length > 20_000)
                {
                    p = "... " + p[CutAt(p, 16_000)..];
                }
            }

            return p;
        }

        if (line.StartsWith("{\"type\":\"assistant.reasoning\"", StringComparison.Ordinal)
            || line.StartsWith("{\"type\":\"assistant.message\"", StringComparison.Ordinal)
            || line.StartsWith("{\"type\":\"model.call_start\"", StringComparison.Ordinal))
        {
            return "";
        }

        return p;
    }

    private static string Cut(string p) => p.Length > 1600 ? "... " + p[CutAt(p, 800)..] : p;

    /// <summary>Where to cut a long partial line: at the last space of its tail, so no word is cut.</summary>
    private static int CutAt(string p, int tail)
    {
        var cut = p.IndexOf(' ', p.Length - tail);
        return cut < 0 ? p.Length - tail : cut + 1;
    }

    private static string Unescape(string piece)
    {
        try
        {
            return Regex.Unescape(piece);
        }
        catch (ArgumentException)
        {
            return piece;
        }
    }

    private static string? MatchGroup(Regex rx, string input)
    {
        var m = rx.Match(input);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static DateTimeOffset? EventTime(string line)
    {
        var m = TimeRx.Match(line);
        if (m.Success
            && DateTimeOffset.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return at;
        }

        return null;
    }
}
