// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>
/// The live snapshot of PAGE-3: what the running workers' logs, stats and timers look like right
/// now, as the JSON the page polls. It is built at most thirty times a second (966-B), only while
/// the page has asked for it in the last five seconds, and never sooner after a build than twice
/// that build's own time. The clock comes in as <see cref="IClock"/>, so the rate is testable.
/// <para>
/// 610-C numbered the lines. Each worker's rendered lines are numbered from the start of its run
/// (line 1 is the first line it wrote), one build keeps the last <see cref="LiveLog.HistoryLines"/>
/// of them, and <see cref="Answer"/> returns what a page has not seen yet - so the page appends
/// instead of replacing its window - plus the line being written. The build is shared by every page
/// that asks; the answer is worked out per request from the cursor each page sent.
/// </para>
/// </summary>
public sealed class LiveFeed(IClock clock, IPageData data)
{
    /// <summary>The shortest gap between two builds: thirty a second (966-B, the page's fast mode).</summary>
    public static readonly TimeSpan BuildInterval = TimeSpan.FromMilliseconds(1000d / 30);

    /// <summary>
    /// The page's action queue, whose state <see cref="Answer"/> reports so the page knows when the
    /// click it made has landed (957-B). The page server binds its own queue here; a feed with no
    /// queue answers with an empty list.
    /// </summary>
    public PageActionQueue? Actions { get; set; }

    /// <summary>How recently the page must have asked for the live view for builds to go on.</summary>
    public static readonly TimeSpan AskWindow = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private DateTimeOffset? _lastAsk;
    private DateTimeOffset? _lastFastAsk;
    private DateTimeOffset? _lastBuildStart;
    private TimeSpan _lastBuild;
    private DateTimeOffset? _lastClick;
    private object? _versionOf;
    private string _version = "";
    private string _build = "";   // 1109-C: the running build's version (3.0.116), "" when the snapshot names none
    private string _checking = "";
    private string _views = "";
    private volatile BuiltLive _built = new(DateTimeOffset.UnixEpoch, false, new Dictionary<WorkerKey, SlotLog>());

    // 1092-C: the last stats line each worker showed that carried its average T/s, with the run it
    // belongs to. A build can read a worker at a moment its stats line has no numbers in it (its
    // readable log was started again, or its run has not been sampled yet); the line the page already
    // had is published again in that case, so one build cannot blank a window. A new run - a new log
    // file - is not covered by it, and a worker that never had stats keeps the dash.
    private readonly Dictionary<WorkerKey, (string Run, string Stats)> _lastStats = [];

    // Two pages polling at once can reach a build together (966-B), and this one writes the memory.
    private readonly Lock _statsGate = new();

    /// <summary>The last build, from before the first one.</summary>
    public LiveSnapshot Snapshot { get; private set; } = new() { T = DateTimeOffset.UnixEpoch };

    /// <summary>The JSON of the last build as a fresh page would be answered: no cursor, so the last
    /// 500 lines of each run. <c>/live.json</c> answers with <see cref="Answer"/>, which trims it to
    /// what the asking page has not seen.</summary>
    public string Json { get; private set; } = "{\"t\":\"\",\"pageDone\":false,\"page\":\"\",\"checking\":\"\",\"views\":\"\",\"slots\":{}}";

    /// <summary>How many builds this feed has made.</summary>
    public int Builds { get; private set; }

    /// <summary>How long the last build took.</summary>
    public TimeSpan LastBuild => _lastBuild;

    /// <summary>Whether the page asked for the live view within the last five seconds.</summary>
    public bool Active => _lastAsk is { } asked && clock.Now - asked <= AskWindow;

    /// <summary>
    /// Whether the page snapshot already shows the newest click, so a page waiting on one knows it
    /// can refresh (PAGE-3).
    /// </summary>
    public bool PageDone => _lastClick is not { } click || data.Snapshot().Built >= click;

    /// <summary>The page asked for the live view.</summary>
    public void Ask()
    {
        var now = clock.Now;
        if (_lastAsk is { } before && now - before <= FastGap)
        {
            _lastFastAsk = now;
        }

        _lastAsk = now;
    }

    /// <summary>
    /// 966-B: two asks this close together mean a page is in fast mode (it asks about thirty times a
    /// second for fifteen seconds after any click or key, against every two seconds otherwise).
    /// </summary>
    public static readonly TimeSpan FastGap = TimeSpan.FromMilliseconds(300);

    /// <summary>966-B: how long fast mode lasts after the last fast ask.</summary>
    public static readonly TimeSpan FastHold = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 966-B: whether a page is in fast mode now, so the scheduler loop wakes at the same rate and picks
    /// up actions and events at once.
    /// </summary>
    public bool Fast => _lastFastAsk is { } fast && clock.Now - fast <= FastHold;

    /// <summary>A click was queued for the scheduler thread (PAGE-1).</summary>
    public void NoteClick() => _lastClick = clock.Now;

    /// <summary>Whether a build is due now: the page wants it, and the last one is old enough.</summary>
    public bool BuildDue()
    {
        if (!Active)
        {
            return false;
        }

        if (_lastBuildStart is not { } start)
        {
            return true;
        }

        var since = clock.Now - start;
        return since >= BuildInterval && since >= _lastBuild + _lastBuild;
    }

    /// <summary>
    /// Makes a build: the last 500 rendered lines of each running worker's run, numbered from its
    /// start, with the line being written, its stats line and its timer (PAGE-3, 610-C). A worker's
    /// stats line is only ever moved forward: one read without numbers in it is answered with the last
    /// line that had them, for the same run (1092-C).
    /// </summary>
    public void Build()
    {
        var start = clock.Now;
        var slots = new Dictionary<WorkerKey, SlotLog>();
        var shown = new Dictionary<int, LiveSlot>();
        var stats = new Dictionary<WorkerKey, (string Run, string Stats)>();
        foreach (var worker in data.RunningWorkers())
        {
            var (lines, partial) = LiveLog.ReadFile(worker.LogPath);

            // WT-W3: a worker is (project, slot), since two projects of a pool each have a slot 1.
            var key = new WorkerKey(worker.Project, worker.Slot);
            var run = Path.GetFileName(worker.LogPath);

            // 1092-C: a stats line with no average in it never replaces the last real one of the same
            // run, so the page is not asked to draw a dash for a worker whose numbers it already has.
            var line = worker.Stats;
            if (HasTps(line))
            {
                stats[key] = (run, line);
            }
            else
            {
                string? keep = null;
                lock (_statsGate)
                {
                    if (_lastStats.TryGetValue(key, out var held) && held.Run == run && held.Run.Length > 0)
                    {
                        keep = held.Stats;
                    }
                }

                line = keep ?? line;
            }

            var entry = new SlotLog(
                run,
                lines.Count,
                LiveLog.History(lines, LiveLog.HistoryLines),
                LiveLog.Marker(partial),
                line,
                worker.Time);

            // 1093-C: the page's window for this slot may already name another card - its worker ended
            // and the next card was dispatched while this entry still holds the finished worker's text.
            // The window gets the starting line instead of another card's lines.
            var starting = StaleCard(key, run);
            if (starting is { } isCard)
            {
                slots[key] = entry with { Next = 0, Lines = [], Partial = "", Stats = "", Time = "" };
                shown[worker.Slot] = new LiveSlot { Log = [StartingLine(isCard)], Stats = "", Time = "" };
                continue;
            }

            slots[key] = entry;
            shown[worker.Slot] = new LiveSlot { Log = entry.Shown(), Stats = line, Time = worker.Time };
        }

        _lastBuild = clock.Now - start;
        _lastBuildStart = start;
        Builds++;
        _built = new BuiltLive(start, PageDone, slots);
        var version = Version();
        Snapshot = new LiveSnapshot { T = start, PageDone = PageDone, Slots = shown, Views = _views };
        Json = Write(_built, version, _checking, _views, NoCursor, Actions);

        // What each running worker last really showed, for the next build (1092-C). The keys are
        // (project, slot), so this holds at most one entry per worker place and needs no clean-up.
        lock (_statsGate)
        {
            foreach (var (key, kept) in stats)
            {
                _lastStats[key] = kept;
            }
        }
    }

    // Whether a stats line carries the average the title bar reads off it (the "<n> avg" the page
    // script looks for). An empty line, or one left with only the model and its server, does not.
    private static bool HasTps(string stats) => stats.Contains(" avg", StringComparison.Ordinal);

    // 1093-C: a worker's readable log is named <card>-<yyyyMMdd>-<HHmmss>.log (WRK-9), so the file a
    // run was written to carries the card it ran, and the page's window carries the card it shows
    // (its data-card, as PageRenderer writes it: the number and the category letter). Both sides are
    // read the same way here, so a run answers to the window it belongs to.
    private static readonly Regex RunStamp = new(@"-\d{8}-\d{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CardNumber = new(@"^\d+-[A-Za-z]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The card a name stands for: the log file's or the page's, reduced to what both carry.</summary>
    private static string CardOf(string name)
    {
        if (name.Length == 0)
        {
            return "";
        }

        // A card's file name is <n>-<L>-<slug> (jobs/README), so both sides start with the card's
        // number and category, whatever the log's stamp or the card's extension left behind them.
        var number = CardNumber.Match(name);
        return number.Success
            ? number.Value
            : RunStamp.Replace(Path.GetFileNameWithoutExtension(name), "");
    }

    /// <summary>The line a window whose worker has not started writing yet shows in place of a log.</summary>
    private static string StartingLine(string card) => "(starting " + card + "...)";

    /// <summary>
    /// The card the page's window for this worker shows now, or "" when the feed cannot tell - the
    /// worker has a project the data does not know, or the page has no window for the slot.
    /// </summary>
    private string WindowCard(WorkerKey key)
    {
        IPageData? source = data;
        if (key.Project.Length > 0)
        {
            source = data is IPoolPageData pool ? pool.For(key.Project) : null;
        }

        if (source is null)
        {
            return "";
        }

        foreach (var slot in source.Snapshot().Slots)
        {
            if (slot.Slot == key.Slot)
            {
                return CardOf(slot.Card);
            }
        }

        return "";
    }

    /// <summary>
    /// 1093-C: the card the slot's window shows when it is not the card <paramref name="run"/> was
    /// written by - a build that has not caught up with a slot that moved to the next card. Null when
    /// the entry is the window's own, or when the window's card cannot be read.
    /// </summary>
    private string? StaleCard(WorkerKey key, string run)
    {
        var own = CardOf(run);
        var shown = WindowCard(key);
        if (own.Length == 0 || shown.Length == 0 || string.Equals(own, shown, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return shown;
    }

    /// <summary>
    /// The answer for one page (PAGE-3, 610-C): every slot's lines after the number that page reached,
    /// its line being written, and the number it should ask for next. The cursors arrive as
    /// <c>4:1234,1:87</c>; a slot the page says nothing about gets the whole window the build holds,
    /// and so does a slot whose cursor is newer than its run, which is a run that was replaced.
    /// The page's own queued actions ride along (957-B), read as they stand at this moment rather than
    /// as the last build saw them, since the page is waiting on exactly those.
    /// </summary>
    public string Answer(string? since)
    {
        var version = Version();
        return Write(_built, version, _checking, _views, ParseSince(since), Actions);
    }

    // The page snapshot's version, and the sha of the health check it shows as running; worked out
    // again only when the host has built a new snapshot.
    private string Version()
    {
        // 1004-C: a pool's page carries the version of every project's snapshot together
        // (PageVersion.Of(list), as CombinedPageRenderer writes it), so the pool's feed answers with
        // that one. One project's version never matched it, and the pool page reloaded on every poll:
        // an open dropdown was rebuilt, Worker types closed again, and clicks landed on replaced rows.
        // The pool page has no single Check main button, so there is no checking sha to carry.
        if (data is IPoolPageData pool)
        {
            var snapshot = pool.PoolSnapshot();
            if (!ReferenceEquals(snapshot, _versionOf))
            {
                _version = PageVersion.Of(snapshot.Pages);
                _build = snapshot.Pages.Select(p => p.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
                _checking = "";
                // 1090-B, VW-20: the views carry a version of their own, out of the page's.
                _views = ProjectViews.VersionOf(snapshot.Views);
                _versionOf = snapshot;
            }

            return _version;
        }

        var page = data.Snapshot();
        if (!ReferenceEquals(page, _versionOf))
        {
            _version = PageVersion.Of(page);
            _build = page.Version ?? "";
            _checking = Views.HealthChecking.Sha(page.Health);
            _versionOf = page;
        }

        return _version;
    }

    /// <summary>
    /// The JSON of PAGE-3: <c>{t, pageDone, page, checking, views, slots:{n:{log, partial, from, next, run,
    /// stats, time, starting}}}</c>, where <c>page</c> is the page snapshot's <see cref="PageVersion"/> (470-C),
    /// <c>checking</c> is the sha of the health check the snapshot shows as running or "" (930-C, what
    /// makes the Check main button look pressed), <c>views</c> is every project's views version (VW-20, 1090-B;
    /// "" without a pool), <c>build</c> is the version of the build that is running (1109-C: an open page of
    /// another build reloads itself), and each slot carries the numbered lines that page
    /// has not seen (610-C): <c>from</c> is the number of the first line in <c>log</c>, <c>next</c> the
    /// number of the last line of the run, and <c>run</c> the log the lines came from, which changes
    /// when the slot starts another run. <c>starting</c> is the card the slot's window shows when none
    /// of these lines belong to it (1093-C), else "" - and then <c>log</c> is empty, so an answer that
    /// trailed the page cannot keep another card's text on screen.
    /// </summary>
    private string Write(
        BuiltLive built,
        string page,
        string checking,
        string views,
        IReadOnlyDictionary<WorkerKey, int> since,
        PageActionQueue? actions)
    {
        var slots = new Dictionary<string, LiveJsonSlot>();
        foreach (var (key, log) in built.Slots)
        {
            // 1093-C: the build this answer comes from can trail the page by a poll: the window it
            // belongs to already names the next card, so these lines are the previous card's. They
            // never go out under the new window - the answer says what is starting, with no lines and
            // no cursor to keep, so the next ask carries the new run's whole window.
            var starting = StaleCard(key, log.Run);
            if (starting is not null)
            {
                slots[key.JsonKey] = new LiveJsonSlot([], "", 1, 0, "", "", "", starting);
                continue;
            }

            var cursor = since.TryGetValue(key, out var reached) ? Math.Max(0, reached) : 0;

            // The window the build holds, and where its first line sits in the run's numbering.
            var first = Math.Max(1, log.Next - log.Lines.Count + 1);

            // A page that has nothing, or one whose cursor is newer than this run (its slot was
            // started again), gets the whole window; any other page gets only what it missed.
            var from = cursor == 0 || cursor > log.Next ? first : Math.Max(first, cursor + 1);
            string[] lines = from > log.Next ? [] : [.. log.Lines.Skip(from - first)];

            slots[key.JsonKey] = new LiveJsonSlot(
                lines,
                log.Partial,
                lines.Length == 0 ? log.Next + 1 : from,
                log.Next,
                log.Run,
                log.Stats,
                log.Time,
                "");
        }

        return JsonSerializer.Serialize(
            new LiveJsonRoot(
                built.T.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                built.PageDone,
                page,
                checking,
                views,
                _build,
                slots,
                [.. (actions?.States() ?? []).Select(state => new LiveJsonAction(
                    state.Id,
                    state.Kind,
                    state.Card ?? "",
                    state.State,
                    state.Message))]),
            JsonOptions);
    }

    /// <summary>The cursors of a <c>/live.json</c> request: <c>4:1234,1:87</c> is slot 4 at line 1234
    /// and slot 1 at line 87; in a pool a worker is named by its project too, as
    /// <c>ProjectToolkit:4:1234</c> (WT-W3). Anything the page did not mean is left out, which asks
    /// for that worker's whole window.</summary>
    private static IReadOnlyDictionary<WorkerKey, int> ParseSince(string? since)
    {
        if (string.IsNullOrWhiteSpace(since))
        {
            return NoCursor;
        }

        Dictionary<WorkerKey, int>? cursors = null;
        foreach (var part in since.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':', 3);
            var (project, slotText, reachedText) = bits.Length switch
            {
                3 => (bits[0], bits[1], bits[2]),
                2 => ("", bits[0], bits[1]),
                _ => ("", "", ""),
            };
            if (int.TryParse(slotText, out var slot)
                && int.TryParse(reachedText, out var reached)
                && slot > 0
                && reached > 0)
            {
                cursors ??= new Dictionary<WorkerKey, int>();
                cursors[new WorkerKey(project, slot)] = reached;
            }
        }

        return cursors ?? NoCursor;
    }

    private static readonly IReadOnlyDictionary<WorkerKey, int> NoCursor = new Dictionary<WorkerKey, int>();

    /// <summary>A worker as the live view keys it (WT-W3): its project - empty is the single project
    /// - and its folder number.</summary>
    private readonly record struct WorkerKey(string Project, int Slot)
    {
        /// <summary>The name this worker's entry has in the JSON: <c>4</c>, or <c>Kestrel:4</c> in a
        /// pool; the page's cursor comes back in the same shape.</summary>
        public string JsonKey =>
            Project.Length == 0
                ? Slot.ToString(CultureInfo.InvariantCulture)
                : Project + ":" + Slot.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One slot as the last build saw it: the numbered lines it holds, the line being
    /// written, and the two text lines beside the log (PAGE-3, 610-C).</summary>
    private sealed record SlotLog(
        string Run,
        int Next,
        IReadOnlyList<string> Lines,
        string Partial,
        string Stats,
        string Time)
    {
        /// <summary>The window as a reader sees it: the kept lines with the line being written after
        /// them, or the placeholder when the worker has written nothing.</summary>
        public IReadOnlyList<string> Shown()
        {
            var all = new List<string>(Lines);
            if (Partial.Length > 0)
            {
                all.Add(Partial);
            }

            return all.Count == 0 ? ["(no output yet)"] : all;
        }
    }

    /// <summary>What one build of the feed found, one entry per running worker (project, slot).</summary>
    private sealed record BuiltLive(DateTimeOffset T, bool PageDone, IReadOnlyDictionary<WorkerKey, SlotLog> Slots);

    private sealed record LiveJsonSlot(
        [property: JsonPropertyName("log")] string[] Log,
        [property: JsonPropertyName("partial")] string Partial,
        [property: JsonPropertyName("from")] int From,
        [property: JsonPropertyName("next")] int Next,
        [property: JsonPropertyName("run")] string Run,
        [property: JsonPropertyName("stats")] string Stats,
        [property: JsonPropertyName("time")] string Time,
        [property: JsonPropertyName("starting")] string Starting);

    private sealed record LiveJsonAction(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("card")] string Card,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("msg")] string Message);

    private sealed record LiveJsonRoot(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("pageDone")] bool PageDone,
        [property: JsonPropertyName("page")] string Page,
        [property: JsonPropertyName("checking")] string Checking,
        [property: JsonPropertyName("views")] string Views,
        [property: JsonPropertyName("build")] string Build,
        [property: JsonPropertyName("slots")] Dictionary<string, LiveJsonSlot> Slots,
        [property: JsonPropertyName("actions")] LiveJsonAction[] Actions);
}
