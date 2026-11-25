// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Host.Processes;

/// <summary>
/// Each json-format worker's readable log, written as it runs (460-B; schedule.ps1's
/// <c>Render-New</c>): what is new in the raw <c>.jsonl</c> since the last read is scanned with
/// Core's <see cref="WorkerLogScan"/> (the PAGE-10 stats) and rendered with
/// <see cref="WorkerEventRender"/> onto the end of the readable <c>.log</c>. Only whole lines are
/// taken while the worker runs, so a line still being written (and a UTF-8 character) is never
/// cut in two; at the finish whatever is left is taken as well. A text agent's raw log is its
/// readable log and is left alone.
/// <para>
/// 964-B: two callers read the same raw log. The tick's watch calls <see cref="Advance"/> once a
/// tick, and the page's live feed calls <see cref="CatchUp"/> on each build while a page is asking
/// (PAGE-3), so text reaches the window within one 2 s poll instead of once a tick. Each run's
/// offset is kept here, under one lock, and both start from it: nothing is rendered twice, and each
/// call reads only the bytes added since the last one.
/// </para>
/// </summary>
public sealed class WorkerLogRenderer(IClock clock)
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly Dictionary<int, Live> _live = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// A copy of the live stats of the worker in <paramref name="slot"/>, or null when none have been
    /// scanned. A copy, since the page reads it on its own thread while a render may be adding to it.
    /// </summary>
    public WorkerStats? Stats(int slot)
    {
        lock (_gate)
        {
            if (!_live.TryGetValue(slot, out var l))
            {
                return null;
            }

            var s = l.Stats;
            return new WorkerStats
            {
                Calls = s.Calls,
                Tools = s.Tools,
                ToolFails = s.ToolFails,
                CtxChars = s.CtxChars,
                Compactions = s.Compactions,
                GenTokens = s.GenTokens,
                GenSeconds = s.GenSeconds,
                LastTps = s.LastTps,
                LastTtft = s.LastTtft,
            };
        }
    }

    /// <summary>
    /// Render what <paramref name="w"/> has added to its raw log and return its new raw position.
    /// <paramref name="final"/> takes a last line with no line end too (the worker has ended).
    /// Once a run has been read, its own offset here wins over <see cref="RunningWorker.RawPosition"/>,
    /// so what <see cref="CatchUp"/> already rendered is not rendered again (964-B).
    /// </summary>
    public long Advance(RunningWorker w, AgentFormat format, bool final)
    {
        if (format == AgentFormat.Text || w.Log.Length == 0 || w.Log == w.RawLog)
        {
            return w.RawPosition;
        }

        lock (_gate)
        {
            var live = LiveOf(w, format);
            Render(live, final);
            return live.Position;
        }
    }

    /// <summary>
    /// 964-B: render what each running json-format worker has added since the last read, for the
    /// page's live feed (PAGE-3). Only runs the tick has watched are known, and only new bytes are read.
    /// </summary>
    public void CatchUp()
    {
        lock (_gate)
        {
            foreach (var live in _live.Values)
            {
                Render(live, false);
            }
        }
    }

    /// <summary>
    /// 1191-B: after a scheduler restart, start an adopted worker's stats from the figures running.json
    /// saved, and its scan from its saved raw position, instead of from zero.
    /// </summary>
    public void Seed(RunningWorker w, WorkerStats stats)
    {
        lock (_gate)
        {
            var live = new Live(w.Pid, w.Start) { Position = w.RawPosition };
            var s = live.Stats;
            s.Calls = stats.Calls;
            s.Tools = stats.Tools;
            s.ToolFails = stats.ToolFails;
            s.CtxChars = stats.CtxChars;
            s.Compactions = stats.Compactions;
            s.GenTokens = stats.GenTokens;
            s.GenSeconds = stats.GenSeconds;
            s.LastTps = stats.LastTps;
            s.LastTtft = stats.LastTtft;
            _live[w.Slot] = live;
        }
    }

    /// <summary>Drop the slot's stats and offset once its worker has finished.</summary>
    public void Forget(int slot)
    {
        lock (_gate)
        {
            _live.Remove(slot);
        }
    }

    private Live LiveOf(RunningWorker w, AgentFormat format)
    {
        if (!_live.TryGetValue(w.Slot, out var live) || live.Pid != w.Pid || live.Start != w.Start)
        {
            live = new Live(w.Pid, w.Start) { Position = w.RawPosition };
            _live[w.Slot] = live;
        }

        live.RawLog = w.RawLog;
        live.Log = w.Log;
        live.Format = format;
        return live;
    }

    // Read the run's new whole lines (everything left when final), render them onto its readable
    // log and move its offset past them. An append that fails leaves the offset for the next read.
    private void Render(Live live, bool final)
    {
        var (text, position) = ReadNew(live.RawLog, live.Position, final);
        if (text.Length == 0)
        {
            live.Position = position;
            return;
        }

        var scan = live.Format switch
        {
            AgentFormat.Claude => WorkerLogScan.Claude(text, live.Stats, live.Partial, clock),
            AgentFormat.Codex => WorkerEvents.ScanCodex(text, live.Stats, live.Partial),
            AgentFormat.OpenCode => WorkerEvents.ScanOpenCode(text, live.Stats, live.Partial),
            _ => WorkerLogScan.Copilot(text, live.Stats, live.Partial),
        };
        live.Partial = scan.Partial;
        var render = live.Format switch
        {
            AgentFormat.Claude => (Func<string, IReadOnlyList<string>>)WorkerEventRender.Claude,
            AgentFormat.Codex => CodexLines,
            AgentFormat.OpenCode => OpenCodeLines,
            _ => WorkerEventRender.Copilot,
        };
        var lines = scan.Lines.SelectMany(render).ToList();
        if (lines.Count > 0)
        {
            try
            {
                File.AppendAllText(live.Log, string.Join("\r\n", lines) + "\r\n", Utf8);
            }
            catch (IOException)
            {
                return; // read again next time
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }

        live.Position = position;
    }

    // A Codex or OpenCode event line as readable lines: the page's own events, marked as the other formats' are,
    // so the live view and the readable log cannot disagree (AG-16).
    private static IReadOnlyList<string> CodexLines(string line) => AgentLines(AgentFormat.Codex, line);

    private static IReadOnlyList<string> OpenCodeLines(string line) => AgentLines(AgentFormat.OpenCode, line);

    private static IReadOnlyList<string> AgentLines(AgentFormat format, string line) =>
        [.. WorkerEvents.Render(format, line).Select(e => e.Mark switch
        {
            LiveMark.Message or LiveMark.ToolCall or LiveMark.Error => e.Prefix + " " + e.Text,
            LiveMark.FailedTool or LiveMark.Reasoning => "  " + e.Prefix + " " + e.Text,
            _ => "  " + e.Text,
        })];

    // The whole lines after position, and the position after them; everything left when final.
    private static (string Text, long Position) ReadNew(string path, long position, bool final)
    {
        try
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                return ("", position);
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < position)
            {
                position = 0; // the file was started again
            }

            if (fs.Length == position)
            {
                return ("", position);
            }

            fs.Seek(position, SeekOrigin.Begin);
            var buf = new byte[fs.Length - position];
            var n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            var take = final ? n : Array.LastIndexOf(buf, (byte)'\n', n - 1) + 1;
            if (take <= 0)
            {
                return ("", position);
            }

            return (Utf8.GetString(buf, 0, take), position + take);
        }
        catch (IOException)
        {
            return ("", position);
        }
        catch (UnauthorizedAccessException)
        {
            return ("", position);
        }
    }

    private sealed class Live(int pid, DateTimeOffset start)
    {
        public int Pid { get; } = pid;

        public DateTimeOffset Start { get; } = start;

        public WorkerStats Stats { get; } = new();

        public string Partial { get; set; } = "";

        public long Position { get; set; }

        public string RawLog { get; set; } = "";

        public string Log { get; set; } = "";

        public AgentFormat Format { get; set; }
    }
}
